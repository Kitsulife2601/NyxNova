param(
    [string]$Owner = "Kitsulife2601",
    [string]$Repo = "NyxNova",
    [string]$Tag = "v1.0.9-beta",
    [string]$Commitish = "main"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$releaseDir = Join-Path $projectRoot "installer\Releases"
$vaultPath = Join-Path $projectRoot "upload-token-vault.json"

if (-not (Test-Path -LiteralPath $releaseDir)) {
    throw "Release-Ordner nicht gefunden: $releaseDir"
}

$assetNames = @(
    "RELEASES-beta",
    "assets.beta.json",
    "releases.beta.json",
    "NyxNova-1.0.9-beta-full.nupkg",
    "NyxNova-beta-Setup.exe"
)

$assetsToUpload = foreach ($name in $assetNames) {
    $path = Join-Path $releaseDir $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Release-Datei nicht gefunden: $path"
    }
    Get-Item -LiteralPath $path
}

Write-Host ""
Write-Host "NyxNova GitHub Release Upload" -ForegroundColor Magenta
Write-Host "Release: $Tag"
Write-Host "Ordner:  $releaseDir"
Write-Host "Assets:"
$assetsToUpload | ForEach-Object {
    Write-Host ("  - {0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
}
Write-Host ""

Add-Type -AssemblyName System.Security
Add-Type -AssemblyName System.Net.Http

if (-not ("NyxNova.ProgressableStreamContent" -as [type])) {
    Add-Type -ReferencedAssemblies "System.Net.Http.dll" -TypeDefinition @"
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace NyxNova
{
    public sealed class ProgressableStreamContent : HttpContent
    {
        private readonly Stream content;
        private readonly int bufferSize;
        private readonly Action<long, long> progress;
        private readonly long length;

        public ProgressableStreamContent(Stream content, int bufferSize, Action<long, long> progress)
        {
            this.content = content;
            this.bufferSize = bufferSize;
            this.progress = progress;
            this.length = content.Length;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var buffer = new byte[bufferSize];
            long uploaded = 0;
            int read;

            while ((read = await content.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                uploaded += read;
                progress(uploaded, length);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = this.length;
            return true;
        }
    }
}
"@
}

function Convert-SecureStringToPlainText {
    param([Security.SecureString]$SecureValue)

    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringAuto($ptr)
    }
    finally {
        if ($ptr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
        }
    }
}

function Get-PinEntropy {
    param([string]$Pin)

    $bytes = [Text.Encoding]::UTF8.GetBytes("NyxNovaUpload:$Pin")
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }
}

function Save-TokenWithPin {
    param([string]$TokenValue)

    $securePin = Read-Host "Neue PIN" -AsSecureString
    $pin = Convert-SecureStringToPlainText $securePin

    $plain = [Text.Encoding]::UTF8.GetBytes($TokenValue)
    $entropy = Get-PinEntropy $pin
    $cipher = [Security.Cryptography.ProtectedData]::Protect($plain, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    @{
        createdAt = (Get-Date).ToString("o")
        cipher = [Convert]::ToBase64String($cipher)
    } | ConvertTo-Json | Set-Content -LiteralPath $vaultPath -Encoding UTF8

    Write-Host "Token wurde lokal verschluesselt gespeichert." -ForegroundColor Green
}

function Read-NewTokenAndStore {
    Write-Host "GitHub-Token einmalig einfuegen. Eingabe bleibt versteckt." -ForegroundColor Yellow
    $secureToken = Read-Host "Token" -AsSecureString
    $tokenValue = Convert-SecureStringToPlainText $secureToken
    Save-TokenWithPin -TokenValue $tokenValue
    return $tokenValue
}

function Read-TokenWithPin {
    if (Test-Path -LiteralPath $vaultPath) {
        Write-Host "Gespeicherter GitHub-Token gefunden." -ForegroundColor Green
        $securePin = Read-Host "PIN" -AsSecureString
        $pin = Convert-SecureStringToPlainText $securePin
        try {
            $vault = Get-Content -LiteralPath $vaultPath -Raw | ConvertFrom-Json
            $cipher = [Convert]::FromBase64String([string]$vault.cipher)
            $entropy = Get-PinEntropy $pin
            $plain = [Security.Cryptography.ProtectedData]::Unprotect($cipher, $entropy, [Security.Cryptography.DataProtectionScope]::CurrentUser)
            return [Text.Encoding]::UTF8.GetString($plain)
        }
        catch {
            Write-Host "PIN falsch oder Token-Speicher ist ungueltig." -ForegroundColor Red
            $backupPath = "$vaultPath.broken-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Move-Item -LiteralPath $vaultPath -Destination $backupPath -Force
            Write-Host "Alter Token-Speicher wurde gesichert:" -ForegroundColor Yellow
            Write-Host $backupPath -ForegroundColor DarkGray
            return Read-NewTokenAndStore
        }
    }

    Write-Host "Kein gespeicherter Token gefunden." -ForegroundColor Yellow
    return Read-NewTokenAndStore
}

$token = Read-TokenWithPin

$headers = @{
    "Authorization"        = "Bearer $token"
    "Accept"               = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "NyxNova-Release-Uploader"
}

$api = "https://api.github.com/repos/$Owner/$Repo"

Write-Host ""
Write-Host "Pruefe Release auf GitHub ..." -ForegroundColor Cyan
try {
    $release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
}
catch {
    Write-Host "Release nicht gefunden. Erstelle Release $Tag ..." -ForegroundColor Yellow
    $releaseBody = @{
        tag_name = $Tag
        target_commitish = $Commitish
        name = "NyxNova $Tag"
        body = "NyxNova Beta Update $Tag"
        draft = $false
        prerelease = $true
    } | ConvertTo-Json
    $release = Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -Body $releaseBody -ContentType "application/json"
}
Write-Host "Release gefunden: $($release.html_url)" -ForegroundColor Green

function Send-FileWithProgress {
    param(
        [string]$Url,
        [string]$Path,
        [hashtable]$Headers
    )

    $file = Get-Item -LiteralPath $Path
    $stream = [IO.File]::OpenRead($Path)
    $started = Get-Date
    $lastDraw = Get-Date

    try {
        $client = [Net.Http.HttpClient]::new()
        $client.Timeout = [TimeSpan]::FromHours(2)
        foreach ($name in $Headers.Keys) {
            [void]$client.DefaultRequestHeaders.TryAddWithoutValidation($name, [string]$Headers[$name])
        }

        $callbackRunspace = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace
        $progressCallback = [Action[Int64, Int64]]{
            param([Int64]$uploaded, [Int64]$total)
            if ($null -eq [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace) {
                [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace = $callbackRunspace
            }
            $now = Get-Date
            if (($now - $script:lastDraw).TotalMilliseconds -ge 250 -or $uploaded -eq $total) {
                $percent = if ($total -gt 0) { ($uploaded / $total) * 100 } else { 100 }
                $elapsed = [Math]::Max(0.1, ($now - $started).TotalSeconds)
                $speed = ($uploaded / 1MB) / $elapsed
                $remainingMb = [Math]::Max(0, ($total - $uploaded) / 1MB)
                $etaText = if ($speed -gt 0.01) {
                    $etaSeconds = [int]($remainingMb / $speed)
                    $eta = [TimeSpan]::FromSeconds($etaSeconds)
                    if ($eta.TotalHours -ge 1) { "{0:D2}:{1:mm}:{1:ss}" -f [int]$eta.TotalHours, $eta }
                    else { "{0:mm}:{0:ss}" -f $eta }
                } else { "--:--" }
                $line = "{0,6:N2}%  {1:N1}/{2:N1} MB  {3:N1} MB/s  ETA {4}" -f $percent, ($uploaded / 1MB), ($total / 1MB), $speed, $etaText
                Write-Host "`r$line" -NoNewline -ForegroundColor Cyan
                $script:lastDraw = $now
            }
        }

        $script:lastDraw = $lastDraw
        $content = [NyxNova.ProgressableStreamContent]::new($stream, 1048576, $progressCallback)
        $content.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/octet-stream")

        $response = $client.PostAsync($Url, $content).GetAwaiter().GetResult()
        $responseBody = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        Write-Host ""

        if (-not $response.IsSuccessStatusCode) {
            throw "Upload fehlgeschlagen: HTTP $([int]$response.StatusCode) $($response.ReasonPhrase)`n$responseBody"
        }
    }
    finally {
        $stream.Dispose()
        if ($content) { $content.Dispose() }
        if ($client) { $client.Dispose() }
    }
}

foreach ($file in $assetsToUpload) {
    $release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
    $existing = @($release.assets | Where-Object { $_.name -eq $file.Name })
    foreach ($asset in $existing) {
        Write-Host "Vorhandenes Asset wird entfernt: $($asset.name)" -ForegroundColor Yellow
        Invoke-RestMethod -Method Delete -Uri $asset.url -Headers $headers | Out-Null
    }

    $sizeMb = [Math]::Round($file.Length / 1MB, 1)
    $uploadUrl = "https://uploads.github.com/repos/$Owner/$Repo/releases/$($release.id)/assets?name=$([Uri]::EscapeDataString($file.Name))"

    Write-Host ""
    Write-Host "Upload startet: $($file.Name) ($sizeMb MB)" -ForegroundColor Cyan
    Write-Host "Fortschritt wird live im Terminal angezeigt." -ForegroundColor DarkGray
    Write-Host ""
    Send-FileWithProgress -Url $uploadUrl -Path $file.FullName -Headers $headers
}

Write-Host ""
Write-Host "Upload fertig. Pruefe Assets ..." -ForegroundColor Green
$release = Invoke-RestMethod -Uri "$api/releases/tags/$Tag" -Headers $headers
$release.assets | Select-Object name, size, browser_download_url | Format-Table -AutoSize

Write-Host ""
Write-Host "Release-Link:" -ForegroundColor Magenta
Write-Host $release.html_url
