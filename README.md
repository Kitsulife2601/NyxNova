# NyxNova

NovaBrowser.CefSharp ist die neue Windows-Version des Browsers auf C#/.NET/WPF mit CEFSharp. Electron, React und Vite werden fuer diese Version nicht mehr verwendet.

## Starten

Voraussetzungen:

- Windows 10 oder Windows 11
- .NET 8 SDK oder Runtime
- Visual Studio 2022 mit `.NET desktop development`
- Plattform `x64`

In Visual Studio:

1. `NovaBrowser.CefSharp/NovaBrowser.CefSharp.sln` oeffnen.
2. Plattform auf `x64` stellen.
3. Startprojekt `NovaBrowser.CefSharp` waehlen.
4. Starten.

Per Terminal:

```powershell
dotnet restore .\NovaBrowser.CefSharp\NovaBrowser.CefSharp.sln
dotnet build .\NovaBrowser.CefSharp\NovaBrowser.CefSharp.sln -c Release -p:Platform=x64
.\NovaBrowser.CefSharp\src\NovaBrowser.CefSharp\bin\x64\Release\net8.0-windows\win-x64\NovaBrowser.CefSharp.exe
```

## Portable Ausgabe bauen

```powershell
dotnet publish .\NovaBrowser.CefSharp\src\NovaBrowser.CefSharp\NovaBrowser.CefSharp.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained false -o .\NovaBrowser.CefSharp\publish\win-x64
```

Danach starten:

```powershell
.\NovaBrowser.CefSharp\publish\win-x64\NovaBrowser.CefSharp.exe
```

## Funktionen

- Chrome-/Edge-aehnliche Tabs oben
- Neuer Tab, Tab schliessen, Tab wechseln
- `target="_blank"` und Popups werden als Nova-Tab geoeffnet, nicht als neues Windows-Fenster
- Toolbar mit Zurueck, Vorwaerts, Neu laden, Home, Adressleiste, Bookmark-Stern, Erweiterungs-Puzzle und Drei-Punkte-Menue
- Eingabe von URL oder Suche
- Suchmaschine waehlen: Google, DuckDuckGo oder Bing
- Interne Seiten im Tab-System:
  - `nova://start`
  - `nova://settings`
  - `nova://history`
  - `nova://downloads`
  - `nova://extensions`
- Startseite mit Nova-Look, Suchfeld und Quick Links
- Lokale Lesezeichen mit Bookmark-Leiste
- Verlauf mit Oeffnen, Entfernen und Loeschen
- CEFSharp-Downloads mit Fortschritt, Oeffnen, Im Ordner anzeigen und Entfernen
- Interne Erweiterungsverwaltung mit Aktivieren/Deaktivieren, Pin/Unpin und Entfernen
- Persistente Cookies, Cache und Sitzungen fuer Google, YouTube und normale Webseiten
- Normaler Chrome-User-Agent ohne Electron-Hinweis

## Speicherorte

Nova speichert Nutzerdaten getrennt als JSON unter:

```text
%AppData%\NovaBrowser.CefSharp
```

Dateien:

- `settings.json`
- `bookmarks.json`
- `history.json`
- `downloads.json`
- `extensions.json`
- `sessions.json`

CEFSharp-Cache und Cookies liegen dauerhaft unter:

```text
%LocalAppData%\NovaBrowser.CefSharp
```

## Google Login

NovaBrowser.CefSharp nutzt CEFSharp/Chromium mit persistenten Cookies und einem normalen Chrome-User-Agent. Google, YouTube und Google-Konto-Seiten werden intern geladen.

Wichtig: Google kann eingebettete Browser trotzdem blockieren. Wenn Google die Anmeldung ablehnt, zeigt Nova eine interne Meldung:

```text
Google blockiert eingebettete Browser. Bitte extern anmelden oder OAuth verwenden.
```

Nova deaktiviert dafuer keine Web-Sicherheit und verwendet keine unsicheren Anti-Erkennungs-Tricks.

## Projektstruktur

```text
NovaBrowser.CefSharp/
  NovaBrowser.CefSharp.sln
  README.md
  src/NovaBrowser.CefSharp/
    App.xaml
    App.xaml.cs
    MainWindow.xaml
    MainWindow.xaml.cs
    Browser/
      BrowserTab.cs
      BrowserTabView.xaml
    Models/
      Bookmark.cs
      BrowserSettings.cs
      DownloadItem.cs
      ExtensionItem.cs
      HistoryItem.cs
      SessionState.cs
    Services/
      AddressParser.cs
      BookmarkService.cs
      DownloadService.cs
      ExtensionService.cs
      HistoryService.cs
      JsonStore.cs
      NovaDownloadHandler.cs
      NovaLifeSpanHandler.cs
      SettingsService.cs
```
