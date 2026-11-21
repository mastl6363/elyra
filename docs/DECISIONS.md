# Architektur-Entscheidungen

Kurzes Log der weichenstellenden Entscheidungen. Neueste oben.

## 2026-11-22 — Hintergrundwiedergabe auf Android

- **Befund.** Android 17 beendete Elyra 1.5.2 während der lokalen Wiedergabe
  am 20. November um 20:32:42 und 21:42:44 Uhr. Das Geräteprotokoll nennt
  beide Male `EXCESSIVE CPU USAGE` und stuft den Prozess als `CACHED` ein.
  Android wird zuerst behoben. Die Diagnose für Windows und Linux ist
  zurückgestellt.
- **Hintergrunddienst und WakeLock.** Elyra startet bei Musik oder Radio
  einen Android-Foreground-Service vom Typ `mediaPlayback`. MediaSession und
  Medienbenachrichtigung zeigen den aktuellen Titel und stellen Mediensteuerung
  bereit. Der Dienst bleibt bei automatischen Titelwechseln aktiv. Bei Pause,
  Stopp, Ende der Warteschlange oder Wechsel zum Video gibt der Dienst den
  partiellen WakeLock frei und beendet sich. Die Medienbenachrichtigung
  verwendet die Android-Ausnahme für MediaSessions.
  [Android-Dokumentation zur Hintergrundwiedergabe](https://developer.android.com/media/media3/session/background-playback)
- **Wiedergabefehler.** Fehlende Dateien und native LibVLC-Fehler gehen in die
  Fehlerbehandlung der Warteschlange ein. Nach drei aufeinanderfolgenden
  Fehlern stoppt Elyra mit einer sichtbaren Meldung. Ein von LibVLC bestätigter
  Start unterbricht die Fehlerfolge. Ereignisse von abgelösten Titeln können
  den aktuellen Titel weder stoppen noch überspringen.
- **Audio-Dateien und Streams.** `:no-video` unterbindet die Videodekodierung
  von eingebetteten Bildern. Der separate Video- und DVD-Pfad lädt das Bild
  regulär.
- **Warteschlange und Status.** `PlaybackService` synchronisiert den Zugriff auf
  Titel, Quelle und Reihenfolge und veröffentlicht schreibgeschützte
  Warteschlangen-Snapshots. `PlayNext()`, `Previous()` und `StopVideo()`
  entscheiden unter derselben Sperre, unter der sie den Player steuern. Eine
  leere Liste stoppt die Wiedergabe. Beim Wechsel zu einer fehlenden Datei
  stoppt der vorherige Titel. Das Ende der Warteschlange aktualisiert die
  Oberfläche und den Hintergrunddienst.
- **Gerätetest.** In einer separaten Test-App auf dem betroffenen Android-17-Gerät
  blieben Dienst, WakeLock und Audioausgabe beim automatischen Titelwechsel
  mit ausgeschaltetem Display aktiv. Pause in Elyra und über die Android-
  Mediensteuerung gab den WakeLock frei. Fortsetzen startete den Dienst
  erneut. Nach drei aufeinanderfolgenden Lesefehlern erschien die Fehlermeldung
  und der Dienst wurde beendet.
- **Langzeittest offen.** Lokale Titel mindestens 90 Minuten bei gesperrtem
  Display abspielen und das Geräteprotokoll auf Prozessabbrüche prüfen.

## 2026-10-25 — OGG Vorbis in der Mediathek

- **OGG Vorbis lokal und über Nextcloud.** Dateien mit `.ogg` werden wie MP3
  und FLAC gescannt, mit TagLib# eingelesen und über LibVLC abgespielt. Der
  Formatfilter berücksichtigt auch die Dateiendung von Nextcloud-Titeln.

## 2026-10-18 — Nextcloud-Musik per WebDAV

- **Bedarfsgesteuertes Streaming.** Elyra liest die Ordnerstruktur mit WebDAV
  und streamt Titel über einen nur auf Loopback erreichbaren Proxy an LibVLC.
  Der Proxy unterstützt Bereichsanfragen für das Spulen.
- **Lokaler Cache.** Abgerufene Titel werden vollständig und atomar in AppData
  gespeichert; ein LRU-Limit von 1 GiB begrenzt den Platzbedarf.
- **App-Passwort nur im Arbeitsspeicher.** Server, Benutzer und Musikordner
  werden gespeichert, das App-Passwort muss pro Sitzung erneut eingegeben
  werden. Weder Mediathek noch Stream-URL enthalten Zugangsdaten.

## 2026-10-18 — Videowiedergabe im Linux-Desktop

- **Natives X11-Fenster für LibVLC.** Der lokale Blazor-Server kann Video nicht
  in den Browser einbetten, ohne auf Browser-Codecs beschränkt zu sein. LibVLC
  zeichnet deshalb in ein eigenes X11-Fenster; die bestehenden Player-Dienste
  steuern Wiedergabe und speichern Fortsetzungspositionen.
- **Videoausgabe als Paketabhängigkeit.** `vlc-plugin-video-output` und
  `libx11-6` werden mit dem Linux-Paket installiert. Die bisherige
  `vlc-plugin-base`-Abhängigkeit enthält keine X11-Videoausgabe.
- **DVD bleibt auf Windows.** Der Linux-Host unterstützt lokale Videodateien;
  die vorhandene MAUI-Seite übernimmt weiterhin DVD-Menüs auf Windows.

## 2026-07-15 — Künstlerzentrierte Mediathek

- **Primäre Navigation: Künstler statt Album.** Unvollständige MP3-Tags erzeugen
  keine künstlichen „Unbekanntes Album“-Karten mehr. Jeder Künstler führt zu
  allen lokal vorhandenen Songs; echte Albumangaben werden innerhalb der
  Künstlerseite als optionale Gruppen angezeigt.
- **Fehlendes Album bleibt leer.** Bestehende gespeicherte Platzhalter werden
  beim Laden automatisch migriert.
- **Online-Abgleich ist opt-in.** MusicBrainz ist ohne API-Schlüssel nutzbar,
  begrenzt Clients aber auf eine Anfrage pro Sekunde. Der Abgleich wird manuell
  in den Einstellungen gestartet, verwendet Künstler, Titel und Titeldauer und
  schreibt Ergebnisse nur in den lokalen Cache, nie ungefragt in Audiodateien.

## 2026-07-15 — Persistente Phase-1-Mediathek und Tests

- **Bibliotheks-Cache: JSON in AppData.** Der letzte Musikordner und seine
  eingelesenen Metadaten inklusive Cover werden als Snapshot gespeichert. So ist
  die Mediathek beim Neustart sofort verfügbar; SQLite bleibt für Phase 2 geplant.
- **Suche/Filter: reine C#-Logik.** Album-, Künstler- und Titelsuche sowie der
  MP3-/FLAC-Filter liegen außerhalb der Razor-Komponente und sind unit-testbar.
- **Tests: separates xUnit-Projekt.** Persistenz, Wiederherstellung und Filterung
  werden unabhängig von der nativen Audio-Engine geprüft.

## 2026-06-17 — Projektstart, Phase-1-Setup

- **App-Framework: .NET MAUI Blazor Hybrid.** Eine C#-Codebase für Windows,
  Android, iOS; UI per Web-Tech im nativen WebView, Logik nativ.
- **Plattform-Umfang MVP: voll cross-platform** (Windows + Android + iOS).
  iOS-Build benötigt einen Mac — wird zurückgestellt bis Mac verfügbar.
- **Tailwind: npm-basiert** (Node.js + package.json + PostCSS), statt Standalone-CLI.
  Begründung: mehr Flexibilität (Plugins), Node ist bereits installiert.
- **Audio-Engine: LibVLCSharp.** Robustes, plattformübergreifend identisches
  FLAC- + MP3-Playback inkl. gapless/ReplayGain-Optionen. Bewusst gewählt gegen
  Plugin.Maui.Audio (FLAC je Plattform unterschiedlich) und NAudio (Windows-only).
- **Metadaten: TagLib# (TagLibSharp)** zum Auslesen von Cover-Art, Künstler, Titel.
- **Toolchain-Install: durch Nutzer selbst** (siehe [SETUP.md](SETUP.md)).
