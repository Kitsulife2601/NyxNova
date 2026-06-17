# NyxNova Codesigning

NyxNova kann beim Installer-Build automatisch signiert werden, wenn ein echtes Codesigning-Zertifikat als `.pfx` vorhanden ist.

## Voraussetzungen

- Windows SDK mit `signtool.exe`
- Ein Codesigning-Zertifikat als `.pfx`
- Das Zertifikat-Passwort nicht in Git speichern

## Empfohlener Start

```powershell
$env:NYXNOVA_CERT_PATH="C:\Pfad\zu\nyxnova-codesign.pfx"
$env:NYXNOVA_CERT_PASSWORD="DEIN_PASSWORT"
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1
```

Oder direkt:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-installer.ps1 -CertificatePath "C:\Pfad\zu\nyxnova-codesign.pfx" -CertificatePassword "DEIN_PASSWORT"
```

Das Skript signiert dann die Velopack-App-Dateien und den Setup-Installer mit SHA256 und Zeitstempel.

## Ohne Zertifikat

Wenn kein Zertifikat angegeben ist, wird NyxNova weiter gebaut, aber der Installer bleibt unsigniert.
