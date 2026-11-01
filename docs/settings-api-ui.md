# Ustawienia konta i kalendarza

Implementacja API, wspólnego UI Blazor (Web/MAUI), słowników typów wydarzeń i palety konta.

## Uruchomienie i baza

Ta gałąź zawiera nową migrację `InitialApiSchema` dla **pustej bazy**. Nie jest to aktualizacja bazy z poprzednią historią migracji. Ustaw `ConnectionStrings__DefaultConnection` na nową bazę, wykonaj `dotnet tool restore` i `dotnet ef database update --project MultiPlanerAPI`. Nie kasuj istniejącej bazy, jeśli zawiera potrzebne dane. `DatabaseInitializer` odrzuca nieznaną historię migracji.

Nie ma automatycznego importu starych kategorii. Przy przyszłym imporcie trzeba utworzyć typy per pokój, przypisać im wydarzenia, a dotychczasowy efektywny kolor zapisać jako własny kolor wydarzenia.

Pliki: `Storage:AvatarRoot` (domyślnie `MultiPlanerAPI/data/avatars`) i istniejący `Storage:CoverRoot`. W środowisku z wieloma instancjami katalogi muszą być trwałe i współdzielone. Pliki nie są serwowane jako publiczne statyczne zasoby.

## API konta

Wszystkie mutacje wymagają cookie i `X-CSRF-TOKEN`. Ustawienia konta są dostępne dla zarejestrowanych użytkowników.

| Metoda i ścieżka | Dane / zachowanie |
| --- | --- |
| PATCH `/api/me` | `displayName` (2–64 znaki), pozostałe istniejące pola profilu; nicki w pokojach nie zmieniają się |
| PUT `/api/me/avatar` | multipart `file`, JPEG/PNG/WebP, 1 bajt–5 MB; format wykrywany po nagłówku pliku |
| DELETE `/api/me/avatar` | usuwa zdjęcie i stary URL; 204 |
| GET `/api/users/{userId}/avatar` | właściciel konta albo aktywny członek wspólnego czytelnego pokoju; 404 poza tym zakresem; no-store |
| POST `/api/me/change-password` | `currentPassword`, `newPassword`; Identity waliduje hasło; 204 i odświeżenie bieżącej sesji, inne sesje tracą ważność |
| GET `/api/me/theme` | `{ palette, rowVersion }`; niezapisana paleta zwraca domyślne kolory i pustą wersję |
| PUT `/api/me/theme` | `{ palette, rowVersion }`; zapis pełnej palety |
| DELETE `/api/me/theme` | `{ rowVersion }`; zapis domyślnej palety, zwraca jej nową wersję |

`AvatarUrl` pozostaje w odpowiedzi sesji, ale jest usunięty z żądania edycji profilu. Odczyt istniejących zewnętrznych URL jest zachowany do wymiany/usunięcia. Nowe obrazy mają chroniony adres API z identyfikatorem wersji. Odświeżenie strony nie przywraca starego zdjęcia.

Zmiana hasła ma osobny limit 5 żądań/minutę na konto. Reset hasła zachowuje istniejący limit według IP.

Paleta: `primary`, `panelBackground`, `controlBackground`, `secondary`, `background`, `roomSelection`, `daySelection`, `daySelectionBackground`; każda wartość to `#RRGGBB`. Kolory tekstów nie są edytowalne. Dwa dawne tokeny tła połączono; nieużywany półprzezroczysty token usunięto. Menu Wygląd oferuje podgląd, zapis, anulowanie i reset wymagający zapisu. Dane są przechowywane na koncie, bez współdzielonego localStorage między użytkownikami.

## Role i członkowie

Właściciel: `Room.OwnerUserId`. Admin: `RoomMember.IsAdmin`, tylko konto (nigdy gość). Właściciela nie można usunąć ani zdegradować.

| Operacja | Właściciel | Admin |
| --- | --- | --- |
| Usuwanie zwykłego członka/gościa, awans konta | Tak | Tak |
| Usuwanie/degradacja admina | Tak | Nie |
| Nazwa, okładka, zaproszenia, typy | Tak | Tak |
| Strefa czasowa, wygaśnięcie, archiwizacja, usunięcie pokoju | Tak | Nie |

Lista członków pozostaje stronicowana i dostępna dla członków/gości. Odpowiedź zawiera `isAdmin`, `avatarUrl`, `rowVersion`, `canRemove`, `canPromote`, `canDemote`.

- PATCH `/api/rooms/{roomId}/members/{memberId}/role`: `{ isAdmin, rowVersion }`, 204.
- DELETE `/api/rooms/{roomId}/members/{memberId}`: `{ rowVersion }`, 204.
- Usunięcie zachowuje autorstwo i wydarzenia, kończy członkostwo oraz usuwa przypisanie do prywatnych folderów. Powrót przez zaproszenie zachowuje tożsamość członka, ale nie rolę admina. Dobrowolne opuszczenie też usuwa rolę.
- Nowe operacje zarządcze wymagają aktywnego pokoju. Zapytania zarządcze osób spoza pokoju zwracają 404; zwykli członkowie bez uprawnień otrzymują 403. Ograniczenia właścicielskie istniejących endpointów archiwizacji/usunięcia pozostają bez zmian.
- Mutacje typu/roli/zaproszenia/okładki/nazwy ponownie sprawdzają uprawnienia w transakcji z blokadą pokoju. Błędna wersja zwraca 409.

## Typy i wydarzenia — zmiana kontraktu

Stały enum `Category` zastąpiono `EventTypeId`. API i klienci muszą zostać wdrożeni razem.

- GET/POST `/api/rooms/{roomId}/event-types` i PATCH `/api/rooms/{roomId}/event-types/{typeId}`.
- Tworzenie: `{ name, color }`; nazwa po trimowaniu 1–64 znaki, unikalna bez rozróżniania wielkości liter w pokoju (także wśród archiwalnych); kolor sześć cyfr HEX **bez #**, zgodnie z istniejącym kontraktem kolorów wydarzeń.
- Edycja: `{ name?, color?, isArchived?, rowVersion }`. Nie ma fizycznego usuwania typu.
- Nowy pokój dostaje Meeting, Training, Social, Business trip, Leave, Shift; początkowy kolor każdego to `999999`.
- Tworzenie wydarzenia wymaga aktywnego typu z tego pokoju. Przy edycji można zachować przypisany typ archiwalny, lecz nie przypisać innego archiwalnego typu.
- Odpowiedź wydarzenia zawiera `eventTypeId`, `eventTypeName`, `eventTypeIsArchived`; kolor efektywny pochodzi z własnego koloru wydarzenia lub z typu. Zmiana koloru typu wpływa na wydarzenia bez własnego koloru.
- Lista pokojowa i globalna przyjmują filtr `eventTypeId`. Typ jest identyfikowany w kontekście konkretnego pokoju; frontend globalnej listy wymaga wybrania pokoju przed wyborem typu.
- Edycja cudzych wydarzeń nadal jest niedozwolona, również dla administratora.

## Zaproszenia i UI

Ustawienia kalendarza pokazują istniejący `JoinUrl`; odczyt nie tworzy zaproszenia. Nowe zaproszenie unieważnia poprzednie. UI potwierdza rotację, archiwizację i usunięcie kalendarza.

Profil i wygląd są dostępne z przycisku użytkownika. Kalendarz ma okna członków oraz ustawień, w tym nazwy, okładki, strefy, wygaśnięcia, typów i zaproszeń. Pola czasu wygaśnięcia są jawnie oznaczone UTC. Uprawnienia UI nie zastępują kontroli backendu. Konflikty wersji odświeżają dane i pokazują komunikat.

## Weryfikacja

- `dotnet test MultiPlaner.Server.slnf` — testy jednostkowe i integracyjne (SQL Server/Testcontainers).
- Można podać `MULTIPLANER_TEST_SQLSERVER`; fixture tworzy własną bazę o losowej nazwie i usuwa tylko tę bazę.
- `dotnet build MultiPlanerWeb/MultiPlanerWeb.csproj`.
- `dotnet build MultiPlanerApp/MultiPlanerApp.csproj -f net10.0-android` (wymaga workloadu i Android SDK).
- Testy SettingsTests obejmują wersjonowanie palety, sesje po zmianie hasła, macierz uprawnień, powrót usuniętego członka, izolację typów, archiwizację i kolory, dostęp do avatarów oraz niezależność nicków.
