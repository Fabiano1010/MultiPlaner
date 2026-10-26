# MultiPlaner - Dev Guide

## API — fundament i etapy 1–4

Serwer ma schemat SQL Server, ASP.NET Core Identity, rejestrację/logowanie,
reset hasła przez SMTP, profil, cookies z CSRF, pokoje, prywatne foldery,
okładki, archiwizację, zaproszenia i dołączanie gości.
Instrukcja przygotowania **pustej bazy**, kontrakty i uruchamianie testów:
[Fundament API](docs/api-foundation.md).

Etap 4 dodaje wydarzenia pokoju, globalną listę wydarzeń, priorytet niezależny
od przypięcia, wydarzenia całodniowe i opuszczanie pokoju z zachowaniem autorstwa.
Kontrakty, filtry, reguły czasu i migracja aktualizująca istniejącą bazę:
[Wydarzenia i członkostwo — etap 4](docs/api-stage-4.md).

Sam backend nie wymaga workloadu MAUI. Użyj filtra rozwiązania:

```bash
dotnet build MultiPlaner.Server.slnf
dotnet test MultiPlaner.Server.slnf
```

Testy integracyjne wymagają Dockera/Podmana albo testowego SQL Servera.
API i Swagger uruchamiaj przez HTTPS: `https://localhost:7157/swagger`.
Migracje są wersjonowane w repo; aktualizacja nie wymaga kasowania lokalnej bazy.

## Pierwsze uruchomienie po pobraniu repo

Dla API/Web potrzebujesz .NET SDK 10 i działającego Dockera z Compose.
MAUI i emulator są potrzebne tylko dla aplikacji mobilnej/desktopowej.

Linux / macOS:

```bash
./dev.sh db-migrate
./dev.sh web
```

Windows (PowerShell):

```powershell
.\dev.ps1 db-migrate
.\dev.ps1 web
```

`db-migrate` odtwarza lokalne narzędzie EF w wersji zapisanej w repo,
uruchamia SQL Server i stosuje istniejące migracje. Na pustej bazie tworzy
schemat; przy kolejnym wywołaniu, jeśli nic się nie zmieniło, zgłasza brak
migracji do zastosowania. Nie trzeba generować migracji po pobraniu repo.
Pierwsze uruchomienie wymaga internetu do pobrania narzędzi, pakietów i obrazu SQL Server.
Przeglądarka wymaga zaufanego certyfikatu developerskiego:
`dotnet dev-certs https --trust` (szczegóły dla systemu w dokumentacji .NET).

Domyślna konfiguracja developerska działa bez pliku `.env`:
SQL Server `localhost:1433`, baza `MultiPlanerSQLDb`, konto `sa`, hasło
`YourStrong@Password123`. To lokalne dane developerskie, nie konfiguracja produkcyjna.
Jeżeli zmieniasz `SA_PASSWORD` lub `DB_PORT` w `.env`, ustaw również pasujący
`ConnectionStrings__DefaultConnection` dla API i narzędzi EF. Zmiana hasła
w Compose nie zmienia hasła konta w istniejącym wolumenie SQL Server.

## Migracje i Git

W repo przechowujemy **pliki migracji i snapshot modelu**, a nie zawartość bazy.
Lokalne dane SQL Server pozostają w wolumenie Dockera; `.env`, wyniki testów
oraz katalogi kompilacji są ignorowane przez Git.

- Po pobraniu repo lub nowych zmian: `./dev.sh db-migrate`.
- Podgląd migracji i ich zastosowania: `./dev.sh db-status` (baza musi działać).
- Po własnej zmianie modelu: `./dev.sh db-add-migration NazwaZmiany`, następnie `./dev.sh db-migrate`.
- Bez zmiany modelu `db-add-migration` niczego nie tworzy.
- Nową migrację, jej plik `.Designer.cs` i snapshot dodaj do tego samego commita co zmianę modelu.
- Nie usuwaj ani nie generuj od nowa zastosowanych migracji. Ich identyfikatory są zapisane w bazie.

Te same komendy są dostępne w `dev.ps1`. `db-down` zatrzymuje kontener i zachowuje dane.
`db-reset` usuwa **cały lokalny wolumen SQL Server i wszystkie jego bazy**;
nie jest elementem pierwszego uruchomienia ani aktualizacji.

Aktualna historia zaczyna się od `20261011134010_InitialApiSchema`. Zastępuje
nieśledzoną migrację deweloperską `20261011080723_test`. Baza z poprzednią
historią wymaga jawnego odtworzenia deweloperskiej bazy (`db-reset` usuwa cały
lokalny wolumen) lub wskazania nowej pustej bazy. API nie resetuje jej samo.

Klucze Data Protection używane przez cookies i odtwarzanie linków zaproszeń
są w development zapisywane w `MultiPlanerAPI/.data-protection-keys`.
Na produkcji ustaw `DataProtection__KeysPath` na trwały, współdzielony katalog.
Okładki pokoi zapisują się w `MultiPlanerAPI/data/room-covers`; produkcyjny
trwały katalog wskazuje `Storage__CoverRoot`.

Reset hasła wymaga `PasswordReset__PublicWebBaseUrl`, `Email__Smtp__Host`,
`Email__Smtp__Port` i `Email__Smtp__From`. Opcjonalne są `Email__Smtp__User`,
`Email__Smtp__Password` oraz `Email__Smtp__EnableSsl`. Development Compose
uruchamia [Mailpit](https://mailpit.axllent.org/docs/install/docker/) na SMTP
`localhost:1025` i podglądzie `http://localhost:8025`. Bez działającego SMTP
odpowiedź żądania resetu nadal ma status 202, ale wiadomość nie dotrze;
błąd dostawy jest zapisywany w logach serwera.

## Codzienny development

- `./dev.sh api` — samo API, HTTPS na `https://localhost:7157/swagger`.
- `./dev.sh web` — API i Blazor, interfejs na `https://localhost:7132`.
- `./dev.sh android` — API i aplikacja Android (wymaga MAUI).
- `./dev.sh db-up` / `./dev.sh db-down` — uruchomienie / zatrzymanie bazy.
- Na Windows użyj `dev.ps1`; dostępne jest również `windows`.

Na Linuksie `dev.sh` omija sprawdzanie magazynów certyfikatów przeglądarek
podczas startu API/Web przez `DOTNET_DEV_CERTS_NSSDB_PATHS=/dev/null`, aby
uniknąć blokady `certutil`. Jawnie ustawiona wartość ma pierwszeństwo.
Nie ustawiaj tego obejścia globalnie ani przy `dotnet dev-certs https --trust`/`--clean`.
HTTPS i weryfikacja certyfikatu przez przeglądarkę pozostają aktywne.

Git Workflow

1.  Tworzenie gałęzi zadania: git checkout -b feature/nazwa-zadania
2.  Kod API, UI i modeli piszemy na tym samym branchu w ramach monorepo.
3.  Zmiany zgłaszamy przez Pull Request do main.



<img width="1920" height="1036" alt="image_2026-06-08_19-12-52" src="https://github.com/user-attachments/assets/31ee3d72-7c50-4196-9f46-63ed30883eb5" />

<img width="1920" height="1038" alt="image_2026-06-08_19-13-05" src="https://github.com/user-attachments/assets/b6b8218c-7593-4617-958a-0867324a76e8" />

<img width="1280" height="691" alt="image" src="https://github.com/user-attachments/assets/87317117-3937-40bb-8a6a-87163207a4d9" />

<img width="1920" height="1038" alt="image_2026-06-08_19-16-40" src="https://github.com/user-attachments/assets/25c51672-a1bd-4803-a1f8-2a63d425bcbe" />
