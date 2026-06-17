$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectRoot "src\NovaBrowser.CefSharp\NovaBrowser.CefSharp.csproj"
$publishDir = Join-Path $projectRoot "installer\publish"
$releaseDir = Join-Path $projectRoot "installer\Releases"
$notesFile = Join-Path $projectRoot "BETA_RELEASE_NOTES.md"
$iconFile = Join-Path $projectRoot "src\NovaBrowser.CefSharp\Assets\nyxnova-icon.ico"
$version = [xml](Get-Content -LiteralPath $projectFile)
$packVersion = $version.Project.PropertyGroup.Version
$cefSource = "C:\cef-build\nova-cef-148\cef-148-codecs-win-x64"
$cefInstallScript = Join-Path $projectRoot "cef-codecs-required\install-custom-cef-runtime.ps1"

Write-Host "NyxNova Installer-Build startet..."
Write-Host "Publish:" $publishDir
Write-Host "Releases:" $releaseDir
Write-Host "Version:" $packVersion

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Get-ChildItem -LiteralPath $releaseDir -Force | Remove-Item -Recurse -Force

dotnet publish $projectFile -c Release -r win-x64 --self-contained true -o $publishDir

if (Test-Path -LiteralPath (Join-Path $cefSource "libcef.dll")) {
    powershell -ExecutionPolicy Bypass -File $cefInstallScript -SourceRuntime $cefSource -ReleaseFolder $publishDir -Apply
}
else {
    Write-Host "Custom-CEF wurde nicht gefunden. Installer wird mit Standard-CEF gebaut." -ForegroundColor Yellow
}

vpk pack `
    --packId "NyxNova" `
    --packTitle "NyxNova Browser" `
    --packAuthors "Kitsulife2601" `
    --packVersion $packVersion `
    --packDir $publishDir `
    --mainExe "NovaBrowser.CefSharp.exe" `
    --runtime "win-x64" `
    --channel "beta" `
    --outputDir $releaseDir `
    --icon $iconFile `
    --releaseNotes $notesFile `
    --splashProgressColor "#B967FF" `
    --shortcuts "Desktop,StartMenuRoot"

Write-Host ""
Write-Host "Installer fertig:"
Get-ChildItem -LiteralPath $releaseDir | Sort-Object LastWriteTime -Descending | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize
