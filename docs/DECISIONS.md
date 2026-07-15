# Architektur-Entscheidungen

Kurzes Log der weichenstellenden Entscheidungen. Neueste oben.

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
