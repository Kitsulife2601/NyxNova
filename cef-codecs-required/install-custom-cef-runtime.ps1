param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRuntime,

    [string]$ReleaseFolder = "",

    [switch]$Apply,

    [int]$KeepBackups = 2
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptDir "..")

if ([string]::IsNullOrWhiteSpace($ReleaseFolder)) {
    $ReleaseFolder = Join-Path $projectRoot "publish\win-x64-release"
}

$source = Resolve-Path -LiteralPath $SourceRuntime
$target = Resolve-Path -LiteralPath $ReleaseFolder
$verifyScript = Join-Path $scriptDir "verify-cef-codecs.ps1"

& $verifyScript -SourceRuntime $source

$copyNames = @(
    "libcef.dll",
    "chrome_elf.dll",
    "libEGL.dll",
    "libGLESv2.dll",
    "icudtl.dat",
    "v8_context_snapshot.bin",
    "snapshot_blob.bin",
    "resources.pak",
    "chrome_100_percent.pak",
    "chrome_200_percent.pak",
    "locales",
    "swiftshader"
)

Write-Host ""
Write-Host "Zielordner:"
Write-Host "  $target"
Write-Host ""

if (-not $Apply) {
    Write-Host "VORSCHAU: Es wird noch nichts kopiert." -ForegroundColor Yellow
    Write-Host "Zum Anwenden denselben Befehl mit -Apply starten."
    Write-Host ""
}

$backup = Join-Path $target ("cef-backup-" + (Get-Date -Format "yyyyMMdd-HHmmss"))

if ($Apply) {
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Write-Host "Backup:"
    Write-Host "  $backup"
}

foreach ($name in $copyNames) {
    $src = Join-Path $source $name
    $dst = Join-Path $target $name

    if (-not (Test-Path -LiteralPath $src)) {
        continue
    }

    if (-not $Apply) {
        Write-Host "Wuerde kopieren: $name"
        continue
    }

    if (Test-Path -LiteralPath $dst) {
        Move-Item -LiteralPath $dst -Destination (Join-Path $backup $name) -Force
    }

    Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
    Write-Host "Kopiert: $name"
}

if ($Apply) {
    $oldBackups = Get-ChildItem -LiteralPath $target -Directory -Filter "cef-backup-*" |
        Sort-Object Name -Descending |
        Select-Object -Skip $KeepBackups

    foreach ($old in $oldBackups) {
        Remove-Item -LiteralPath $old.FullName -Recurse -Force
        Write-Host "Altes Backup entfernt: $($old.Name)"
    }
}

Write-Host ""
if ($Apply) {
    Write-Host "Custom-CEF-Runtime wurde eingesetzt." -ForegroundColor Green
    Write-Host "Starte danach Nova und oeffne nova://media-diagnostics."
} else {
    Write-Host "Vorschau abgeschlossen. Keine Dateien wurden geaendert."
}
