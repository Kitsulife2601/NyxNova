# Neuer Tab

Eine moderne Startseite für Chrome, Edge und Firefox. Nach dem Laden der Erweiterung öffnet sie automatisch in jedem neuen Tab. Der Klick auf das Addon öffnet die Seite ebenfalls.

## Chrome und Edge

1. Chrome oder Edge öffnen.
2. Erweiterungen-Seite öffnen:
   - Chrome: `chrome://extensions`
   - Edge: `edge://extensions`
3. Entwicklermodus aktivieren.
4. `Entpackte Erweiterung laden` auswählen.
5. Den Ordner `C:\Users\denni\Desktop\webseite adon` auswählen.

Danach erscheint das Addon oben in der Browserleiste und ersetzt die Neuer-Tab-Seite.

## Hintergrund ändern

In der Suchleiste kannst du diese Befehle eingeben:

- `!backround` oder `!background`: Bild oder Video als neuen Hintergrund hochladen.
- `!backround upload` oder `!background upload`: macht dasselbe.
- `!backround reset` oder `!background reset`: setzt den Hintergrund zurück.

Bilder wie PNG, JPG, JPEG, WebP und GIF sowie Videos wie MP4 und WebM werden unterstützt.

## Uhr einstellen

In der Sidebar kannst du die Uhr zwischen `Digital` und `Zeiger` umstellen. Für die digitale Uhr kannst du außerdem die Schrift ändern: `Neon`, `Klar`, `Mono` oder `Elegant`.

## Firefox

Firefox braucht für diese Erweiterung eine eigene Manifest-Datei. Dafür gibt es die Datei `manifest.firefox.json`.

Wichtig: Die ZIP-Datei nicht über `about:addons` installieren. Normales Firefox blockiert unsignierte Addons dort mit der Meldung, dass das Addon nicht verifiziert wurde.

Zum Testen lade den vorbereiteten Firefox-Ordner so:

1. Firefox öffnen.
2. `about:debugging#/runtime/this-firefox` öffnen.
3. `Temporäres Add-on laden` auswählen.
4. Im Ordner `C:\Users\denni\Desktop\Novaaddon-firefox` die Datei `manifest.json` auswählen.

Firefox entfernt temporäre Addons nach einem Browser-Neustart. Für eine dauerhafte Installation muss die Erweiterung als Firefox-Addon signiert oder über Mozilla Add-ons veröffentlicht werden.

### Firefox dauerhaft speichern

Normales Firefox speichert temporäre Addons nicht dauerhaft. Nach einem Neustart sind sie wieder weg. Außerdem blockiert normales Firefox unsignierte `.xpi`-Dateien.

Es gibt zwei Wege:

1. Empfohlen: Die Datei `C:\Users\denni\Desktop\novaaddon-firefox-amo.zip` bei Mozilla Add-ons hochladen und signieren lassen. Wenn du sie privat behalten möchtest, wähle bei Mozilla die Verteilung `Auf eigene Faust` beziehungsweise `Self-distribution`. Danach bekommst du eine signierte `.xpi`, die in normalem Firefox dauerhaft installiert bleibt.
2. Nur zum Entwickeln: Firefox Developer Edition, Nightly oder ESR nutzen, in `about:config` die Einstellung `xpinstall.signatures.required` auf `false` setzen und dann die Datei `C:\Users\denni\Desktop\novaaddon-firefox-unsigned.xpi` installieren. Das ist nicht für normales Firefox gedacht.

## Hintergrund-Updates

Wenn der Browser ein Update für die Erweiterung bereitstellt, lädt sich Novaaddon automatisch neu.

## Suchbefehle

- `!background upload` oder `!bg upload`: eigenen Hintergrund als Bild oder Video speichern.
- `!background reset` oder `!bg reset`: eigenen Hintergrund zurücksetzen.
- `!lese https://example.com`: neues Lesezeichen mit automatischem Seiten-Icon speichern.
- `!lese https://example.com Name`: neues Lesezeichen mit eigenem Namen speichern.

## Übersetzung

Die automatische Webseiten-Übersetzung fragt erst beim Einschalten nach Zugriff auf Webseiten. Ohne diese einmalige Erlaubnis bleibt die Übersetzung aus.

Wichtig: Das funktioniert nur bei einer richtig installierten, gepackten oder veröffentlichten Erweiterung. Wenn du sie als entpackte oder temporäre Erweiterung lädst, tauscht der Browser die Dateien nicht automatisch aus GitHub aus. Dann musst du den Ordner nach einem GitHub-Update neu laden.

## GitHub

Vor dem Hochladen keinen Discord-Token in Dateien speichern. Der Token gehört nur beim Starten des lokalen Bots in die PowerShell:

```powershell
$env:DISCORD_BOT_TOKEN="DEIN_NEUER_TOKEN"
node discord-bot/heart-server.js
```
