param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRuntime
)

$ErrorActionPreference = "Stop"

$requiredFiles = @(
    "libcef.dll",
    "chrome_elf.dll",
    "libEGL.dll",
    "libGLESv2.dll",
    "icudtl.dat",
    "v8_context_snapshot.bin",
    "snapshot_blob.bin",
    "resources.pak",
    "chrome_100_percent.pak",
    "chrome_200_percent.pak"
)

$requiredDirs = @(
    "locales",
    "swiftshader"
)

$source = Resolve-Path -LiteralPath $SourceRuntime
Write-Host "Pruefe Custom-CEF-Runtime:"
Write-Host "  $source"
Write-Host ""

$missing = @()

foreach ($file in $requiredFiles) {
    $path = Join-Path $source $file
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $item = Get-Item -LiteralPath $path
        Write-Host ("OK    {0,-28} {1,12:n0} Bytes" -f $file, $item.Length)
    } else {
        Write-Host ("FEHLT {0}" -f $file) -ForegroundColor Red
        $missing += $file
    }
}

foreach ($dir in $requiredDirs) {
    $path = Join-Path $source $dir
    if (Test-Path -LiteralPath $path -PathType Container) {
        $count = (Get-ChildItem -LiteralPath $path -Recurse -File | Measure-Object).Count
        Write-Host ("OK    {0,-28} {1,12} Dateien" -f ($dir + "\"), $count)
    } else {
        Write-Host ("FEHLT {0}\" -f $dir) -ForegroundColor Red
        $missing += ($dir + "\")
    }
}

Write-Host ""
if ($missing.Count -gt 0) {
    Write-Host "Nicht bereit. Fehlende Teile:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "Runtime-Ordner sieht vollstaendig aus." -ForegroundColor Green
Write-Host "Wichtig: Dieses Script kann nicht beweisen, dass proprietary_codecs=true gesetzt ist."
Write-Host "Der echte Test passiert danach in Nova unter nova://media-diagnostics."
