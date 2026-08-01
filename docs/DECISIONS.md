# Architektur-Entscheidungen

Kurzes Log der weichenstellenden Entscheidungen. Neueste oben.

## 2026-08-01 — Songtexte, Equalizer und Mini-Player

- **Songtexte bleiben lokal und dateinah.** Elyra liest zuerst eine gleichnamige
  `.lrc`-Datei und danach eingebettete Lyrics-Tags. Zeitmarken werden geparst,
  hervorgehoben und sind direkt anspringbar; ein ungeklärtes Online-Lizenzmodell wird nicht
  durch Scraping umgangen.
- **Der Equalizer liegt vor beiden Audio-Playern.** LibVLC-Presets,
  Vorverstärkung und alle Frequenzbänder werden live auf Haupt- und
  Crossfade-Player angewendet und zusammen mit den Wiedergabeeinstellungen
  lokal gespeichert.
- **Der Mini-Player ist ein UI-Modus desselben Fensters.** Queue und
  Audio-Instanzen laufen ohne Übergabe weiter. Auf Windows wird das Fenster
  verkleinert und im Vordergrund gehalten; beim Verlassen werden die vorherigen
  Maße wiederhergestellt.

## 2026-08-01 — Mediathek-Index und automatische Sammlungen

- **SQLite ersetzt den JSON-Snapshot als Mediathekindex.** Beim ersten Start wird
  der vorhandene Snapshot automatisch übernommen. Persönliche Daten wie
  Favoriten, Hörverlauf und eigene Wiedergabelisten bleiben getrennt in JSON.
- **Tags werden nur nach expliziter Auswahl geschrieben.** Der Metadateneditor
  unterstützt einzelne und mehrere Titel und ändert Künstler, Album, Genre und
  Jahr über TagLib# direkt in den Audiodateien.
- **Der Musikordner wird entprellt überwacht.** Änderungen an MP3- und FLAC-Dateien
  lösen nach kurzer Ruhezeit einen vollständigen, robusten Neuabgleich aus.
- **Intelligente Wiedergabelisten sind berechnete Ansichten.** Noch ungehörte,
  lange nicht gehörte, neue, verlustfreie und genrebezogene Listen entstehen
  aus Mediathek und lokalem Hörverlauf und benötigen keine zweite Persistenz.
- **Die Bibliotheksprüfung löscht nicht automatisch.** Fehlende Dateien und
  mögliche Dubletten werden zunächst nur gemeldet; das Bereinigen fehlender
  Einträge bleibt eine bewusste Nutzeraktion.

## 2026-07-31 — Gemeinsame Mediathek-Navigation

- **Songs sind die vollständige Standardansicht.** Künstler und Alben bleiben
  eigene Perspektiven auf dieselbe gefilterte Titelmenge; Dateien ohne
  Albumangabe verschwinden dadurch nicht aus der Mediathek.
- **Filter greifen auf Titel-Ebene.** Freitext, Künstler, Genre und Dateiformat
  lassen sich kombinieren. Jede Ansicht ergänzt dazu passende Sortierfelder.
- **Der Browse-Zustand bleibt in der App-Sitzung erhalten.** Beim Öffnen eines
  Künstlers oder Albums und anschließender Rückkehr bleiben Ansicht, Suche,
  Filter und Sortierung bestehen. Alben merken zusätzlich, ob sie aus einer
  Künstlerseite geöffnet wurden.

## 2026-07-30 — Windows-Mediensteuerung und Wiedergabefehler

- **SMTC wird manuell an LibVLC gekoppelt.** Da Elyra nicht den Windows-
  `MediaPlayer` verwendet, verbindet ein Windows-spezifischer Dienst die
  vorhandene Queue über `ISystemMediaTransportControlsInterop` mit
  Hardwaretasten, Medienoverlay, Metadaten, Cover und Zeitleiste.
- **Plattformcode bleibt hinter einer gemeinsamen Schnittstelle.** Windows
  registriert die native Implementierung; Android und iOS erhalten bis zu ihrer
  jeweiligen Media-Session-Integration einen No-op-Dienst.
- **Musikfehler blockieren die Queue nicht.** Fehlende oder von LibVLC nicht
  lesbare lokale Titel werden mit sichtbarem Hinweis übersprungen. Radio- und
  Videofehler behalten den Wiedergabekontext und bieten einen erneuten Versuch.

## 2026-07-30 — Übergänge und Lautstärke

- **Eine LibVLC-Instanz, zwei Audio-Player.** Der zweite Player lädt den nächsten
  lokalen Titel vor. Crossfade überblendet beide Player; „lückenlos“ verwendet
  denselben Pfad mit einem sehr kurzen Übergang. Radio und Video bleiben davon
  getrennt.
- **Übergänge sind Queue-gesteuert.** Shuffle, Repeat und manuelle Queue-Änderungen
  bestimmen weiterhin eindeutig den nächsten Titel. Manuelle Navigation,
  Pausieren und Suchen brechen einen laufenden Übergang ab.
- **Normalisierung ist eine Startoption.** Der VLC-Normalizer wird beim Erzeugen
  der nativen Engine aktiviert. Eine Änderung der Einstellung wirkt deshalb
  bewusst erst nach dem nächsten App-Start.

## 2026-07-30 — Persistenter Spotify-artiger Hörkontext

- **Queue ist eigener Anwendungszustand.** Titel können als Nächstes oder am Ende
  eingereiht, umsortiert, entfernt und gemeinsam geleert werden. Shuffle und die
  Repeat-Modi Aus/Alle/Eins bleiben unabhängig davon im Playback-Dienst.
- **Wiederaufnahme startet nicht ungefragt.** Queue, aktueller Titel,
  Wiedergabeposition, Lautstärke, Shuffle und Repeat werden lokal gespeichert,
  aber nach einem App-Neustart erst durch eine Nutzeraktion fortgesetzt.
- **Favoriten und Verlauf bleiben lokal.** Lieblingssongs, letzte Wiedergaben und
  Wiedergabezähler liegen in einer separaten JSON-Datei in AppData. Gespeichert
  werden kompakte Track-Snapshots ohne Coverdaten.
- **Audio-Engine erhält eine testbare Grenze.** `IAudioPlayerService` trennt die
  Queue-/Sitzungslogik von LibVLCSharp und ermöglicht deterministische Unit-Tests.

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
