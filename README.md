# Elyra — Musik Player

Moderner, performanter, werbefreier Musikplayer für lokale Sammlungen (MP3, FLAC, OGG Vorbis).
Design auf dem Niveau von Apple Music / Spotify, umgesetzt mit Web-Technologien in
einer nativen Cross-Platform-App.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Tech-Stack

| Schicht        | Technologie                         |
|----------------|-------------------------------------|
| App-Framework  | .NET MAUI Blazor Hybrid             |
| Frontend / UI  | Razor + HTML + Tailwind CSS (npm)   |
| Kern-Logik     | C# / .NET                           |
| Audio-Engine   | LibVLCSharp (MP3, FLAC, OGG Vorbis) |
| Metadaten/ID3  | TagLib# (TagLibSharp)               |
| Lokaler Cache  | JSON (AppData), SQLite ab Phase 2   |

## Architektur

Keine Webseite: Die UI läuft in einem lokalen, unsichtbaren WebView; die C#-Logik
läuft nativ auf dem OS. Razor-Komponenten bilden die Brücke — ein Klick im HTML
triggert direkt native C#-Audio-/Datei-Funktionen. Keine Browser-Sandbox.

Architektur- und Werkzeug-Entscheidungen sind chronologisch in
[docs/DECISIONS.md](docs/DECISIONS.md) protokolliert.

## Plattform-Ziel

Voll Cross-Platform: **Windows, Android, iOS** (iOS-Build benötigt einen Mac).

## Setup

Voraussetzungen und Toolchain-Installation: [docs/SETUP.md](docs/SETUP.md).

Kurzfassung für Windows:

```bash
dotnet workload install maui
dotnet build src/Elyra/Elyra.csproj -f net10.0-windows10.0.19041.0
```

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

## Android auf dem eigenen Gerät

Für ARM64-Geräte erzeugt `packaging/android/build-apk.sh` eine signierte APK unter
`artifacts/android/elyra-1.5.2-arm64.apk`. Der Build benötigt das .NET Android
Workload und einen Android-Signierschlüssel. Standardmäßig wird der lokale
Debug-Keystore (`~/.android/debug.keystore`) verwendet. Für spätere Updates
muss derselbe Schlüssel erhalten bleiben. Ein eigener Schlüssel kann über
`ELYRA_ANDROID_KEYSTORE`, `ELYRA_ANDROID_KEY_ALIAS`,
`ELYRA_ANDROID_STORE_PASSWORD` und `ELYRA_ANDROID_KEY_PASSWORD` gesetzt werden.

```bash
./packaging/android/build-apk.sh
adb devices
adb -s <geraete-id> install -r artifacts/android/elyra-1.5.2-arm64.apk
```

Auf Android wählt „Ordner auswählen“ einen Musikordner über den Systemdialog.
Elyra kopiert MP3-, FLAC- und OGG-Dateien daraus in den App-Speicher. Mit „Neu
einlesen“ werden spätere Änderungen im ausgewählten Ordner übernommen.

## Linux-Desktop (nativ, ohne Emulator)

`src/Elyra.Desktop` ist ein separater Blazor-Server-Host für Linux-Desktops. Er
teilt sich Components/Models/Services mit `src/Elyra` (per Datei-Link im
`.csproj`), läuft aber als lokaler Kestrel-Server (`127.0.0.1:5297`) statt in
einer MAUI-WebView und öffnet sich automatisch im Standardbrowser.

Voraussetzungen (einmalig, per `apt`): `libvlc5 libvlc-dev vlc-plugin-base
vlc-plugin-video-output libx11-6` (Audio- und Video-Engine) und `zenity`
(Dateiauswahl). Das `.deb` installiert die Laufzeitpakete als Abhängigkeiten.

```bash
# Dev-Loop (nutzt den NuGet-Cache für Framework-JS, kein Publish nötig)
cd src/Elyra.Desktop
ASPNETCORE_ENVIRONMENT=Development dotnet run

# "Produktions"-Start (empfohlen für den Alltagsgebrauch)
dotnet publish src/Elyra.Desktop/Elyra.Desktop.csproj -c Release -o out/Elyra.Desktop
dotnet out/Elyra.Desktop/Elyra.Desktop.dll
```

Lokale Filmdateien laufen in einem eigenen X11-Videofenster mit LibVLC. In der
Filmansicht lassen sich MP4, MKV, AVI, MOV und weitere Formate hinzufügen;
Wiedergabepositionen werden gespeichert. Im Fenster: Leertaste für Pause,
Pfeiltasten links/rechts für ±5 Sekunden und Esc zum Schließen. Eine X11- oder
XWayland-Sitzung (`DISPLAY`) ist erforderlich. DVD-Laufwerke und Menüs sind
derzeit nur im Windows-Build verfügbar.

## Nextcloud-Musik

Unter **Einstellungen → Nextcloud-Musik** die Server-Adresse, den Benutzernamen,
den Musikordner und ein Nextcloud-App-Passwort eintragen. Das App-Passwort lässt
sich in Nextcloud unter **Persönliche Einstellungen → Sicherheit** erstellen.
Elyra liest MP3-, FLAC- und OGG-Dateien per WebDAV ein, streamt sie bei Bedarf und
speichert vollständig abgespielte/abgerufene Dateien in einem lokalen Cache
von maximal 1 GiB. Beim nächsten Start bleiben Server-Adresse, Musikordner und
Mediathek erhalten; das App-Passwort muss erneut eingegeben werden. Bereits
zwischengespeicherte Titel können ohne Verbindung gespielt werden.

## Mitmachen

Beiträge sind willkommen — siehe [CONTRIBUTING.md](CONTRIBUTING.md) für den
Ablauf (Issues, Branches, Pull Requests, Tests) und
[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) für die Umgangsregeln.

## Lizenz

[MIT](LICENSE) — freie Nutzung, Veränderung und Weitergabe, auch kommerziell,
unter Erhalt des Copyright-Hinweises.
