# Fundament API — kroki 1–5

## Zakres

Wdrożone są podstawy API, nowy model SQL Server, konta/sesje, pokoje,
ustawienia uczestnika oraz zaproszenia i dołączanie gości.

Modele wydarzeń, uczestników wydarzeń, załączników, wiadomości, dostępności,
aktywności, audytu i outbox są przygotowaniem schematu. Nie ma jeszcze ich
endpointów, automatycznego usuwania wygasłych pokoi ani publikacji SignalR.
Automatyczna archiwizacja pokoi jest już obsługiwana.

## Uruchomienie

Wymagane: .NET SDK 10, SQL Server i narzędzie dotnet-ef 10.0.11.
Backend i testy nie wymagają MAUI.

```bash
dotnet tool restore
dotnet restore MultiPlaner.Server.slnf
dotnet dev-certs https --trust
```

Wersję EF CLI określa `.config/dotnet-tools.json`; nie jest potrzebna instalacja globalna.
Skrypty `dev.sh` i `dev.ps1` odtwarzają i wywołują tę samą wersję narzędzia.
Na Linuxie konfiguracja zaufania certyfikatu zależy od przeglądarki.
Cookies uwierzytelniające zawsze wymagają HTTPS, również w development.

### Baza po przebudowie schematu

Na świeżym pobraniu repo uruchom `./dev.sh db-migrate` (Windows: `./dev.ps1 db-migrate`).
Polecenie tworzy schemat na pustej bazie i stosuje tylko brakujące migracje na istniejącej.
Nie używaj `db-add-migration` po samym pobraniu repo. To polecenie jest przeznaczone
wyłącznie do zapisania własnej zmiany modelu; bez zmian kończy się bez tworzenia plików.

Nie usuwaj plików migracji ani snapshotu przy zachowaniu bazy. Historia musi być
wersjonowana razem z kodem. Aktualny łańcuch zachowuje identyfikatory:

- `20260913213459_InitialApiSchema`
- `20260915120625_AutoMigration_20260915140619` (historyczna pusta migracja)
- `20261010153500_AddAutomaticRoomArchival`

`./dev.sh db-status` pokazuje ich zastosowanie w działającej bazie. Pusta migracja
pozostaje w repo dla zgodności z istniejącymi bazami; nie zawiera danych użytkowników.
Wszystkie trzy migracje i snapshot należy przechowywać w Git. Pliki `.env`, `bin`,
`obj` i wyniki testów są lokalne. Dane SQL Server pozostają w wolumenie Dockera.

Poniższe uwagi dotyczą wyłącznie bardzo starego schematu sprzed `InitialApiSchema`;
nie oznaczają konieczności resetowania obecnej bazy przy aktualizacji.

Migracja `InitialApiSchema` zastępuje dawną migrację
`20260825144737_InitialSqlServerCreate`. Nie przenosi jej danych.

Przy pierwszym uruchomieniu wskaż nową, pustą bazę `MultiPlanerSQLDb`,
w `ConnectionStrings:DefaultConnection`. Można użyć tego samego serwera SQL
i dotychczasowych danych logowania, zmieniając tylko nazwę bazy.
Nie trzeba usuwać całego kontenera ani jego wolumenu.

Przykład konfiguracji przez środowisko (uzupełnij własne hasło):

```bash
export ConnectionStrings__DefaultConnection='Server=localhost,1433;Database=MultiPlanerSQLDb;User Id=sa;Password=<hasło SQL>;Encrypt=True;TrustServerCertificate=True'
dotnet ef database update --project MultiPlanerAPI
dotnet run --project MultiPlanerAPI --launch-profile https
```

PowerShell:

```powershell
$env:ConnectionStrings__DefaultConnection = 'Server=localhost,1433;Database=MultiPlanerSQLDb;User Id=sa;Password=<hasło SQL>;Encrypt=True;TrustServerCertificate=True'
dotnet ef database update --project MultiPlanerAPI
dotnet run --project MultiPlanerAPI --launch-profile https
```

Development ma `Database:MigrateOnStartup=true`. Start odmawia zastosowania
nowego schematu na bazie ze starą historią migracji. Sama migracja ma również
zabezpieczenie przed nałożeniem jej na stare tabele przez CLI.
Program nigdy automatycznie nie usuwa bazy.

Poza Development migracje nie uruchamiają się przy starcie; należy wykonać je
osobno. `TrustServerCertificate=True` z przykładów dotyczy lokalnego SQL Servera.

- API/Swagger: `https://localhost:7157/swagger`.
- Blazor w development: `https://localhost:7132`.
- Skrypty `dev.sh` i `dev.ps1` uruchamiają API/Web z profilem HTTPS.
- Docelowo frontend i API powinny być dostępne pod wspólnym originem.
- `Cors:AllowedOrigins` zawiera w development tylko origin HTTPS Blazora.
  Inne originy trzeba jawnie skonfigurować. Nie stosujemy `AllowAnyOrigin`.

## Kontrakt HTTP

| Metoda i adres | Dostęp | Działanie |
|---|---|---|
| GET /api/auth/csrf | anonimowy lub sesja | Token CSRF i cookie powiązane z bieżącą tożsamością |
| POST /api/auth/register | anonimowy, z CSRF | Rejestracja; 201, bez automatycznego logowania |
| POST /api/auth/login | anonimowy, z CSRF | Logowanie; 200 i cookie konta |
| POST /api/auth/logout | sesja, z CSRF | Unieważnienie sesji; 204 |
| GET /api/me | konto lub gość | Bieżący profil/tożsamość |
| PATCH /api/me | tylko konto, z CSRF | Częściowa aktualizacja własnego profilu |
| POST /api/rooms | tylko konto, z CSRF | Pokój, członkostwo właściciela i pierwsze zaproszenie |
| GET /api/rooms?status=active/archived/all | konto lub gość | Lista własnych i udostępnionych pokoi; domyślnie aktywnych |
| GET /api/rooms/{roomId} | członek pokoju | Szczegóły pokoju |
| PATCH /api/rooms/{roomId} | właściciel, z CSRF | Zmiana nazwy, strefy lub terminu wygaśnięcia |
| GET /api/rooms/{roomId}/members?page=1&pageSize=50 | członek pokoju | Stronicowana lista uczestników |
| GET /api/rooms/{roomId}/members/me | członek pokoju | Własna nazwa, kolor, ulubiony i wersja ustawień |
| PATCH /api/rooms/{roomId}/members/me | członek pokoju, z CSRF | Aktualizacja własnych ustawień uczestnika |
| POST /api/rooms/{roomId}/archive | właściciel, z CSRF | Archiwizacja; zachowuje odczyt |
| DELETE /api/rooms/{roomId} | właściciel, z CSRF | Trwałe usunięcie pokoju i danych zależnych |
| POST /api/rooms/{roomId}/invitations | właściciel, z CSRF | Wygenerowanie nowego magicznego linku |
| DELETE /api/rooms/{roomId}/invitations/{invitationId} | właściciel, z CSRF | Unieważnienie linku |
| POST /api/invitations/preview | dowolny, z CSRF | Podgląd zaproszenia bez zużycia tokenu |
| POST /api/invitations/join | dowolny, z CSRF | Dołączenie konta lub gościa |
| GET /api/invitations/{token} | dowolny | Alias podglądu dla starszych klientów |
| POST /api/invitations/{token}/join | dowolny, z CSRF | Alias dołączania dla starszych klientów |

DTO znajdują się w `MultiPlanerSharedModels/Contracts`.
Odpowiedzi nie zawierają encji Identity, hasha hasła ani security stamp.
Pola `userId`, `role` i inne nieznane pola żądania są odrzucane.

Rejestracja wymaga poprawnego e-maila, hasła i nazwy wyświetlanej (2–64 znaki).
E-mail jest unikalny bez rozróżniania wielkości liter.
Domyślne ustawienia to kraj `PL` i strefa `Europe/Warsaw`.
Akceptujemy rzeczywiste kody krajów ISO oraz identyfikatory stref IANA lub `UTC`.

Hasło ma 12–128 znaków, przynajmniej małą i wielką literę, cyfrę oraz znak
niealfanumeryczny. Po 5 nieudanych logowaniach konto jest blokowane na 15 minut.
Błędy logowania nie rozróżniają nieistniejącego konta, złego hasła i blokady.
Potwierdzanie e-maila, reset hasła i MFA nie są częścią tych trzech kroków.

Profil pozwala zmienić `displayName`, `countryCode`, `timeZoneId`,
`avatarUrl` i `contactDetails`. Nie zmienia e-maila ani hasła.
Pominięcie pola lub `null` pozostawia je bez zmian. Pusty string usuwa awatar
lub dane kontaktowe. Awatar jest opcjonalnym adresem HTTPS; API nie pobiera
pliku z tego adresu. Własny upload zostanie dodany w module załączników.

### Cookies i CSRF

1. Wywołaj `GET /api/auth/csrf` i zachowaj cookie.
2. Odczytaj `token` z odpowiedzi i wysyłaj go jako `X-CSRF-TOKEN` przy
   POST/PATCH/PUT/DELETE, również przy rejestracji i logowaniu.
3. Po zalogowaniu, wylogowaniu lub zmianie tożsamości pobierz nowy token.
4. Klient przeglądarkowy musi przesyłać cookies; dla osobnego dozwolonego
   originu użyj `credentials: "include"`.

Cookies mają `HttpOnly`, `Secure`, `SameSite=Lax`, prefiks `__Host-`
i ścieżkę `/`. Sesja konta trwa maksymalnie 14 dni.
Logout konta unieważnia **wszystkie sesje tego konta**, także skopiowane cookies,
przez zmianę security stamp i sprawdzanie go przy każdym żądaniu.

W Swaggerze najpierw wykonaj GET CSRF, a następnie w przycisku Authorize wklej
token do schematu Csrf. Po loginie powtórz tę czynność.
Przykłady dla klienta HTTP Ridera są w `MultiPlanerAPI/MultiPlanerAPI.http`.

### Goście

`GuestSessionService.CreateAsync` zapisuje tożsamość ważną przez 30 dni.
`SignInAsync` wystawia oddzielne cookie; `RevokeAsync` odbiera dostęp.
Ważność cookie oraz datę i unieważnienie w bazie sprawdzamy przy każdym żądaniu.

**Nie ma publicznego endpointu tworzenia gościa.** Po poprawnym użyciu zaproszenia
API zapisuje sesję gościa i członkostwo w jednej transakcji, a po jej
zatwierdzeniu wystawia cookie. Nowy gość podaje pseudonim (2–64 znaki).

GET /api/me rozróżnia `kind: "user"` oraz `kind: "guest"`; gość nie ma e-maila
ani identyfikatora konta. PATCH /api/me zwraca gościowi 403.
Logowanie na konto z aktywnej sesji gościa unieważnia tę sesję. Przenoszenie
gościnnych wpisów na konto nie zostało zaimplementowane.

### Pokoje i zaproszenia

Pokój wygasa domyślnie po 3 miesiącach kalendarzowych, najpóźniej po roku.
Właściciel może zmienić nazwę, strefę czasową i datę wygaśnięcia. Archiwum
pozostaje dostępne do odczytu członkom; `DELETE` usuwa pokój wraz z zależnymi
rekordami. Niezarchiwizowany pokój po wygaśnięciu nie jest dostępny do odczytu.

Przy tworzeniu można zaznaczyć `archiveOnExpiry: true` (domyślnie `false`).
Pokój pozostaje aktywny do `expiresAtUtc`, a dokładnie od tego momentu jest
archiwum dostępnym do odczytu dotychczasowym uczestnikom. Dotyczy to także
niestandardowego terminu, np. sześciu miesięcy; późniejsza zmiana terminu
aktywnego pokoju przesuwa moment automatycznej archiwizacji. Sam wybór opcji
nie blokuje bieżącej edycji. Formularz tworzenia kalendarza ma odpowiedni checkbox.

Proces w tle zapisuje należne archiwizacje przy starcie i co minutę. Odczyt,
filtrowanie i blokada zmian uwzględniają termin niezależnie od tego procesu,
także po restarcie. `archivedAtUtc` dla automatycznego archiwum odpowiada
terminowi wygaśnięcia, a nie opóźnionej dacie uruchomienia procesu. Zapis jest
warunkowy i bezpieczny przy ponowieniach oraz wielu instancjach serwera.
`RoomArchival:Enabled=false` wyłącza proces zapisujący (używane w testach);
nie wyłącza reguł odczytu i dostępu. Przyszła retencja musi wykluczać pokoje
z `archiveOnExpiry=true`, nawet jeśli `archivedAtUtc` jeszcze nie zapisano.

Ręczna archiwizacja aktywnego pokoju pozostaje dostępna. Nie można nią
przywrócić dostępu do wygasłego pokoju bez wybranej automatycznej archiwizacji.
Automatyczne usuwanie pozostałych wygasłych pokoi nadal należy do kroku 12.
Migracja `AddAutomaticRoomArchival` dodaje opcję bez zmiany dotychczasowych
pokoi (otrzymują `false`); nie usuwa ani nie archiwizuje istniejących danych.

Lista i szczegóły pokoju oraz odpowiedzi tworzenia/dołączenia zawierają
`membership`: ustawienia bieżącego uczestnika. `isFavourite` jest prywatne;
publiczna lista uczestników nie ujawnia tego pola ani wersji ich ustawień.

Konto i gość mogą zmienić własne `displayName` (maksymalnie 64 znaki,
minimum 2 po przycięciu), `color` (6 cyfr szesnastkowych bez `#`) i `isFavourite`.
Nazwa jest przycinana, kolor normalizowany do wielkich liter. Zmiany dotyczą
wyłącznie danego pokoju; nie modyfikują profilu konta ani innych członkostw.
Pominięte pola i `null` pozostają bez zmian; `false` usuwa oznaczenie ulubionego.
W archiwum można zmienić tylko prywatne oznaczenie ulubionego.

PATCH wymaga `rowVersion` pobranego z aktualnego `membership` lub
GET `/api/rooms/{roomId}/members/me`. Jest to 8 bajtów zakodowanych w JSON
jako base64. Stara wersja zwraca 409 `concurrency_conflict`; klient powinien
pobrać aktualne ustawienia przed ponowieniem zapisu. Nie przekazuje się
identyfikatora uczestnika — serwer ustala go na podstawie sesji.

Utworzenie pokoju zapisuje także właściciela jako uczestnika oraz pierwsze
zaproszenie; odpowiedź zawiera je w `initialInvitation`. Nowy link unieważnia
poprzednie linki pokoju. Domyślnie jest ważny 7 dni i może być używany wiele
razy; `maxUses: 1` tworzy link jednorazowy. Serwer przechowuje wyłącznie skrót
losowego tokenu. Dołączenie zużywa token atomowo, podgląd go nie zużywa.

Pełny `joinUrl` prowadzi w development do `https://localhost:7132/join/{token}`.
Adres konfiguruje `Invitations:PublicWebBaseUrl`. Strona Web wywołuje API
z cookies i tokenem CSRF. Właściwy token jest dostępny tylko w odpowiedzi
na utworzenie pokoju lub zaproszenia; `{token}` to symbol w dokumentacji.

### Błędy

Błędy API mają format `application/problem+json`, z `status`, `title`
i `traceId`. Błędy biznesowe/walidacji dodatkowo mają stabilny `code`;
walidacja zawiera `errors`.

- 400: walidacja, nieznane pola JSON lub nieprawidłowy CSRF.
- 401: brak sesji, sesja wygasła lub błędne logowanie; bez przekierowań HTML.
- 403: brak uprawnień, np. gość próbujący zmienić profil konta.
- 409: istniejący e-mail albo konflikt równoczesnej edycji.
- 500: ogólny komunikat bez szczegółów wyjątku; szczegóły pozostają w logach.

Opisy i przyszłe wiadomości są zwykłym tekstem. Klient powinien je renderować
z kodowaniem HTML; odpowiedź JSON nie jest zgodą na renderowanie surowego HTML.

## Model i granice modułów

- `Controllers`: kontrolery HTTP wywołujące usługi z modułów.
- `Modules/Users`: usługi kont, sesje i odczyt bieżącej tożsamości.
- `Infrastructure`: wspólne błędy i ochrona CSRF.
- `Database/Configurations`: mapowania i ograniczenia SQL Server.
- `Models`: encje serwera; nie są kontraktem dla UI.
- `MultiPlanerSharedModels/Contracts`: wspólne DTO i podstawy stronicowania.

Konto to `ApplicationUser : IdentityUser<int>`. Gość jest osobną encją.
`RoomMember` wskazuje dokładnie konto albo sesję gościa, co wymusza SQL CHECK.
Właściciel jest wskazany przez `Room.OwnerUserId`; oznaczenie właściciela
nie pochodzi z tekstowej roli przekazanej przez klienta.

Autorzy wydarzeń, wiadomości i deklaracji dostępności wskazują członka pokoju.
Złożone klucze obce uniemożliwiają podpięcie autora lub uczestnika z innego
pokoju. Domena używa `NO ACTION` przy usuwaniu: późniejsze operacje usunięcia
muszą jawnie posprzątać zależności, pliki i metadane.

Wydarzenie ma własny `RoomId`, czas UTC, flagę całego dnia, kategorię, kolor
i przypięcie. Kontrakt przyszłych operacji czasowych to [początek, koniec).
SQL wymusza dodatnią długość przedziału. `rowversion` chroni mutowalne encje
przed nadpisaniem równoczesnej zmiany. Daty utworzenia/aktualizacji kont,
sesji i encji śledzonych ustala `TimeProvider` po stronie serwera.

Wiadomości są osobnymi rekordami. Unikalny klucz żądania na nadawcę/pokój
przygotowuje ochronę przed duplikatami. Kursory przeczytania w członkostwie
są wskaźnikami, a nie FK blokującymi przyszłe usuwanie treści.

Przyszły audyt przechowuje tylko metadane operacji; jego identyfikatory nie
są kluczami obcymi do usuwanych danych. Ich anonimizacja należy do kroku 12.
Outbox ma miejsce na blokadę zadania, ponowienia i datę przetworzenia;
worker zostanie wdrożony razem z synchronizacją.

## Testy

```bash
dotnet test MultiPlanerAPI.UnitTests/MultiPlanerAPI.UnitTests.csproj
dotnet test MultiPlanerAPI.IntegrationTests/MultiPlanerAPI.IntegrationTests.csproj
```

Testy integracyjne używają `WebApplicationFactory` i SQL Server 2022 przez
Testcontainers. Weryfikują migrację, realne FK/SQL CHECK/rowversion, rejestrację,
logowanie, cookies, CSRF, profil, gości i dokument Swagger.
Nie zastępujemy SQL Servera providerem InMemory ani SQLite.

Domyślnie Testcontainers uruchamia jednorazowy kontener. Można podać
`MULTIPLANER_TEST_SQLSERVER` z connection stringiem do dostępnej instancji
SQL Server. Konto testowe potrzebuje praw tworzenia i usuwania baz.
Fixture zawsze tworzy własną bazę `MultiPlanerTests_<losowy identyfikator>`
i usuwa wyłącznie ją; nie wykorzystuje bazy wskazanej w connection stringu.

Dla Podmana uruchom jego usługę zgodną z Docker API, ustaw `DOCKER_HOST`
na jej socket i, przy rootless Podman, `TESTCONTAINERS_RYUK_DISABLED=true`.
W takim trybie sprzątanie zapewnia fixture; po przerwanym procesie sprawdź
pozostawione kontenery testowe. Testy wymagają dostępu do lokalnych gniazd
runnera .NET, silnika kontenerów i portu SQL Server.

Brak dostępnej infrastruktury kończy testy błędem; nie są po cichu pomijane.
