# MultiPlaner - Dev Guide

## Wymagania
- .NET SDK (z zainstalowanym workloadem: `dotnet workload install maui`)
- Docker & Docker Compose
- Emulator Androida lub fizyczne urządzenie z włączonym debugowaniem USB (dla Androida)
- Windows 10/11 w trybie dewelopera (dla wersji Desktop na Windowsie)

---

## Pierwsze uruchomienie

1. Nadanie uprawnień do skryptu (tylko Linux/macOS):
   ```bash
   chmod +x dev.sh

2.  Uruchomienie bazy SQL Server w Dockerze:

      - Linux/macOS: ./dev.sh db-up
      - Windows: .\dev.ps1 db-up

3.  Aplikowanie migracji EF Core:

      - Linux/macOS: ./dev.sh db-migrate
      - Windows: .\dev.ps1 db-migrate

Codzienny development (Komendy startowe)

Skrypty startowe uruchamiają Web API w tle i podnoszą wybrany interfejs.

Linux / macOS

  - Samo Web API: ./dev.sh api
  - Blazor Web + API: ./dev.sh web
  - MAUI Android + API: ./dev.sh android
  - Zatrzymanie bazy: ./dev.sh db-down

Windows (PowerShell)

  - Samo Web API: .\dev.ps1 api
  - Blazor Web + API: .\dev.ps1 web
  - MAUI Android + API: .\dev.ps1 android
  - MAUI Windows + API: .\dev.ps1 windows
  - Zatrzymanie bazy: .\dev.ps1 db-down

Parametry połączeń i porty

  - SQL Server: localhost:1433 (Użytkownik: sa, Hasło: YourStrong@Password123,
    Baza: MultiPlanerDb)
  - Web API: http://localhost:5147
  - Emulator Androida: http://10.0.2.2:5147 (mapowane automatycznie w kodzie)

Git Workflow

1.  Tworzenie gałęzi zadania: git checkout -b feature/nazwa-zadania
2.  Kod API, UI i modeli piszemy na tym samym branchu w ramach monorepo.
3.  Zmiany zgłaszamy przez Pull Request do main.



<img width="1920" height="1036" alt="image_2026-06-08_19-12-52" src="https://github.com/user-attachments/assets/31ee3d72-7c50-4196-9f46-63ed30883eb5" />

<img width="1920" height="1038" alt="image_2026-06-08_19-13-05" src="https://github.com/user-attachments/assets/b6b8218c-7593-4617-958a-0867324a76e8" />

<img width="1280" height="691" alt="image" src="https://github.com/user-attachments/assets/87317117-3937-40bb-8a6a-87163207a4d9" />

<img width="1920" height="1038" alt="image_2026-06-08_19-16-40" src="https://github.com/user-attachments/assets/25c51672-a1bd-4803-a1f8-2a63d425bcbe" />
