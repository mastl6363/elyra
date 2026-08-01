# Elyra — Musik Player

Moderner, performanter, werbefreier Musikplayer für lokale Sammlungen (MP3, FLAC).
Design auf dem Niveau von Apple Music / Spotify, umgesetzt mit Web-Technologien in
einer nativen Cross-Platform-App. Start als Donationware, später optionales SaaS.

## Tech-Stack

| Schicht        | Technologie                         |
|----------------|-------------------------------------|
| App-Framework  | .NET MAUI Blazor Hybrid             |
| Frontend / UI  | Razor + HTML + **Tailwind CSS (npm)** |
| Kern-Logik     | C# / .NET                           |
| Audio-Engine   | **LibVLCSharp** (FLAC + MP3, gapless) |
| Metadaten/ID3  | TagLib# (TagLibSharp)               |
| Lokaler Cache  | SQLite                             |

## Architektur

Keine Webseite: Die UI läuft in einem lokalen, unsichtbaren WebView; die C#-Logik
läuft **nativ** auf dem OS. Razor-Komponenten bilden die Brücke — ein Klick im
HTML triggert direkt native C#-Audio-/Datei-Funktionen. Keine Browser-Sandbox.

## Status

**Phase 1 (MVP) — in Entwicklung.** Projekt ist gescaffoldet (`src/Elyra`,
.NET 10, MAUI Blazor Hybrid), Solution `Elyra.slnx`. Integriert: Tailwind (npm,
v4), LibVLCSharp + native Binaries pro Plattform, TagLib#. Funktionsfähig auf
Windows: Ordner-Import, Metadaten-/Cover-/Genre-Auslesen, vollständige
Song-, Künstler- und Albenansichten mit kombinierbarer Suche, Filtern,
Sortierung und zustandserhaltender Detailnavigation, Künstlerdetail,
optionalen Albumgruppen, Album-Detail, Wiedergabe mit Player-Leiste (Play/Pause/Next/Prev/
Seek/Lautstärke, Auto-Advance), Now-Playing-Ansicht mit Queue, persistente
Wiedergabelisten (JSON in AppData), SQLite-Mediathekindex, Metadateneditor mit
Mehrfachauswahl, automatische Ordnerüberwachung, intelligente Wiedergabelisten,
Bibliotheksprüfung auf fehlende Dateien und mögliche Dubletten, Suche nach
Album/Künstler/Titel, MP3-/FLAC-Filter und Einstellungen für Bibliothek und
Wiedergabe sowie manueller MusicBrainz-Abgleich für fehlende Albumdaten.
Songtexte werden aus eingebetteten Tags und synchronisierten LRC-Dateien
angezeigt. Ein persistenter LibVLC-Equalizer bietet Presets, Vorverstärkung und
manuelle Frequenzbänder. Der Mini-Player reduziert die Oberfläche auf Titel,
Transport und Zeitleiste; unter Windows bleibt das kompakte Fenster im Vordergrund.
Automatisierte Tests decken Filter-, Persistenz-, Metadaten- und
Bibliothekslogik ab. Windows integriert Hardware-Medientasten, System-Medienoverlay,
Metadaten/Cover/Zeitleiste und robuste Fehlerbehandlung. Offen: Android/iOS-Tests.

## Plattform-Ziel

Voll Cross-Platform: **Windows, Android, iOS** (iOS-Build benötigt einen Mac).

## Entscheidungen

Architektur-/Werkzeug-Entscheidungen werden in [docs/DECISIONS.md](docs/DECISIONS.md)
protokolliert.

## Dev-Befehle

Tailwind-Quelle liegt in `src/Elyra/Styles/app.css`, Ausgabe (committet) in
`src/Elyra/wwwroot/css/app.css`. Der Build generiert das CSS automatisch via
MSBuild-Target `TailwindBuild`.

- `npm run css:watch` (in `src/Elyra`) — Tailwind im Watch-Modus für UI-Arbeit
- `dotnet build src/Elyra/Elyra.csproj -f net10.0-windows10.0.19041.0` — Windows-Build
- `dotnet test tests/Elyra.Tests/Elyra.Tests.csproj` — automatisierte Tests
- `dotnet build src/Elyra/Elyra.csproj -t:Run -f net10.0-windows10.0.19041.0` — App starten
- Alternativ: `Elyra.slnx` in Visual Studio öffnen, F5

> iOS-Build benötigt einen Mac (Pair to Mac).

## Roadmap

1. **Phase 1 — MVP / Solo-Player:** Setup, UI-Komponenten (Player, Playlist-Grid,
   Settings), lokale MP3/FLAC lesen, Cover extrahieren, flüssig abspielen.
2. **Phase 2 — BYOS:** Google Drive / WebDAV (NAS) anbinden und den vorhandenen
   SQLite-Metadatenindex dafür erweitern.
3. **Phase 3 — SaaS-Backend:** ASP.NET Core / Spring Boot, Stripe, isolierter
   Cloud-Speicher (S3), Docker-Deployment.
