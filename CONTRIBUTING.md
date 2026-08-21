# Beitragen zu Elyra

Danke für dein Interesse an Elyra! Diese Anleitung beschreibt den Ablauf für
Bug-Reports, Feature-Vorschläge und Pull Requests.

## Bevor du loslegst

- Lies [README.md](README.md) für einen Überblick über Tech-Stack und Architektur.
- Lies [docs/DECISIONS.md](docs/DECISIONS.md), um bereits getroffene
  Architektur-Entscheidungen (und deren Begründung) nicht versehentlich zu
  wiederholen oder zu widersprechen.
- Für größere Änderungen: bitte zuerst ein Issue eröffnen und die Idee
  kurz abstimmen, bevor viel Implementierungsarbeit investiert wird.

## Setup

Siehe [docs/SETUP.md](docs/SETUP.md) für die Toolchain (.NET SDK, MAUI-Workload).

```bash
dotnet build src/Elyra/Elyra.csproj -f net10.0-windows10.0.19041.0
dotnet test tests/Elyra.Tests/Elyra.Tests.csproj
```

## Branches & Commits

- Branch von `main` abzweigen, aussagekräftiger Name (`feature/…`, `fix/…`).
- Commit-Messages: kurz, im Imperativ, beschreiben das Warum, nicht nur das Was.
- Kleine, fokussierte Pull Requests sind leichter zu review-en als große.

## Code-Konventionen

- C#: Standard .NET-Konventionen, bestehenden Stil im jeweiligen Modul
  (`Services/`, `Models/`, `Components/`) beibehalten.
- UI/Styling: Tailwind-Klassen direkt in Razor-Markup, keine Inline-Styles.
- Reine Logik (Suche, Filter, Persistenz) bleibt außerhalb der Razor-Komponenten
  und ist unit-testbar (siehe `Services/LibraryFilter.cs` als Beispiel).

## Tests

Neue Logik in `Services/` oder `Models/` sollte, wo sinnvoll, von Tests in
`tests/Elyra.Tests` abgedeckt werden. Vor dem Öffnen eines PRs:

```bash
dotnet test tests/Elyra.Tests/Elyra.Tests.csproj
```

## Pull Requests

- Beschreibe kurz **was** sich ändert und **warum**.
- Verlinke das zugehörige Issue, falls vorhanden.
- Stelle sicher, dass Build und Tests grün sind (CI läuft automatisch).

## Fragen

Bei Unsicherheiten einfach ein Issue eröffnen — lieber einmal zu viel gefragt
als in die falsche Richtung entwickelt.
