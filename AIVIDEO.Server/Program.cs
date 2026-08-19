using System.Text;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Pollo;
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
// The signing key is required. Rather than fail to boot (which would take down the whole
// site, including the pages that explain the problem), a development fallback key is
// generated so the app runs; tokens simply don't survive a restart. Production must supply
// a real Jwt:Key — enforced below.
var jwtSection = builder.Configuration.GetSection(JwtOptions.SectionName);
var jwt = jwtSection.Get<JwtOptions>() ?? new JwtOptions();

if (!jwt.IsConfigured)
{
    if (builder.Environment.IsDevelopment())
    {
        jwt.Key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        builder.Services.Configure<JwtOptions>(o => o.Key = jwt.Key);
    }
    else
    {
        throw new InvalidOperationException(
            "Jwt:Key is not configured. Set a key of at least 32 characters via user-secrets or environment variables.");
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
builder.Services.AddScoped<GenerationService>();
builder.Services.AddHostedService<PolloPollingService>();

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
// Applied automatically in development only. In any other environment migrations are a
// deploy step, because an app instance silently altering a shared schema at startup is how
// concurrent deploys corrupt a database.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied.");
    }
    catch (Exception ex)
    {
        // Starting anyway is deliberate: /api/system/status reports the database as
        // unreachable and the UI explains how to fix it, which beats a startup crash
        // with a stack trace and no guidance.
        logger.LogError(ex, "Could not apply migrations. The API will start, but database-backed endpoints will fail.");
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
