#!/usr/bin/env bash
set -Eeuo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"
COMPOSE_FILE="$SCRIPT_DIR/docker-compose.db.yml"
DOCKER=(docker)
COMPOSE=("${DOCKER[@]}" compose -f "$COMPOSE_FILE")
API_PROJECT="$SCRIPT_DIR/MultiPlanerAPI/MultiPlanerAPI.csproj"
WEB_PROJECT="$SCRIPT_DIR/MultiPlanerWeb/MultiPlanerWeb.csproj"
APP_PROJECT="$SCRIPT_DIR/MultiPlanerApp/MultiPlanerApp.csproj"
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

prepare_dotnet_ef() {
    dotnet tool restore --tool-manifest "$SCRIPT_DIR/.config/dotnet-tools.json"
}

run_dotnet_ef() {
    dotnet tool run dotnet-ef -- "$@"
}

COMMAND="${1:-help}"

configure_dev_https() {
    if [[ "$(uname -s)" == "Linux" && -z "${DOTNET_DEV_CERTS_NSSDB_PATHS+x}" ]]; then
        export DOTNET_DEV_CERTS_NSSDB_PATHS=/dev/null
        echo "▶ HTTPS: pomijanie sprawdzania magazynów przeglądarek (obejście blokady certutil)."
    fi
}

function run_api {
    configure_dev_https
    echo "▶ Uruchamianie Web API..."
    dotnet run --project "$API_PROJECT" --launch-profile https
}

function run_api_background {
    configure_dev_https
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
        prepare_dotnet_ef
        start_database
        echo "▶ Aplikowanie migracji EF Core..."
        run_dotnet_ef database update --project "$API_PROJECT" --startup-project "$API_PROJECT"
        ;;
    "db-status")
        prepare_dotnet_ef
        run_dotnet_ef migrations list --project "$API_PROJECT" --startup-project "$API_PROJECT"
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
        dotnet build "$APP_PROJECT" -t:Run -f net10.0-android "-p:RuntimeIdentifier=$ANDROID_RUNTIME_IDENTIFIER"
        ;;
    "windows")
        echo "❌ Błąd: Kompilacja Windows nie jest wspierana na środowisku Linux/macOS."
        exit 1
        ;;
    "db-add-migration")
        prepare_dotnet_ef
        if run_dotnet_ef migrations has-pending-model-changes --project "$API_PROJECT" --startup-project "$API_PROJECT"; then
            echo "✅ Model nie zmienił się. Nie ma potrzeby tworzenia nowej migracji."
        else
            MIGRATION_NAME=${2:-"AutoMigration_$(date +%Y%m%d%H%M%S)"}
            echo "▶ Tworzenie nowej migracji: $MIGRATION_NAME..."
            run_dotnet_ef migrations add "$MIGRATION_NAME" --project "$API_PROJECT" --startup-project "$API_PROJECT"
        fi
        ;;
    *)
        echo "Użycie: ./dev.sh [opcja]"
        echo "Opcje:"
        echo "  db-up               - Podnosi bazę SQL Server w Dockerze"
        echo "  db-down             - Zatrzymuje bazę SQL Server"
        echo "  db-reset            - Usuwa kontener i cały developerski wolumen SQL Server"
        echo "  db-migrate          - Aplikuje migracje EF Core"
        echo "  db-status           - Pokazuje migracje i stan ich zastosowania"
        echo "  db-add-migration [nazwa] - Tworzy migrację tylko gdy zmienił się model"
        echo "  api                 - Uruchamia samo Web API"
        echo "  web                 - Uruchamia API w tle + Blazor Web"
        echo "  android             - Uruchamia API w tle + MAUI Android (domyślnie emulator x64)"
        echo "  windows             - Nieobsługiwane na Linux/macOS"
        echo "  ANDROID_RUNTIME_IDENTIFIER=android-arm64 ./dev.sh android - uruchamia na telefonie ARM64"
        ;;
esac
