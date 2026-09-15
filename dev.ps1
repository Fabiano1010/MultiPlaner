[CmdletBinding()]
param (
    [Parameter(Position = 0)]
    [ValidateSet("help", "db-up", "db-down", "db-migrate", "db-add-migration", "api", "web", "android", "windows")]
    [string]$Command = "help",

    [Parameter(Position = 1)]
    [string]$MigrationName
)

$ErrorActionPreference = "Stop"
$ScriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$ComposeFile = Join-Path $ScriptDirectory "docker-compose.db.yml"
$ApiProject = Join-Path (Join-Path $ScriptDirectory "MultiPlanerAPI") "MultiPlanerAPI.csproj"
$WebProject = Join-Path (Join-Path $ScriptDirectory "MultiPlanerWeb") "MultiPlanerWeb.csproj"
$AppProject = Join-Path (Join-Path $ScriptDirectory "MultiPlanerApp") "MultiPlanerApp.csproj"
$AndroidRuntimeIdentifier = if ($env:ANDROID_RUNTIME_IDENTIFIER) { $env:ANDROID_RUNTIME_IDENTIFIER } else { "android-x64" }

function Invoke-Checked {
    param (
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter()]
        [string[]]$Arguments = @()
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Polecenie zakończyło się kodem $($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Invoke-Compose {
    param ([string[]]$Arguments = @())

    Invoke-Checked -FilePath "docker" -Arguments (@("compose", "-f", $ComposeFile) + $Arguments)
}

function Test-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker nie jest zainstalowany albo nie ma go w PATH."
    }

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Engine jest niedostępny. Uruchom Docker Desktop/daemon i sprawdź dostęp użytkownika do Docker Engine."
    }
}

function Show-DatabaseFailure {
    $status = (& docker inspect --format '{{.State.Status}}' MultiPlanerSQLDb 2>$null | Out-String).Trim()
    if ($status -eq "exited" -or $status -eq "dead") {
        Write-Host "❌ Kontener SQL Server zakończył pracę. Ostatnie logi:" -ForegroundColor Red
        & docker logs --tail 80 MultiPlanerSQLDb 2>&1 | Write-Host
        throw "Kontener MultiPlanerSQLDb nie działa."
    }
}

function Start-Database {
    $port = if ($env:DB_PORT) { [int]$env:DB_PORT } else { 1433 }

    Test-DockerAvailable
    Write-Host "▶ Uruchamianie bazy SQL Server..." -ForegroundColor Green
    Invoke-Compose -Arguments @("up", "-d")

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Show-DatabaseFailure
        $running = (& docker inspect --format '{{.State.Running}}' MultiPlanerSQLDb 2>$null | Out-String).Trim()
        if ($running -eq "true" -and (Test-DatabasePort -Port $port)) {
            Write-Host "✅ SQL Server przyjmuje połączenia na porcie $port." -ForegroundColor Green
            Invoke-Compose -Arguments @("ps")
            return
        }
        Start-Sleep -Seconds 1
    }

    Write-Host "❌ SQL Server nie zaczął przyjmować połączeń na porcie $port w ciągu 60 sekund." -ForegroundColor Red
    Invoke-Compose -Arguments @("ps")
    Show-DatabaseFailure
    throw "Nie udało się uruchomić SQL Servera."
}

function Test-DatabasePort {
    param ([Parameter(Mandatory = $true)][int]$Port)

    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        if (-not $connection.AsyncWaitHandle.WaitOne(1000)) {
            return $false
        }
        $client.EndConnect($connection)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Invoke-DotNetEf {
    param ([string[]]$Arguments = @())

    $dotnetEf = Get-Command "dotnet-ef" -ErrorAction SilentlyContinue
    if ($dotnetEf) {
        Invoke-Checked -FilePath $dotnetEf.Source -Arguments $Arguments
        return
    }

    $userProfile = [Environment]::GetFolderPath("UserProfile")
    $globalToolCandidates = @(
        (Join-Path $userProfile ".dotnet\tools\dotnet-ef.exe"),
        (Join-Path $userProfile ".dotnet\tools\dotnet-ef")
    )
    $globalTool = $globalToolCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($globalTool) {
        Invoke-Checked -FilePath $globalTool -Arguments $Arguments
        return
    }

    $toolManifest = Join-Path $ScriptDirectory ".config\dotnet-tools.json"
    if (Test-Path $toolManifest) {
        Invoke-Checked -FilePath "dotnet" -Arguments (@("tool", "run", "dotnet-ef", "--") + $Arguments)
        return
    }

    throw "Nie znaleziono narzędzia dotnet-ef. Zainstaluj je poleceniem: dotnet tool install --global dotnet-ef --version 10.*"
}

function Start-ApiBackground {
    Write-Host "▶ Uruchamianie Web API w osobnym oknie..." -ForegroundColor Cyan
    $arguments = "run --project `"$ApiProject`" --launch-profile https"
    if (Get-Command wt -ErrorAction SilentlyContinue) {
        Start-Process -FilePath "wt" -ArgumentList $arguments
    }
    else {
        $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { "pwsh" } else { "powershell" }
        Start-Process -FilePath $shell -ArgumentList "-NoExit", "-Command", "dotnet $arguments"
    }
    Start-Sleep -Seconds 3
}

switch ($Command) {
    "db-up" {
        Start-Database
    }
    "db-down" {
        Test-DockerAvailable
        Write-Host "▶ Zatrzymywanie bazy..." -ForegroundColor Yellow
        Invoke-Compose -Arguments @("down")
    }
    "db-migrate" {
        Start-Database
        Write-Host "▶ Wykonywanie migracji EF Core..." -ForegroundColor Green
        Invoke-DotNetEf -Arguments @("database", "update", "--project", $ApiProject, "--startup-project", $ApiProject)
    }
    "db-add-migration" {
        $name = if ($MigrationName) { $MigrationName } else { "AutoMigration_$(Get-Date -Format yyyyMMddHHmmss)" }
        Write-Host "▶ Tworzenie migracji: $name..." -ForegroundColor Green
        Invoke-DotNetEf -Arguments @("migrations", "add", $name, "--project", $ApiProject, "--startup-project", $ApiProject)
    }
    "api" {
        Invoke-Checked -FilePath "dotnet" -Arguments @("run", "--project", $ApiProject, "--launch-profile", "https")
    }
    "web" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie Blazor Web..." -ForegroundColor Green
        Invoke-Checked -FilePath "dotnet" -Arguments @("run", "--project", $WebProject, "--launch-profile", "https")
    }
    "android" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie MAUI Android ($AndroidRuntimeIdentifier)..." -ForegroundColor Green
        Invoke-Checked -FilePath "dotnet" -Arguments @("build", $AppProject, "-t:Run", "-f", "net10.0-android", "-p:RuntimeIdentifiers=$AndroidRuntimeIdentifier")
    }
    "windows" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie MAUI Windows..." -ForegroundColor Green
        Invoke-Checked -FilePath "dotnet" -Arguments @("build", $AppProject, "-t:Run", "-f", "net10.0-windows10.0.19041.0")
    }
    default {
        Write-Host "Użycie: .\dev.ps1 [opcja]"
        Write-Host "Opcje:"
        Write-Host "  db-up               - Podnosi bazę SQL Server w Dockerze"
        Write-Host "  db-down             - Zatrzymuje bazę SQL Server"
        Write-Host "  db-migrate          - Aplikuje migracje EF Core"
        Write-Host "  db-add-migration    - Dodaje migrację EF Core"
        Write-Host "  api                 - Uruchamia samo Web API"
        Write-Host "  web                 - Uruchamia API w tle + Blazor Web"
        Write-Host "  android             - Uruchamia API w tle + MAUI Android (domyślnie emulator x64)"
        Write-Host "  windows             - Uruchamia API w tle + MAUI Windows"
        Write-Host "  `$env:ANDROID_RUNTIME_IDENTIFIER='android-arm64'; .\dev.ps1 android - uruchamia na telefonie ARM64"
    }
}
