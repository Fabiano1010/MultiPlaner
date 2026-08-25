param (
    [Parameter(Mandatory=$true, Position=0)]
    [ValidateSet("db-up", "db-down", "db-migrate", "api", "web", "android", "windows")]
    [string]$Command
)

function Start-ApiBackground {
    Write-Host "▶ Uruchamianie Web API w osobnym oknie..." -ForegroundColor Cyan
    Start-Process wt -ArgumentList "dotnet run --project MultiPlanerAPI" -ErrorAction SilentlyContinue
    if (-not $?) {
        # Fallback jeśli brak Windows Terminal
        Start-Process powershell -ArgumentList "-NoExit", "-Command", "dotnet run --project MultiPlanerAPI"
    }
    Start-Sleep -Seconds 3
}

switch ($Command) {
    "db-up" {
        Write-Host "▶ Podnoszenie bazy SQL Server w Dockerze..." -ForegroundColor Green
        docker compose -f docker-compose.db.yml up -d
    }
    "db-down" {
        Write-Host "▶ Zatrzymywanie bazy..." -ForegroundColor Yellow
        docker compose -f docker-compose.db.yml down
    }
    "db-migrate" {
        Write-Host "▶ Wykonywanie migracji EF Core..." -ForegroundColor Green
        dotnet ef database update --project MultiPlanerAPI
    }
    "api" {
        dotnet run --project MultiPlanerAPI
    }
    "web" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie Blazor Web..." -ForegroundColor Green
        dotnet run --project MultiPlanerWeb
    }
    "android" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie MAUI Android..." -ForegroundColor Green
        dotnet build MultiPlanerApp -t:Run -f net10.0-android
    }
    "windows" {
        Start-ApiBackground
        Write-Host "▶ Uruchamianie MAUI Windows..." -ForegroundColor Green
        dotnet build MultiPlanerApp -t:Run -f net10.0-windows10.0.19041.0
    }
}
