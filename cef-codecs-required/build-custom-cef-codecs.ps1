param(
    [string]$WorkRoot = "C:\cef-build\nova-cef-148",
    [string]$CefCommit = "0d9d52a65c74c729e568ebf8506141a1e754eaeb",
    [string]$DepotTools = "",
    [switch]$InstallTools,
    [switch]$InstallDepotTools,
    [switch]$StartBuild,
    [switch]$ForceClean,
    [ValidateRange(1, 64)]
    [int]$BuildJobs = 1
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "== $Text ==" -ForegroundColor Cyan
}

function Get-RealCommand {
    param([string[]]$Names)

    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command -and $command.Source -and ($command.Source -notlike "*\WindowsApps\*")) {
            return $command.Source
        }
    }

    $lowerNames = $Names | ForEach-Object { $_.ToLowerInvariant() }
    $knownCandidates = @()

    if ($lowerNames -contains "git.exe" -or $lowerNames -contains "git") {
        $knownCandidates += "C:\Program Files\Git\cmd\git.exe"
    }

    if ($lowerNames -contains "python.exe" -or $lowerNames -contains "python3.exe" -or $lowerNames -contains "py.exe") {
        $knownCandidates += @(
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python312\python.exe"),
            (Join-Path $env:LOCALAPPDATA "Programs\Python\Python311\python.exe"),
            "C:\Program Files\Python312\python.exe",
            "C:\Program Files\Python311\python.exe",
            "C:\Windows\py.exe"
        )
    }

    if ($lowerNames -contains "cmake.exe" -or $lowerNames -contains "cmake") {
        $knownCandidates += "C:\Program Files\CMake\bin\cmake.exe"
    }

    if ($lowerNames -contains "ninja.exe" -or $lowerNames -contains "ninja") {
        $knownCandidates += Get-ChildItem -Path (Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages") -Recurse -File -Filter "ninja.exe" -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty FullName
    }

    foreach ($candidate in $knownCandidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return $candidate
        }
    }

    return $null
}

function Install-WithWinget {
    param(
        [string]$Id,
        [string]$Name,
        [string[]]$ExtraArgs = @()
    )

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "winget wurde nicht gefunden. Installiere $Name bitte manuell."
    }

    Write-Host "Installiere $Name ueber winget..."
    $args = @(
        "install",
        "--id", $Id,
        "--exact",
        "--accept-package-agreements",
        "--accept-source-agreements",
        "--disable-interactivity"
    ) + $ExtraArgs

    & $winget.Source @args
}

function Install-DepotTools {
    param([string]$Target)

    if (Test-Path -LiteralPath $Target) {
        Write-Host "depot_tools existiert bereits: $Target"
        return
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Target) | Out-Null
    $zip = Join-Path (Split-Path -Parent $Target) "depot_tools.zip"

    Write-Host "Lade depot_tools..."
    Invoke-WebRequest -Uri "https://storage.googleapis.com/chrome-infra/depot_tools.zip" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $Target -Force
    Remove-Item -LiteralPath $zip -Force
}

function Get-VisualStudioPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return $null
    }

    $path = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        return $null
    }

    return $path.Trim()
}

function Get-AnyVisualStudioBuildToolsPath {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $path = & $vswhere -latest -products * -property installationPath
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            return $path.Trim()
        }
    }

    $fallback = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools"
    if (Test-Path -LiteralPath $fallback) {
        return $fallback
    }

    return $null
}

function Install-VSNativeDesktopWorkload {
    $setup = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\setup.exe"
    $installPath = Get-AnyVisualStudioBuildToolsPath

    if (-not (Test-Path -LiteralPath $setup)) {
        Install-WithWinget `
            -Id "Microsoft.VisualStudio.2022.BuildTools" `
            -Name "Visual Studio 2022 Build Tools" `
            -ExtraArgs @("--override", "--wait --quiet --norestart --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended")
        $installPath = Get-AnyVisualStudioBuildToolsPath
    }

    if (-not $installPath) {
        throw "Visual Studio Build Tools wurden installiert, der Installationspfad wurde aber nicht gefunden."
    }

    Write-Host "Installiere/ergaenze C++ Desktop Workload..."
    & $setup modify `
        --installPath $installPath `
        --quiet `
        --norestart `
        --add Microsoft.VisualStudio.Workload.VCTools `
        --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
        --add Microsoft.VisualStudio.Component.VC.ATL `
        --add Microsoft.VisualStudio.Component.VC.ATLMFC `
        --add Microsoft.VisualStudio.Component.VC.14.44.17.14.ATL `
        --add Microsoft.VisualStudio.Component.VC.14.44.17.14.MFC `
        --includeRecommended
}

function Test-VSAtlHeaders {
    param([string]$VisualStudioPath)

    if (-not $VisualStudioPath) {
        return $false
    }

    $headers = Get-ChildItem -Path (Join-Path $VisualStudioPath "VC\Tools\MSVC") -Recurse -File -Filter "atldef.h" -ErrorAction SilentlyContinue |
        Select-Object -First 1

    return [bool]$headers
}

function Find-CefRuntimePackage {
    param([string]$Root)

    $candidate = Get-ChildItem -LiteralPath $Root -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "cef_binary_*_windows64*" -and (Test-Path (Join-Path $_.FullName "Release\libcef.dll")) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $candidate) {
        return $null
    }

    return $candidate.FullName
}

function New-CodecsRuntimeFolder {
    param(
        [string]$CefBinaryFolder,
        [string]$TargetRuntime
    )

    $release = Join-Path $CefBinaryFolder "Release"
    $resources = Join-Path $CefBinaryFolder "Resources"

    if (-not (Test-Path -LiteralPath (Join-Path $release "libcef.dll"))) {
        throw "Release\libcef.dll wurde im CEF-Build nicht gefunden."
    }

    if (Test-Path -LiteralPath $TargetRuntime) {
        $backup = "$TargetRuntime.backup-$(Get-Date -Format yyyyMMdd-HHmmss)"
        Move-Item -LiteralPath $TargetRuntime -Destination $backup -Force
        Write-Host "Altes Runtime-Paket gesichert: $backup"
    }

    New-Item -ItemType Directory -Force -Path $TargetRuntime | Out-Null

    $releasePatterns = @("*.dll", "*.bin", "*.dat", "*.pak", "*.json")
    foreach ($pattern in $releasePatterns) {
        Get-ChildItem -LiteralPath $release -Filter $pattern -File -ErrorAction SilentlyContinue |
            Copy-Item -Destination $TargetRuntime -Force
    }

    foreach ($pattern in @("*.pak", "*.dat", "*.bin")) {
        Get-ChildItem -LiteralPath $resources -Filter $pattern -File -ErrorAction SilentlyContinue |
            Copy-Item -Destination $TargetRuntime -Force
    }

    foreach ($folder in @("locales", "swiftshader")) {
        $fromRelease = Join-Path $release $folder
        $fromResources = Join-Path $resources $folder
        $sourceFolder = if (Test-Path -LiteralPath $fromRelease) { $fromRelease } elseif (Test-Path -LiteralPath $fromResources) { $fromResources } else { $null }

        if ($sourceFolder) {
            Copy-Item -LiteralPath $sourceFolder -Destination (Join-Path $TargetRuntime $folder) -Recurse -Force
        }
    }

    Write-Host "Codec-Runtime vorbereitet:"
    Write-Host "  $TargetRuntime" -ForegroundColor Green
}

$workRootFull = [System.IO.Path]::GetFullPath($WorkRoot)
New-Item -ItemType Directory -Force -Path $workRootFull | Out-Null

if ([string]::IsNullOrWhiteSpace($DepotTools)) {
    $DepotTools = Join-Path $workRootFull "depot_tools"
}
$DepotTools = [System.IO.Path]::GetFullPath($DepotTools)

Write-Step "Nova CEF Codec Build"
Write-Host "CEF Version: 148.0.9+g0d9d52a+chromium-148.0.7778.180"
Write-Host "CEF Commit:  $CefCommit"
Write-Host "WorkRoot:    $workRootFull"
Write-Host "depot_tools: $DepotTools"
Write-Host "Build-Jobs:  $BuildJobs"
Write-Host "GN_DEFINES:  is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome chrome_pgo_phase=0 use_siso=false"

if ($InstallTools) {
    Write-Step "Installiere Build-Werkzeuge"
    if (-not (Get-RealCommand @("git.exe", "git"))) {
        Install-WithWinget -Id "Git.Git" -Name "Git"
    }

    if (-not (Get-RealCommand @("python.exe", "py.exe", "python3.exe"))) {
        Install-WithWinget -Id "Python.Python.3.11" -Name "Python 3.11"
    }

    if (-not (Get-RealCommand @("cmake.exe", "cmake"))) {
        Install-WithWinget -Id "Kitware.CMake" -Name "CMake"
    }

    if (-not (Get-RealCommand @("ninja.exe", "ninja"))) {
        Install-WithWinget -Id "Ninja-build.Ninja" -Name "Ninja"
    }

    if (-not (Get-VisualStudioPath)) {
        Install-VSNativeDesktopWorkload
    }
}

if ($InstallDepotTools) {
    Write-Step "Installiere depot_tools"
    Install-DepotTools -Target $DepotTools
}

$toolProblems = New-Object System.Collections.Generic.List[string]
$git = Get-RealCommand @("git.exe", "git")
$python = Get-RealCommand @("python3.exe", "python.exe", "py.exe")
$cmake = Get-RealCommand @("cmake.exe", "cmake")
$ninja = Get-RealCommand @("ninja.exe", "ninja")
$vs = Get-VisualStudioPath
$hasAtl = Test-VSAtlHeaders -VisualStudioPath $vs

if (-not $git) { $toolProblems.Add("Git fehlt.") }
if (-not $python) { $toolProblems.Add("Python fehlt oder ist nur der WindowsApps-Platzhalter.") }
if (-not $cmake) { $toolProblems.Add("CMake fehlt.") }
if (-not $ninja) { $toolProblems.Add("Ninja fehlt.") }
if (-not $vs) { $toolProblems.Add("Visual Studio 2022 Build Tools mit C++ Build Tools fehlen.") }
if ($vs -and -not $hasAtl) { $toolProblems.Add("Visual Studio ATL/MFC Header fehlen (atldef.h).") }
if (-not (Test-Path -LiteralPath $DepotTools)) { $toolProblems.Add("depot_tools fehlt: $DepotTools") }

$drive = Get-PSDrive -Name ([System.IO.Path]::GetPathRoot($workRootFull).Substring(0, 1))
$freeGb = [math]::Round($drive.Free / 1GB, 1)
if ($freeGb -lt 250) {
    $toolProblems.Add("Zu wenig freier Speicher auf $($drive.Name):\: $freeGb GB frei, empfohlen sind mindestens 250 GB.")
}

Write-Step "Voraussetzungen"
Write-Host ("Git:        {0}" -f ($(if ($git) { $git } else { "FEHLT" })))
Write-Host ("Python:     {0}" -f ($(if ($python) { $python } else { "FEHLT" })))
Write-Host ("CMake:      {0}" -f ($(if ($cmake) { $cmake } else { "FEHLT" })))
Write-Host ("Ninja:      {0}" -f ($(if ($ninja) { $ninja } else { "FEHLT" })))
Write-Host ("VS Build:   {0}" -f ($(if ($vs) { $vs } else { "FEHLT" })))
Write-Host ("VS ATL:     {0}" -f ($(if ($hasAtl) { "OK" } else { "FEHLT" })))
Write-Host ("Speicher:   {0} GB frei" -f $freeGb)

if ($toolProblems.Count -gt 0) {
    Write-Host ""
    Write-Host "Noch nicht buildbereit:" -ForegroundColor Yellow
    foreach ($problem in $toolProblems) {
        Write-Host "  - $problem" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "Naechster Versuch mit automatischer Vorbereitung:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1 -InstallTools -InstallDepotTools"
    Write-Host ""
    Write-Host "Wenn nur VS ATL fehlt, im Visual Studio Installer diese Datei importieren:"
    Write-Host "  $scriptDir\vs-cef-buildtools.vsconfig"
    Write-Host ""
    Write-Host "Wenn danach alles OK ist, starte den echten Build mit:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1 -StartBuild"
    exit 1
}

if (-not $StartBuild) {
    Write-Host ""
    Write-Host "Alles sieht fuer den Build vorbereitet aus." -ForegroundColor Green
    Write-Host "Der echte CEF-Build dauert typischerweise mehrere Stunden."
    Write-Host "Start:"
    Write-Host "  powershell -ExecutionPolicy Bypass -File .\build-custom-cef-codecs.ps1 -StartBuild"
    exit 0
}

Write-Step "Starte CEF Build"
$automateDir = Join-Path $workRootFull "automate"
$automate = Join-Path $automateDir "automate-git.py"
$downloadDir = Join-Path $workRootFull "chromium_git"
$logsDir = Join-Path $workRootFull "logs"
New-Item -ItemType Directory -Force -Path $automateDir, $logsDir | Out-Null

if (-not (Test-Path -LiteralPath $automate)) {
    Invoke-WebRequest -Uri "https://bitbucket.org/chromiumembedded/cef/raw/master/tools/automate/automate-git.py" -OutFile $automate
}

$automateContent = Get-Content -LiteralPath $automate -Raw
$patchedAutoninja = "command = 'autoninja -j $BuildJobs '"
if ($automateContent -notmatch [regex]::Escape($patchedAutoninja)) {
    $automateContent = $automateContent -replace "command = 'autoninja(?: -j \d+)? '", $patchedAutoninja
    Set-Content -LiteralPath $automate -Value $automateContent -Encoding UTF8
}

$toolPathParts = @(
    $DepotTools,
    (Split-Path -Parent $git),
    (Split-Path -Parent $python),
    (Split-Path -Parent $cmake),
    (Split-Path -Parent $ninja)
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$env:PATH = (($toolPathParts -join ";") + ";" + $env:PATH)
$env:DEPOT_TOOLS_WIN_TOOLCHAIN = "0"
$env:CEF_USE_GN = "1"
$env:GYP_MSVS_VERSION = "2022"
$env:vs2022_install = $vs
$env:GYP_MSVS_OVERRIDE_PATH = $vs
$env:GN_DEFINES = "is_official_build=true proprietary_codecs=true ffmpeg_branding=Chrome chrome_pgo_phase=0 use_siso=false"
$env:CEF_ARCHIVE_FORMAT = "tar.bz2"

$arguments = @(
    $automate,
    "--download-dir=$downloadDir",
    "--depot-tools-dir=$DepotTools",
    "--checkout=$CefCommit",
    "--x64-build",
    "--minimal-distrib",
    "--client-distrib",
    "--no-debug-build",
    "--no-debug-tests",
    "--no-release-tests",
    "--force-build"
)

if ($ForceClean) {
    $arguments += "--force-clean"
}

$logFile = Join-Path $logsDir ("cef-build-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")
Write-Host "Log:"
Write-Host "  $logFile"
Write-Host ""
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
try {
    & $python @arguments 2>&1 | Tee-Object -FilePath $logFile
    $buildExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

if ($buildExitCode -ne 0) {
    throw "CEF-Build wurde mit Exit-Code $buildExitCode beendet. Pruefe das Log: $logFile"
}

Write-Step "Packe Runtime fuer Nova"
$cefBinary = Find-CefRuntimePackage -Root $downloadDir
if (-not $cefBinary) {
    throw "CEF-Binary-Distribution wurde nicht gefunden. Pruefe das Build-Log: $logFile"
}

$runtimeTarget = Join-Path $workRootFull "cef-148-codecs-win-x64"
New-CodecsRuntimeFolder -CefBinaryFolder $cefBinary -TargetRuntime $runtimeTarget

Write-Host ""
Write-Host "Danach in Nova einsetzen:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\install-custom-cef-runtime.ps1 -SourceRuntime `"$runtimeTarget`" -Apply"
