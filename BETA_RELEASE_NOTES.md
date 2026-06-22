# NyxNova Browser Beta 1.0.11

Diese Beta enthaelt den aktuellen NyxNova-Stand mit eigenem Nova-Design, Custom-CEF-Runtime-Unterstuetzung und GitHub-basiertem Beta-Updater.

## Neu in dieser Beta

- Version auf `1.0.11` erhoeht, damit der GitHub-Updater eine neue Beta erkennt
- Settings-Bereiche sauber getrennt: Design, Suche, Lesezeichen, Downloads, Addons, Datenschutz und Build
- Lokaler Telegram-Bot bleibt lokal und wird im installierten Velopack-Build ausgeblendet
- Custom-CEF-Runtime wird beim Installer-Build wieder in den Publish-Ordner eingesetzt, falls vorhanden
- Installer-Assets werden im Velopack-kompatiblen Release-Ordner erzeugt

## Starten

Die installierbare Beta liegt nach dem Build unter:

`installer/Releases/NyxNova-beta-Setup.exe`

## Hinweis

Der Updater liest GitHub-Releases aus `Kitsulife2601/NyxNova`. Damit Clients diese Version sehen, muessen die Dateien aus `installer/Releases` als Release-Assets zu `v1.0.11-beta` hochgeladen werden.
