# NyxNova GitHub Installer

Dieser Installer ist ein Bootstrap-Installer fuer Windows.

Er macht Folgendes:

1. Fragt, ob eine Desktop-Verknuepfung erstellt werden soll.
2. Fragt nach dem Installationsordner.
3. Zeigt AGB/Hinweise an und installiert erst nach Zustimmung.
4. Laedt nur die build-relevanten Dateien aus `src/NovaBrowser.CefSharp/` von GitHub herunter.
5. Baut die App lokal mit `dotnet publish`.
6. Installiert nur die fertigen Publish-Dateien, nicht den Quellcode.
7. Erstellt eine Startmenue-Verknuepfung und optional eine Desktop-Verknuepfung.
8. Fragt am Ende, ob NyxNova direkt gestartet werden soll.

Wenn NyxNova bereits installiert ist, startet der Installer im Wartungsmodus:

- Reparieren: Platzhalter, kommt noch.
- Komplett reinstallieren: entfernt die alte Installation und installiert neu.
- Deinstallieren: entfernt Programmdateien und Verknuepfungen.

## Starten

```powershell
.\NyxNova-GitHub-Installer.cmd
```

Oder direkt:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\NyxNova-GitHub-Installer.ps1
```

## Voraussetzungen

- Windows 10 oder Windows 11
- Internetverbindung
- .NET 8 SDK oder neuer

Der Installer baut aus Quellcode. Wenn `dotnet` nicht installiert ist, bricht er mit einer Meldung ab.
