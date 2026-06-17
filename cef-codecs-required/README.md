# NovaBrowser CEF Codec Runtime

Dieser Ordner beschreibt, was NovaBrowser.CefSharp fuer volle Video-Kompatibilitaet braucht.

## Warum das noetig ist

Die normale CefSharp/NuGet-Runtime kann je nach Build keine proprietaeren Codecs wie H.264 und AAC enthalten. Viele Video-Seiten liefern aber MP4/HLS mit H.264-Video und AAC-Audio aus. Wenn diese Codecs fehlen, sieht Nova zwar die Webseite, aber der Player meldet Quellenfehler oder laedt endlos.

## Benoetigter CEF-Build

Der CEF/Chromium-Build muss mit diesen Flags gebaut worden sein:

```text
proprietary_codecs=true
ffmpeg_branding=Chrome
```

Der Build muss zur verwendeten CefSharp-Version passen:

```text
CefSharp.Wpf.NETCore: 148.0.90
Architektur: win-x64
CEF Runtime: 148.0.9+g0d9d52a+chromium-148.0.7778.180
CEF Commit: 0d9d52a65c74c729e568ebf8506141a1e754eaeb
```

Die Fehlermeldung `MEDIA_ERR_SRC_NOT_SUPPORTED` bei `.mp4`/HLS ist ein starker Hinweis darauf, dass genau diese Codecs in der aktuell verwendeten Runtime fehlen.

## Build-Werkzeuge vorbereiten

Ein eigener CEF-Build ist gross. Plane mehrere Stunden und mindestens 250 GB freien Speicher ein.

Auf diesem Rechner braucht der Build:

```text
Git
Python 3.11 oder 3.12
CMake
Ninja
Visual Studio 2022 Build Tools mit C++ Desktop Workload
depot_tools
```

Der Helfer prueft alles:

```powershell
cd "C:\Users\denni\Documents\Bowseer test\NovaBrowser.CefSharp\cef-codecs-required"
powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1
```

Automatische Vorbereitung, soweit Windows/winget es erlaubt:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1 -InstallTools -InstallDepotTools
```

Falls danach nur noch `VS ATL: FEHLT` gemeldet wird, fehlt die Visual-Studio-Komponente mit `atldef.h`. Importiere dann im Visual Studio Installer diese Datei:

```text
C:\Users\denni\Documents\Bowseer test\NovaBrowser.CefSharp\cef-codecs-required\vs-cef-buildtools.vsconfig
```

Sie enthaelt die C++ Build Tools, Windows 11 SDK 26100 und ATL/MFC fuer das aktuell installierte MSVC 14.44 Toolset.

Wenn alle Werkzeuge gefunden werden, startet dieser Befehl den echten Build:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1 -StartBuild
```

Der Build erstellt nach Erfolg einen Runtime-Ordner:

```text
C:\cef-build\nova-cef-148\cef-148-codecs-win-x64
```

## Dateien, die zusammen aus demselben Build kommen muessen

Diese Dateien/Ordner duerfen nicht einzeln aus verschiedenen Builds gemischt werden:

```text
libcef.dll
chrome_elf.dll
libEGL.dll
libGLESv2.dll
icudtl.dat
v8_context_snapshot.bin
snapshot_blob.bin
resources.pak
chrome_100_percent.pak
chrome_200_percent.pak
locales\
swiftshader\
```

Wenn der Custom-Build weitere `.pak`, `.dat`, `.bin` oder Runtime-DLLs enthaelt, sollten sie aus demselben Runtime-Ordner mitkopiert werden.

## Verwendung

1. Lege die Custom-CEF-Runtime in einen eigenen Ordner, zum Beispiel:

```text
C:\CEF\cefsharp-148-codecs-win-x64
```

2. Pruefe den Ordner:

```powershell
powershell -ExecutionPolicy Bypass -File .\cef-codecs-required\verify-cef-codecs.ps1 -SourceRuntime "C:\CEF\cefsharp-148-codecs-win-x64"
```

3. Vorschau fuer den Austausch:

```powershell
powershell -ExecutionPolicy Bypass -File .\cef-codecs-required\install-custom-cef-runtime.ps1 -SourceRuntime "C:\CEF\cefsharp-148-codecs-win-x64"
```

4. Wirklich kopieren:

```powershell
powershell -ExecutionPolicy Bypass -File .\cef-codecs-required\install-custom-cef-runtime.ps1 -SourceRuntime "C:\CEF\cefsharp-148-codecs-win-x64" -Apply
```

5. Danach Nova starten und `nova://media-diagnostics` oeffnen.

Erwartet:

```text
video/mp4: probably oder maybe
H.264: probably oder maybe
AAC: probably oder maybe
MediaSource: ja
WebGL: ja
```

## Wichtiger Hinweis

H.264/AAC koennen lizenzrechtlich relevant sein. Vor Weitergabe einer EXE mit solchen Codecs sollte geprueft werden, ob die Verteilung erlaubt ist.

## Aktueller Befund

Wenn `nova://media-diagnostics` bei MP4/H.264/AAC `no` zeigt oder DevTools `MEDIA_ERR_SRC_NOT_SUPPORTED` meldet, kann Nova die Videodatei nicht decodieren. Cookie-, Popup- oder Privacy-Anpassungen beheben das dann nicht. Erst ein passender CEF-Runtime-Austausch mit `proprietary_codecs=true` und `ffmpeg_branding=Chrome` kann diese Klasse von Videos verbessern.

Der bisherige Build-Versuch kam bis zur echten Chromium-Kompilierung. Er stoppte bei:

```text
fatal error: 'atldef.h' file not found
```

Das bestaetigt: Der CEF-Codecs-Build ist korrekt gestartet, aber Visual Studio braucht noch ATL/MFC.
