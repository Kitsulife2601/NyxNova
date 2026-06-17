param(
    [string]$CertificatePath = $env:NYXNOVA_CERT_PATH,
    [string]$CertificatePassword = $env:NYXNOVA_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

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

$signParams = $null
if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Codesigning-Zertifikat wurde nicht gefunden: $CertificatePath"
    }

    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($null -eq $signtool) {
        throw "signtool.exe wurde nicht gefunden. Installiere das Windows SDK oder fuege signtool.exe zum PATH hinzu."
    }

    $signParams = "sign /f `"$CertificatePath`" /fd SHA256 /tr `"$TimestampUrl`" /td SHA256"
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signParams += " /p `"$CertificatePassword`""
    }

    Write-Host "Codesigning aktiv:" $CertificatePath
}
else {
    Write-Host "Codesigning ist aus: kein Zertifikat angegeben. Installer wird unsigniert gebaut." -ForegroundColor Yellow
}

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

$vpkArgs = @(
    "pack",
    "--packId", "NyxNova",
    "--packTitle", "NyxNova Browser",
    "--packAuthors", "Kitsulife2601",
    "--packVersion", $packVersion,
    "--packDir", $publishDir,
    "--mainExe", "NovaBrowser.CefSharp.exe",
    "--runtime", "win-x64",
    "--channel", "beta",
    "--outputDir", $releaseDir,
    "--icon", $iconFile,
    "--releaseNotes", $notesFile,
    "--splashProgressColor", "#B967FF",
    "--shortcuts", "Desktop,StartMenuRoot"
)

if ($signParams) {
    $vpkArgs += @("--signParams", $signParams)
}

vpk @vpkArgs

Write-Host ""
Write-Host "Installer fertig:"
Get-ChildItem -LiteralPath $releaseDir | Sort-Object LastWriteTime -Descending | Select-Object Name,Length,LastWriteTime | Format-Table -AutoSize
