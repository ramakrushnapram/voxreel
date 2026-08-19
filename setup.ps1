<#
.SYNOPSIS
    One-step setup for VoxReel: configures the database connection so registration and
    login work, then creates the schema.

.DESCRIPTION
    Accounts live in PostgreSQL, and the working connection string is stored in user-secrets
    (never committed). This script sets that up on a fresh clone.

.EXAMPLE
    ./setup.ps1 -PostgresPassword "mysecret"

.EXAMPLE
    ./setup.ps1          # prompts for the password
#>
param(
    [string]$PostgresPassword,
    [string]$PostgresUser = "postgres",
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$Database = "voxreel",
    [string]$PolloApiKey
)

$ErrorActionPreference = "Stop"
$server = Join-Path $PSScriptRoot "AIVIDEO.Server"

Write-Host "VoxReel setup" -ForegroundColor Cyan
Write-Host "=============="

# 1. Tooling checks
foreach ($tool in @("dotnet", "node")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: '$tool' is not installed or not on PATH." -ForegroundColor Red
        Write-Host "  Install it, then re-run this script. See SETUP.md."
        exit 1
    }
}
Write-Host "[ok] dotnet and node found."

# 2. Password
if (-not $PostgresPassword) {
    $secure = Read-Host "PostgreSQL password for user '$PostgresUser'" -AsSecureString
    $PostgresPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

$connString = "Host=$PostgresHost;Port=$PostgresPort;Database=$Database;Username=$PostgresUser;Password=$PostgresPassword"

# 3. Store secrets (never committed)
Write-Host "[..] Storing connection string in user-secrets"
dotnet user-secrets init --project $server | Out-Null
dotnet user-secrets set "ConnectionStrings:Default" $connString --project $server | Out-Null
Write-Host "[ok] Connection string saved."

# 4. Stable JWT key so sessions survive restarts
$existing = (dotnet user-secrets list --project $server) -join "`n"
if ($existing -notmatch "Jwt:Key") {
    $bytes = New-Object 'System.Byte[]' 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $key = [Convert]::ToBase64String($bytes)
    dotnet user-secrets set "Jwt:Key" $key --project $server | Out-Null
    Write-Host "[ok] Generated a stable JWT signing key."
} else {
    Write-Host "[ok] JWT key already set."
}

# 5. Optional Pollo key
if ($PolloApiKey) {
    dotnet user-secrets set "Pollo:ApiKey" $PolloApiKey --project $server | Out-Null
    Write-Host "[ok] Pollo API key saved (paid models enabled)."
}

# 6. Create schema (the app also does this on startup; doing it here surfaces auth errors early)
Write-Host "[..] Creating database schema"
$hasEf = (dotnet tool list --global | Select-String "dotnet-ef")
if (-not $hasEf) {
    Write-Host "     installing dotnet-ef tool"
    dotnet tool install --global dotnet-ef | Out-Null
    $env:PATH += ";$env:USERPROFILE\.dotnet\tools"
}
try {
    dotnet ef database update --project $server
    Write-Host "[ok] Database schema created." -ForegroundColor Green
} catch {
    Write-Host "[!!] Could not create the schema. The most likely cause is a wrong password." -ForegroundColor Yellow
    Write-Host "     Re-run: ./setup.ps1 -PostgresPassword <correct-password>"
    exit 1
}

Write-Host ""
Write-Host "Done. Start the app with:" -ForegroundColor Cyan
Write-Host "  dotnet run --project AIVIDEO.Server --launch-profile https"
Write-Host "Then open https://localhost:7244 and register."
