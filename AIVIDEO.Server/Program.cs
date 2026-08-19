using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Pollo;
using AIVIDEO.Server.Services;
using AIVIDEO.Server.Storage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Options ----
builder.Services.Configure<PolloOptions>(builder.Configuration.GetSection(PolloOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

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

// ---- Application services ----
builder.Services.AddScoped<IAssetStore, LocalAssetStore>();
builder.Services.AddScoped<GenerationService>();
builder.Services.AddHostedService<PolloPollingService>();

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

app.UseDefaultFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
