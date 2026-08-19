using System.Text;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Llm;
using AIVIDEO.Server.Pollo;
using AIVIDEO.Server.Providers;
using AIVIDEO.Server.Services;
using AIVIDEO.Server.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---- Options ----
builder.Services.Configure<PolloOptions>(builder.Configuration.GetSection(PolloOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection(OllamaOptions.SectionName));

// ---- Data (code-first, PostgreSQL) ----
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=voxreel;Username=postgres;Password=postgres";

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// ---- Pollo ----
// A generous timeout: submission is fast, but asset downloads run through the same client
// and a 1080p clip is not small.
builder.Services.AddHttpClient<IPolloClient, PolloClient>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

// ---- Auth ----
// The signing key must exist for auth to work. Previously the app threw on startup when no
// Jwt:Key was set outside Development — which crashed a fresh clone's backend (the key lives
// in user-secrets, never committed) and surfaced to users as a 502 on registration.
//
// Instead, when no key is configured we generate one and persist it to a gitignored file next
// to the app, so a clone runs with zero configuration AND sessions survive restarts (a random
// per-boot key would log everyone out on every restart). Setting Jwt:Key explicitly via
// user-secrets or an environment variable still takes precedence and is recommended for
// production.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (!jwt.IsConfigured)
{
    var keyFile = Path.Combine(builder.Environment.ContentRootPath, ".jwtkey");
    string key;
    if (File.Exists(keyFile))
    {
        key = File.ReadAllText(keyFile).Trim();
    }
    else
    {
        key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        try { File.WriteAllText(keyFile, key); } catch { /* read-only FS: fall back to an in-memory key for this run */ }
    }

    jwt.Key = key;
    builder.Services.Configure<JwtOptions>(o => o.Key = key);

    if (!builder.Environment.IsDevelopment())
    {
        // Not fatal, but worth flagging: a generated key is fine for a personal deploy, less so
        // for a shared production one where the key should be managed as a real secret.
        Console.WriteLine("[warn] Jwt:Key was not configured; using a generated key from .jwtkey. " +
                          "Set Jwt:Key explicitly for production.");
    }
}

builder.Services.AddScoped<AuthService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true,
            // Tokens expire exactly when they say they do; the default 5-minute grace is
            // unnecessary slack for a first-party API.
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ---- Application services ----
builder.Services.AddScoped<IAssetStore, LocalAssetStore>();

// The free image provider (Pollinations) is synchronous and can take 30s+, so its client
// gets a generous timeout independent of the Pollo client.
builder.Services.AddHttpClient<FreeImageProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
});

builder.Services.AddScoped<GenerationService>();
builder.Services.AddHostedService<PolloPollingService>();

// ---- Local LLM (Ollama) + RAG ----
var ollamaTimeout = builder.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>()?.TimeoutSeconds ?? 180;
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(ollamaTimeout);
});
builder.Services.AddScoped<LlmService>();

// Database outages become a 503 with a readable message rather than a 500 with a stack trace.
builder.Services.AddExceptionHandler<DatabaseExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// ---- Storage root ----
var storage = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
Directory.CreateDirectory(Path.GetFullPath(storage.Root));

// ---- Migrations ----
// Applied on startup in every environment. MigrateAsync also creates the database itself if
// it does not yet exist, so a fresh clone with a valid connection string comes up with the
// full schema and working registration/login — no manual "dotnet ef database update" step.
// (This is a single-instance, clone-and-run app; the usual caution about many instances
// racing to migrate a shared database does not apply here.)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database is ready (schema created/updated).");
    }
    catch (Exception ex)
    {
        // Starting anyway is deliberate: /api/system/status reports the database as
        // unreachable and the UI explains how to fix it, which beats a startup crash
        // with a stack trace and no guidance. The most common cause on a fresh clone is a
        // wrong password in ConnectionStrings:Default — set it via user-secrets or setup.ps1.
        logger.LogError(ex, "Could not reach or migrate the database. The API will start, but " +
            "registration/login and generations will fail until ConnectionStrings:Default is correct.");
    }
}

app.UseExceptionHandler();

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
