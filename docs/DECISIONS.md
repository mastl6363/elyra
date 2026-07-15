# Architektur-Entscheidungen

Kurzes Log der weichenstellenden Entscheidungen. Neueste oben.

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
