# NyxNova Website

Diese statische Webseite kann direkt online gestellt werden.

## Lokal testen

```powershell
cd "C:\Users\denni\Documents\Bowseer test\NovaBrowser.CefSharp\docs"
python -m http.server 8088
```

Dann im Browser oeffnen:

```text
http://127.0.0.1:8088
```

## GitHub Pages

1. Repository auf GitHub oeffnen.
2. Settings -> Pages.
3. Source: `Deploy from a branch`.
4. Branch: `main`.
5. Folder: `/docs`.
6. Speichern.

Danach ist die Seite ueber GitHub Pages erreichbar.

## Download-Link

Der Button `Zum Download` zeigt aktuell auf:

```text
https://github.com/Kitsulife2601/NyxNova/releases/latest
```

Wenn spaeter eine feste Installer-Datei genutzt werden soll, kann der Link in `index.html` angepasst werden.
