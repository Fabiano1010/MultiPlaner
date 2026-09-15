#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.db.yml"
DOCKER=(docker)
COMPOSE=("${DOCKER[@]}" compose -f "$COMPOSE_FILE")
API_PROJECT="$SCRIPT_DIR/MultiPlanerAPI/MultiPlanerAPI.csproj"
WEB_PROJECT="$SCRIPT_DIR/MultiPlanerWeb/MultiPlanerWeb.csproj"
APP_PROJECT="$SCRIPT_DIR/MultiPlanerApp/MultiPlanerApp.csproj"
DOTNET_EF_GLOBAL="$HOME/.dotnet/tools/dotnet-ef"
ANDROID_RUNTIME_IDENTIFIER="${ANDROID_RUNTIME_IDENTIFIER:-android-x64}"

require_docker() {
    if ! command -v docker >/dev/null 2>&1; then
        echo "❌ Docker nie jest zainstalowany albo nie ma go w PATH." >&2
        exit 1
    fi

    if docker info >/dev/null 2>&1; then
        return
    fi

    if command -v sudo >/dev/null 2>&1; then
        echo "▶ Docker wymaga uprawnień administratora; proszę o hasło sudo..."
        if sudo docker info >/dev/null 2>&1; then
            DOCKER=(sudo docker)
            COMPOSE=("${DOCKER[@]}" compose -f "$COMPOSE_FILE")
            return
        fi
    fi

    echo "❌ Docker Engine jest niedostępny dla bieżącego użytkownika." >&2
    echo "   Uruchom Docker Desktop/daemon albo sprawdź uprawnienia sudo." >&2
    exit 1
}

show_database_failure() {
    local status
    status="$("${DOCKER[@]}" inspect --format '{{.State.Status}}' MultiPlanerSQLDb 2>/dev/null || true)"
    if [[ "$status" == "exited" || "$status" == "dead" ]]; then
        echo "❌ Kontener SQL Server zakończył pracę. Ostatnie logi:" >&2
        "${DOCKER[@]}" logs --tail 80 MultiPlanerSQLDb >&2 || true
        exit 1
    fi
}

start_database() {
    local port="${DB_PORT:-1433}"

    require_docker
    echo "▶ Uruchamianie bazy SQL Server..."
    "${COMPOSE[@]}" up -d
    for _ in {1..60}; do
        show_database_failure
        if [[ "$("${DOCKER[@]}" inspect --format '{{.State.Running}}' MultiPlanerSQLDb 2>/dev/null || true)" == "true" ]] \
            && (: >"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
            echo "✅ SQL Server przyjmuje połączenia na porcie $port."
            "${COMPOSE[@]}" ps
            return
        fi
        sleep 1
    done
    echo "❌ SQL Server nie zaczął przyjmować połączeń na porcie $port w ciągu 60 sekund." >&2
    "${COMPOSE[@]}" ps >&2 || true
    show_database_failure
    exit 1
}

run_dotnet_ef() {
    if command -v dotnet-ef >/dev/null 2>&1; then
        dotnet-ef "$@"
    elif [[ -x "$DOTNET_EF_GLOBAL" ]]; then
        "$DOTNET_EF_GLOBAL" "$@"
    elif [[ -f "$SCRIPT_DIR/.config/dotnet-tools.json" ]]; then
        dotnet tool run dotnet-ef -- "$@"
    else
        echo "❌ Nie znaleziono narzędzia dotnet-ef." >&2
        echo "   Zainstaluj je poleceniem: dotnet tool install --global dotnet-ef --version 10.*" >&2
        exit 1
    fi
}

COMMAND="${1:-help}"

function run_api {
    echo "▶ Uruchamianie Web API..."
    dotnet run --project "$API_PROJECT" --launch-profile https
}

function run_api_background {
    echo "▶ Sprawdzanie / Uruchamianie Web API w tle..."
    dotnet run --project "$API_PROJECT" --launch-profile https &
    API_PID=$!
    trap 'exit_status=$?; echo "Zatrzymywanie API..."; kill "$API_PID" 2>/dev/null || true; exit "$exit_status"' EXIT
    sleep 3
}

case "$COMMAND" in
    "db-up")
        start_database
        ;;
    "db-down")
        require_docker
        echo "▶ Zatrzymywanie bazy danych..."
        "${COMPOSE[@]}" down
        ;;
    "db-reset")
        require_docker
        echo "▶ Usuwanie developerskiego kontenera i całego wolumenu SQL Server..."
        "${COMPOSE[@]}" down --volumes --remove-orphans
        echo "✅ Wszystkie bazy z tego developerskiego SQL Servera zostały usunięte."
        ;;
    "db-migrate")
        start_database
        echo "▶ Aplikowanie migracji EF Core..."
        run_dotnet_ef database update --project "$API_PROJECT" --startup-project "$API_PROJECT"
        ;;
    "api")
        run_api
        ;;
    "web")
        run_api_background
        echo "▶ Uruchamianie aplikacji Blazor Web..."
        dotnet run --project "$WEB_PROJECT" --launch-profile https
        ;;
    "android")
        run_api_background
        echo "▶ Uruchamianie MAUI Android ($ANDROID_RUNTIME_IDENTIFIER)..."
        dotnet build "$APP_PROJECT" -t:Run -f net10.0-android "-p:RuntimeIdentifiers=$ANDROID_RUNTIME_IDENTIFIER"
        ;;
    "windows")
        echo "❌ Błąd: Kompilacja Windows nie jest wspierana na środowisku Linux/macOS."
        exit 1
        ;;
    "db-add-migration")
        MIGRATION_NAME=${2:-"AutoMigration_$(date +%Y%m%d%H%M%S)"}
        echo "▶ Tworzenie nowej migracji: $MIGRATION_NAME..."
        run_dotnet_ef migrations add "$MIGRATION_NAME" --project "$API_PROJECT" --startup-project "$API_PROJECT"
        ;;
    *)
        echo "Użycie: ./dev.sh [opcja]"
        echo "Opcje:"
        echo "  db-up               - Podnosi bazę SQL Server w Dockerze"
        echo "  db-down             - Zatrzymuje bazę SQL Server"
        echo "  db-reset            - Usuwa kontener i cały developerski wolumen SQL Server"
        echo "  db-migrate          - Aplikuje migracje EF Core"
        echo "  db-add-migration    - Dodaje nową migracje EF Core"
        echo "  api                 - Uruchamia samo Web API"
        echo "  web                 - Uruchamia API w tle + Blazor Web"
        echo "  android             - Uruchamia API w tle + MAUI Android (domyślnie emulator x64)"
        echo "  windows             - Nieobsługiwane na Linux/macOS"
        echo "  ANDROID_RUNTIME_IDENTIFIER=android-arm64 ./dev.sh android - uruchamia na telefonie ARM64"
        ;;
esac
