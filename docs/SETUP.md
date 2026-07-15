# Setup — Toolchain installieren

Status der Umgebung (geprüft am 2026-06-17):

| Werkzeug      | Status |
|---------------|--------|
| git           | ✅ 2.54 |
| Node.js       | ✅ v24.16 |
| npm           | ✅ 11.13 |
| **.NET SDK**  | ❌ fehlt |
| **MAUI-Workload** | ❌ fehlt |
| Visual Studio | ❌ fehlt |

Es fehlt nur noch das .NET-MAUI-Toolset. Da du **voll cross-platform** (Windows +
Android + iOS) bauen willst, ist der Visual-Studio-Weg klar am bequemsten — er
installiert SDK, MAUI-Workloads, Android-SDK, OpenJDK und einen Emulator in einem
Rutsch.

## Empfohlen: Visual Studio 2022 Community + MAUI-Workload

**GUI-Weg:** VS 2022 Community herunterladen, im Installer den Workload
**„.NET Multi-platform App UI development"** anhaken, installieren.

**Oder per winget (eine Zeile):**

```powershell
winget install --id Microsoft.VisualStudio.2022.Community -e `
  --override "--quiet --norestart --add Microsoft.VisualStudio.Workload.NetCrossPlat --includeRecommended"
```

Das zieht automatisch das aktuelle .NET SDK + alle MAUI-Workloads
(`maui-windows`, `maui-android`, `maui-ios`, …) und das Android-Toolset.

## Alternative: nur CLI (ohne Visual Studio)

Schlanker, aber Android-Setup (SDK/Emulator) ist dann mehr Handarbeit:

```powershell
winget install --id Microsoft.DotNet.SDK.9 -e
dotnet workload install maui
```

## iOS-Hinweis

iOS-Builds brauchen zwingend einen **Mac als Build-Host** (Xcode + „Pair to Mac"
aus Visual Studio). Windows + Android funktionieren ohne Mac; iOS heben wir uns
auf, bis ein Mac verfügbar ist.

## Verifikation (danach bitte ausführen)

Neues Terminal öffnen (damit der PATH frisch ist), dann:

```powershell
dotnet --version          # sollte eine Version ausgeben (z.B. 9.0.x)
dotnet workload list      # sollte 'maui' bzw. maui-* Einträge zeigen
```

Wenn beides klappt: **kurz Bescheid geben** — dann scaffolde ich das
MAUI-Blazor-Hybrid-Projekt und binde Tailwind, LibVLCSharp und TagLib# ein.
