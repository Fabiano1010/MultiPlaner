#!/usr/bin/env bash
set -e

COMMAND=$1

function run_api {
    echo "▶ Uruchamianie Web API..."
    dotnet run --project MultiPlanerAPI
}

function run_api_background {
    echo "▶ Sprawdzanie / Uruchamianie Web API w tle..."
    dotnet run --project MultiPlanerAPI &
    API_PID=$!
    trap "echo 'Zatrzymywanie API...'; kill $API_PID 2>/dev/null" EXIT
    sleep 3
}

case "$COMMAND" in
    "db-up")
        echo "▶ Uruchamianie bazy SQL Server..."
        docker compose -f docker-compose.db.yml up -d
        ;;
    "db-down")
        echo "▶ Zatrzymywanie bazy danych..."
        docker compose -f docker-compose.db.yml down
        ;;
    "db-migrate")
        echo "▶ Aplikowanie migracji EF Core..."
        dotnet ef database update --project MultiPlanerAPI
        ;;
    "api")
        run_api
        ;;
    "web")
        run_api_background
        echo "▶ Uruchamianie aplikacji Blazor Web..."
        dotnet run --project MultiPlanerWeb
        ;;
    "android")
        run_api_background
        echo "▶ Uruchamianie MAUI Android..."
        dotnet build MultiPlanerApp -t:Run -f net10.0-android
        ;;
    "windows")
        echo "❌ Błąd: Kompilacja Windows nie jest wspierana na środowisku Linux/macOS."
        exit 1
        ;;
    *)
        echo "Użycie: ./dev.sh [opcja]"
        echo "Opcje:"
        echo "  db-up       - Podnosi bazę SQL Server w Dockerze"
        echo "  db-down     - Zatrzymuje bazę SQL Server"
        echo "  db-migrate  - Aplikuje migracje EF Core"
        echo "  api         - Uruchamia samo Web API"
        echo "  web         - Uruchamia API w tle + Blazor Web"
        echo "  android     - Uruchamia API w tle + MAUI Android"
        ;;
esac
