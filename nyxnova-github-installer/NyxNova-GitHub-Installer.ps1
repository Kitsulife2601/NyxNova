param(
    [string]$Repository = "Kitsulife2601/NyxNova",
    [string]$Branch = "main",
    [string]$GitHubToken = "PASTE_GITHUB_TOKEN_HERE"
)

$ErrorActionPreference = "Stop"
$ConfirmPreference = "None"
$Global:ConfirmPreference = "None"
$PSDefaultParameterValues['*:Confirm'] = $false
$PSDefaultParameterValues['Remove-Item:Confirm'] = $false
$PSDefaultParameterValues['Remove-Item:Force'] = $true

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$script:GitHubToken = $GitHubToken
$script:desktopShortcut = $true
$script:installPath = Join-Path $env:LOCALAPPDATA "Programs\NyxNova"
$script:acceptedTerms = $false
$script:launchAfterInstall = $true
$script:cleanInstall = $false
$script:stateDir = Join-Path $env:LOCALAPPDATA "NyxNovaInstaller"
$script:stateFile = Join-Path $script:stateDir "install.json"
$script:splashPath = Join-Path $PSScriptRoot "nyxnova-installer-splash.png"

function New-GitHubClient {
    $client = New-Object System.Net.WebClient
    $client.Headers.Add("User-Agent", "NyxNova-Installer")
    if (-not [string]::IsNullOrWhiteSpace($script:GitHubToken)) {
        $client.Headers.Add("Authorization", "Bearer $script:GitHubToken")
    }
    return $client
}

function Show-InstallerMessage {
    param(
        [string]$Message,
        [string]$Title = "NyxNova Installer",
        [System.Windows.Forms.MessageBoxIcon]$Icon = [System.Windows.Forms.MessageBoxIcon]::Information
    )
    [void][System.Windows.Forms.MessageBox]::Show($Message, $Title, [System.Windows.Forms.MessageBoxButtons]::OK, $Icon)
}

function Get-InstalledNyxNova {
    $paths = New-Object System.Collections.Generic.List[string]

    if (Test-Path -LiteralPath $script:stateFile) {
        try {
            $state = Get-Content -LiteralPath $script:stateFile -Raw | ConvertFrom-Json
            if ($state.installPath) { $paths.Add([string]$state.installPath) }
        } catch {}
    }

    $paths.Add((Join-Path $env:LOCALAPPDATA "Programs\NyxNova"))

    foreach ($path in ($paths | Select-Object -Unique)) {
        $exe = Join-Path $path "NovaBrowser.CefSharp.exe"
        if (Test-Path -LiteralPath $exe) {
            return [pscustomobject]@{ InstallPath = $path; ExePath = $exe }
        }
    }
    return $null
}

function Save-InstallState {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $script:stateDir)) { [System.IO.Directory]::CreateDirectory($script:stateDir) | Out-Null }
    @{
        installPath = $Path
        installedAt = (Get-Date).ToString("o")
        repository  = $Repository
        branch      = $Branch
    } | ConvertTo-Json | Set-Content -LiteralPath $script:stateFile -Encoding UTF8
}

function Remove-ShortcutIfExists {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Force -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function Remove-DirectoryForce {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
            try { $_.Attributes = [IO.FileAttributes]::Normal } catch {}
        }
        Remove-Item -LiteralPath $Path -Recurse -Force -Confirm:$false -ErrorAction Stop
    } catch {
        # Fallback: cmd /c rmdir
        & cmd /c "rmdir /s /q `"$Path`"" 2>$null
    }
}

function Uninstall-NyxNova {
    param([string]$Path)

    $running = Get-Process -Name "NovaBrowser.CefSharp" -ErrorAction SilentlyContinue
    if ($running) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "NyxNova laeuft noch und muss geschlossen werden um fortzufahren.`r`n`r`nJetzt automatisch schliessen?",
            "NyxNova schliessen",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) {
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 1500
        } else {
            throw "Installation abgebrochen. Bitte schliesse NyxNova und starte den Installer erneut."
        }
    }

    Remove-ShortcutIfExists (Join-Path ([Environment]::GetFolderPath("Desktop")) "NyxNova.lnk")
    $startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NyxNova"
    Remove-DirectoryForce $startMenuDir
    Remove-DirectoryForce $Path

    if (Test-Path -LiteralPath $script:stateFile) {
        Remove-Item -LiteralPath $script:stateFile -Force -Confirm:$false -ErrorAction SilentlyContinue
    }
}

function New-InstallerForm {
    param([string]$Title)

    $form = New-Object System.Windows.Forms.Form
    $form.Text = $Title
    $form.StartPosition = "CenterScreen"
    $form.FormBorderStyle = "FixedDialog"
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.ClientSize = New-Object System.Drawing.Size(720, 390)
    $form.Font = New-Object System.Drawing.Font("Segoe UI", 9)
    $form.BackColor = [System.Drawing.Color]::White

    if (Test-Path -LiteralPath $script:splashPath) {
        $picture = New-Object System.Windows.Forms.PictureBox
        $picture.Image = [System.Drawing.Image]::FromFile($script:splashPath)
        $picture.SizeMode = "Zoom"
        $picture.BackColor = [System.Drawing.Color]::FromArgb(10, 10, 18)
        $picture.Location = New-Object System.Drawing.Point(0, 0)
        $picture.Size = New-Object System.Drawing.Size(230, 390)
        $form.Controls.Add($picture)
    }
    return $form
}

function Add-Title {
    param([System.Windows.Forms.Form]$Form, [string]$Text, [string]$SubText)

    $title = New-Object System.Windows.Forms.Label
    $title.Text = $Text
    $title.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
    $title.AutoSize = $true
    $title.Location = New-Object System.Drawing.Point(260, 26)
    $Form.Controls.Add($title)

    $subtitle = New-Object System.Windows.Forms.Label
    $subtitle.Text = $SubText
    $subtitle.AutoSize = $false
    $subtitle.Size = New-Object System.Drawing.Size(420, 58)
    $subtitle.Location = New-Object System.Drawing.Point(262, 70)
    $Form.Controls.Add($subtitle)
}

function Add-NavButtons {
    param(
        [System.Windows.Forms.Form]$Form,
        [scriptblock]$OnBack,
        [scriptblock]$OnNext,
        [string]$NextText = "Weiter"
    )

    $back = New-Object System.Windows.Forms.Button
    $back.Text = "Zurueck"
    $back.Size = New-Object System.Drawing.Size(96, 32)
    $back.Location = New-Object System.Drawing.Point(496, 340)
    $back.Enabled = $null -ne $OnBack
    if ($OnBack) { $back.Add_Click($OnBack) }
    $Form.Controls.Add($back)

    $next = New-Object System.Windows.Forms.Button
    $next.Text = $NextText
    $next.Size = New-Object System.Drawing.Size(96, 32)
    $next.Location = New-Object System.Drawing.Point(604, 340)
    $next.Add_Click($OnNext)
    $Form.Controls.Add($next)

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = "Abbrechen"
    $cancel.Size = New-Object System.Drawing.Size(96, 32)
    $cancel.Location = New-Object System.Drawing.Point(260, 340)
    $cancel.Add_Click({ $Form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $Form.Close() })
    $Form.Controls.Add($cancel)
}

function Show-MaintenancePage {
    param($Install)

    $form = New-InstallerForm "NyxNova Wartung"
    Add-Title $form "NyxNova ist bereits installiert" "Waehle, was der Installer mit der bestehenden Installation machen soll."

    $path = New-Object System.Windows.Forms.Label
    $path.Text = "Gefunden: $($Install.InstallPath)"
    $path.AutoSize = $false
    $path.Size = New-Object System.Drawing.Size(420, 38)
    $path.Location = New-Object System.Drawing.Point(262, 130)
    $form.Controls.Add($path)

    $repair = New-Object System.Windows.Forms.Button
    $repair.Text = "Reparieren"
    $repair.Size = New-Object System.Drawing.Size(135, 38)
    $repair.Location = New-Object System.Drawing.Point(262, 190)
    $repair.Add_Click({ Show-InstallerMessage "Reparieren kommt noch." "NyxNova Installer" Information })
    $form.Controls.Add($repair)

    $reinstall = New-Object System.Windows.Forms.Button
    $reinstall.Text = "Komplett reinstallieren"
    $reinstall.Size = New-Object System.Drawing.Size(160, 38)
    $reinstall.Location = New-Object System.Drawing.Point(410, 190)
    $reinstall.Add_Click({
        # 1. NyxNova laueft noch?
        $running = Get-Process -Name "NovaBrowser.CefSharp" -ErrorAction SilentlyContinue
        if ($running) {
            $answer = [System.Windows.Forms.MessageBox]::Show(
                "NyxNova laeuft noch und muss geschlossen werden.`r`n`r`nJetzt automatisch schliessen?",
                "NyxNova schliessen",
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Warning
            )
            if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) {
                $running | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 1500
            } else {
                return
            }
        }

        # 2. Alte Dateien loeschen
        $installDir = $Install.InstallPath
        if (Test-Path -LiteralPath $installDir) {
            & cmd /c "rmdir /s /q `"$installDir`"" 2>$null
            Start-Sleep -Milliseconds 800
        }
        Remove-ShortcutIfExists (Join-Path ([Environment]::GetFolderPath("Desktop")) "NyxNova.lnk")
        $smDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NyxNova"
        & cmd /c "rmdir /s /q `"$smDir`"" 2>$null
        if (Test-Path -LiteralPath $script:stateFile) {
            Remove-Item -LiteralPath $script:stateFile -Force -Confirm:$false -ErrorAction SilentlyContinue
        }

        # 3. Neu installieren
        $script:installPath = $installDir
        $script:cleanInstall = $false
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Yes
        $form.Close()
    })
    $form.Controls.Add($reinstall)

    $uninstall = New-Object System.Windows.Forms.Button
    $uninstall.Text = "Deinstallieren"
    $uninstall.Size = New-Object System.Drawing.Size(120, 38)
    $uninstall.Location = New-Object System.Drawing.Point(584, 190)
    $uninstall.Add_Click({
        $answer = [System.Windows.Forms.MessageBox]::Show("NyxNova wirklich deinstallieren?", "NyxNova Installer", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Question)
        if ($answer -eq [System.Windows.Forms.DialogResult]::Yes) {
            try {
                Uninstall-NyxNova $Install.InstallPath
                Show-InstallerMessage "NyxNova wurde deinstalliert." "NyxNova Installer" Information
                $form.DialogResult = [System.Windows.Forms.DialogResult]::No
                $form.Close()
            } catch {
                Show-InstallerMessage $_.Exception.Message "Deinstallation fehlgeschlagen" Error
            }
        }
    })
    $form.Controls.Add($uninstall)

    $close = New-Object System.Windows.Forms.Button
    $close.Text = "Schliessen"
    $close.Size = New-Object System.Drawing.Size(96, 32)
    $close.Location = New-Object System.Drawing.Point(604, 340)
    $close.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })
    $form.Controls.Add($close)

    return $form.ShowDialog()
}

function Show-ShortcutPage {
    $form = New-InstallerForm "NyxNova installieren"
    Add-Title $form "NyxNova Installer" "Dieser Installer laedt NyxNova von GitHub herunter, baut die App lokal und installiert nur die fertigen Programmdateien."

    $check = New-Object System.Windows.Forms.CheckBox
    $check.Text = "Desktop-Verknuepfung erstellen"
    $check.Checked = $script:desktopShortcut
    $check.AutoSize = $true
    $check.Location = New-Object System.Drawing.Point(264, 154)
    $form.Controls.Add($check)

    Add-NavButtons $form $null {
        $script:desktopShortcut = $check.Checked
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    }
    return $form.ShowDialog()
}

function Show-InstallPathPage {
    $form = New-InstallerForm "Installationsordner"
    Add-Title $form "Speicherort auswaehlen" "Du kannst den vorgeschlagenen Ordner nutzen oder einen eigenen Installationsordner waehlen."

    $label = New-Object System.Windows.Forms.Label
    $label.Text = "Installationsordner:"
    $label.AutoSize = $true
    $label.Location = New-Object System.Drawing.Point(264, 138)
    $form.Controls.Add($label)

    $pathBox = New-Object System.Windows.Forms.TextBox
    $pathBox.Text = $script:installPath
    $pathBox.Size = New-Object System.Drawing.Size(315, 26)
    $pathBox.Location = New-Object System.Drawing.Point(264, 164)
    $form.Controls.Add($pathBox)

    $browse = New-Object System.Windows.Forms.Button
    $browse.Text = "Durchsuchen"
    $browse.Size = New-Object System.Drawing.Size(104, 28)
    $browse.Location = New-Object System.Drawing.Point(590, 163)
    $browse.Add_Click({
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        $dialog.Description = "NyxNova Installationsordner auswaehlen"
        $dialog.SelectedPath = $pathBox.Text
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $pathBox.Text = $dialog.SelectedPath
        }
    })
    $form.Controls.Add($browse)

    Add-NavButtons $form {
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Retry
        $form.Close()
    } {
        if ([string]::IsNullOrWhiteSpace($pathBox.Text)) {
            Show-InstallerMessage "Bitte waehle einen Installationsordner aus." "NyxNova Installer" Warning
            return
        }
        $script:installPath = $pathBox.Text.Trim()
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    }
    return $form.ShowDialog()
}

function Show-TermsPage {
    $form = New-InstallerForm "AGB akzeptieren"
    Add-Title $form "AGB und Hinweise" "Bitte lies und akzeptiere die Bedingungen, bevor NyxNova installiert wird."

    $terms = New-Object System.Windows.Forms.TextBox
    $terms.Multiline = $true
    $terms.ReadOnly = $true
    $terms.ScrollBars = "Vertical"
    $terms.Size = New-Object System.Drawing.Size(430, 128)
    $terms.Location = New-Object System.Drawing.Point(264, 122)
    $terms.Text = @"
NyxNova wird von GitHub heruntergeladen und lokal aus dem Quellcode gebaut.

Installiert werden nur die veroeffentlichten Programmdateien aus dem Publish-Ordner. Quellcode, ZIP-Dateien und temporaere Build-Dateien bleiben nicht im Installationsordner.

Voraussetzungen: Internetverbindung und .NET 8 SDK oder neuer.
"@
    $form.Controls.Add($terms)

    $accept = New-Object System.Windows.Forms.CheckBox
    $accept.Text = "Ich akzeptiere die AGB und moechte NyxNova installieren."
    $accept.Checked = $script:acceptedTerms
    $accept.AutoSize = $true
    $accept.Location = New-Object System.Drawing.Point(264, 262)
    $form.Controls.Add($accept)

    Add-NavButtons $form {
        $script:acceptedTerms = $accept.Checked
        $form.DialogResult = [System.Windows.Forms.DialogResult]::Retry
        $form.Close()
    } {
        if (-not $accept.Checked) {
            Show-InstallerMessage "Du musst die AGB akzeptieren, um fortzufahren." "NyxNova Installer" Warning
            return
        }
        $script:acceptedTerms = $true
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    } "Installieren"
    return $form.ShowDialog()
}

function Show-LaunchPage {
    $form = New-InstallerForm "Installation abgeschlossen"
    Add-Title $form "NyxNova wurde installiert" "Die Installation ist fertig. Du kannst NyxNova jetzt direkt starten."

    $check = New-Object System.Windows.Forms.CheckBox
    $check.Text = "NyxNova jetzt starten"
    $check.Checked = $script:launchAfterInstall
    $check.AutoSize = $true
    $check.Location = New-Object System.Drawing.Point(264, 154)
    $form.Controls.Add($check)

    $finish = New-Object System.Windows.Forms.Button
    $finish.Text = "Fertig"
    $finish.Size = New-Object System.Drawing.Size(96, 32)
    $finish.Location = New-Object System.Drawing.Point(604, 340)
    $finish.Add_Click({
        $script:launchAfterInstall = $check.Checked
        $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $form.Controls.Add($finish)
    return $form.ShowDialog()
}

function Write-ProgressText {
    param([System.Windows.Forms.TextBox]$Box, [string]$Text)
    $Box.AppendText("$Text`r`n")
    $Box.SelectionStart = $Box.TextLength
    $Box.ScrollToCaret()
    [System.Windows.Forms.Application]::DoEvents()
}

function Set-InstallerProgress {
    param(
        [System.Windows.Forms.ProgressBar]$ProgressBar,
        [System.Windows.Forms.Label]$StatusLabel,
        [int]$Value,
        [string]$Text,
        [bool]$Marquee = $false
    )
    if ($Marquee) {
        $ProgressBar.Style = "Marquee"
        $ProgressBar.MarqueeAnimationSpeed = 25
    } else {
        $ProgressBar.Style = "Continuous"
        $ProgressBar.MarqueeAnimationSpeed = 0
        $ProgressBar.Value = [Math]::Max(0, [Math]::Min(100, $Value))
    }
    $StatusLabel.Text = $Text
    [System.Windows.Forms.Application]::DoEvents()
}

function Invoke-DownloadWithProgress {
    param(
        [string]$Url,
        [string]$OutFile,
        [System.Windows.Forms.ProgressBar]$ProgressBar,
        [System.Windows.Forms.Label]$StatusLabel,
        [System.Windows.Forms.TextBox]$LogBox,
        [int]$StartValue,
        [int]$EndValue,
        [string]$Label = "Download laeuft"
    )

    $client = New-GitHubClient
    $state = [hashtable]::Synchronized(@{ Done = $false; Error = $null; Cancelled = $false; Percent = 0; Received = 0L; Total = 0L })

    $client.add_DownloadProgressChanged({
        param($s, $e)
        $state.Percent = $e.ProgressPercentage
        $state.Received = $e.BytesReceived
        $state.Total = $e.TotalBytesToReceive
    })
    $client.add_DownloadFileCompleted({
        param($s, $e)
        $state.Cancelled = $e.Cancelled
        $state.Error = $e.Error
        $state.Done = $true
    })

    try {
        $client.DownloadFileAsync([Uri]$Url, $OutFile)
        while (-not $state.Done) {
            $range = $EndValue - $StartValue
            $value = $StartValue + [int]([Math]::Round($range * ($state.Percent / 100)))
            $sizeText = if ($state.Total -gt 0) { " ({0:N1}/{1:N1} MB)" -f ($state.Received / 1MB), ($state.Total / 1MB) } else { "" }
            Set-InstallerProgress $ProgressBar $StatusLabel $value ("$Label... {0}%{1}" -f $state.Percent, $sizeText)
            Start-Sleep -Milliseconds 80
            [System.Windows.Forms.Application]::DoEvents()
        }
        if ($state.Cancelled) { throw "Download wurde abgebrochen." }
        if ($state.Error) { throw $state.Error }
        Write-ProgressText $LogBox "Download fertig."
    } finally {
        $client.Dispose()
    }
}

function Invoke-ProcessChecked {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [System.Windows.Forms.TextBox]$LogBox
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    $psi.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " "
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($psi)

    $stdoutJob = [System.Threading.Tasks.Task]::Run([System.Func[string]]{ $process.StandardOutput.ReadToEnd() })
    $stderrJob  = [System.Threading.Tasks.Task]::Run([System.Func[string]]{ $process.StandardError.ReadToEnd() })

    while (-not $process.WaitForExit(250)) {
        [System.Windows.Forms.Application]::DoEvents()
    }

    $stdoutText = $stdoutJob.Result
    $stderrText = $stderrJob.Result

    foreach ($line in ($stdoutText -split "`n")) {
        $t = $line.TrimEnd(); if ($t) { Write-ProgressText $LogBox $t }
    }
    foreach ($line in ($stderrText -split "`n")) {
        $t = $line.TrimEnd(); if ($t) { Write-ProgressText $LogBox "ERR: $t" }
    }

    if ($process.ExitCode -ne 0) {
        $detail = if ($stderrText.Trim()) { $stderrText.Trim() } else { $stdoutText.Trim() }
        throw "dotnet fehlgeschlagen (Exit $($process.ExitCode)):`r`n$detail"
    }
}

function Remove-TempFiles {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Success = $true; Message = "Keine temporaeren Dateien vorhanden." }
    }
    for ($i = 1; $i -le 5; $i++) {
        try {
            [GC]::Collect(); [GC]::WaitForPendingFinalizers()
            Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object {
                try { $_.Attributes = [IO.FileAttributes]::Normal } catch {}
            }
            Remove-Item -LiteralPath $Path -Recurse -Force -Confirm:$false -ErrorAction Stop
        } catch { Start-Sleep -Milliseconds (250 * $i) }
        if (-not (Test-Path -LiteralPath $Path)) {
            return [pscustomobject]@{ Success = $true; Message = "Temporaere Dateien geloescht." }
        }
    }
    # Last resort
    & cmd /c "rmdir /s /q `"$Path`"" 2>$null
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Success = $true; Message = "Temporaere Dateien geloescht (cmd)." }
    }
    return [pscustomobject]@{ Success = $false; Message = "Temp-Dateien konnten nicht geloescht werden: $Path" }
}

function New-Shortcut {
    param([string]$ShortcutPath, [string]$TargetPath, [string]$WorkingDirectory, [string]$IconPath)
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    if (Test-Path -LiteralPath $IconPath) { $shortcut.IconLocation = $IconPath }
    $shortcut.Save()
}

function Install-NyxNova {
    $form = New-InstallerForm "NyxNova wird installiert"
    Add-Title $form "Installation laeuft" "Bitte warte, waehrend NyxNova heruntergeladen, gebaut und installiert wird."

    $status = New-Object System.Windows.Forms.Label
    $status.Text = "Vorbereitung..."
    $status.AutoSize = $false
    $status.Size = New-Object System.Drawing.Size(430, 24)
    $status.Location = New-Object System.Drawing.Point(264, 120)
    $form.Controls.Add($status)

    $bar = New-Object System.Windows.Forms.ProgressBar
    $bar.Minimum = 0; $bar.Maximum = 100; $bar.Value = 0
    $bar.Size = New-Object System.Drawing.Size(430, 22)
    $bar.Location = New-Object System.Drawing.Point(264, 150)
    $form.Controls.Add($bar)

    $log = New-Object System.Windows.Forms.TextBox
    $log.Multiline = $true; $log.ReadOnly = $true; $log.ScrollBars = "Vertical"
    $log.Size = New-Object System.Drawing.Size(430, 118)
    $log.Location = New-Object System.Drawing.Point(264, 190)
    $form.Controls.Add($log)

    $form.Add_Shown({
        $tempRoot = $null
        try {
            # Pruefe dotnet
            $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
            if ($null -eq $dotnet) {
                throw ".NET SDK nicht gefunden. Bitte installiere das .NET 8 SDK von https://aka.ms/dotnet/download"
            }

            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("NyxNovaInstaller-" + [Guid]::NewGuid().ToString("N"))
            if (-not (Test-Path -LiteralPath $tempRoot)) { [System.IO.Directory]::CreateDirectory($tempRoot) | Out-Null }
            Write-ProgressText $log "Temp: $tempRoot"

            # Alte Temp-Ordner aufraumen
            $tempBase = [IO.Path]::GetTempPath()
            Get-ChildItem -LiteralPath $tempBase -Directory -Filter "NyxNovaInstaller-*" -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -ne $tempRoot } | ForEach-Object { Remove-TempFiles $_.FullName | Out-Null }

            # Repo als ZIP herunterladen (kein API Rate Limit)
            Set-InstallerProgress $bar $status 10 "NyxNova von GitHub herunterladen..."
            $zipUrl = "https://github.com/$Repository/archive/refs/heads/$Branch.zip"
            $zipPath = Join-Path $tempRoot "source.zip"
            Write-ProgressText $log "Download: $zipUrl"
            Invoke-DownloadWithProgress $zipUrl $zipPath $bar $status $log 10 35 "Quellcode herunterladen"

            # ZIP entpacken
            Set-InstallerProgress $bar $status 35 "Quellcode entpacken..."
            Write-ProgressText $log "Entpacke ZIP..."
            $extractPath = Join-Path $tempRoot "source"
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $extractPath)

            # Projektdatei finden (GitHub ZIP enthaelt Ordner "RepoName-Branch")
            $repoName = ($Repository -split "/")[-1]
            $sourceRoot = Join-Path $extractPath "$repoName-$Branch"
            $projectFile = Join-Path $sourceRoot "src\NovaBrowser.CefSharp\NovaBrowser.CefSharp.csproj"

            if (-not (Test-Path -LiteralPath $projectFile)) {
                # Fallback: suche .csproj
                $found = Get-ChildItem -LiteralPath $extractPath -Recurse -Filter "NovaBrowser.CefSharp.csproj" -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($found) {
                    $projectFile = $found.FullName
                    Write-ProgressText $log "Projektdatei gefunden: $projectFile"
                } else {
                    throw "Projektdatei nicht gefunden. Struktur:`r`n$(Get-ChildItem $extractPath -Recurse -Depth 3 | Select-Object -First 20 | ForEach-Object { $_.FullName } | Out-String)"
                }
            } else {
                Write-ProgressText $log "Projektdatei: $projectFile"
            }

            # Alte Installation entfernen wenn Clean-Install
            if ($script:cleanInstall -and (Test-Path -LiteralPath $script:installPath)) {
                Set-InstallerProgress $bar $status 38 "Alte Installation entfernen..."
                Write-ProgressText $log "Entferne: $script:installPath"
                & cmd /c "rmdir /s /q `"$script:installPath`"" 2>$null
                Remove-ShortcutIfExists (Join-Path ([Environment]::GetFolderPath("Desktop")) "NyxNova.lnk")
                $startMenuDir2 = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NyxNova"
                & cmd /c "rmdir /s /q `"$startMenuDir2`"" 2>$null
            }

            # Bauen
            $publishPath = Join-Path $tempRoot "publish"
            Set-InstallerProgress $bar $status 45 "NyxNova bauen..." $true
            Write-ProgressText $log "Starte dotnet publish..."
            Invoke-ProcessChecked "dotnet" @(
                "publish", $projectFile,
                "-c", "Release",
                "-o", $publishPath,
                "-v", "minimal",
                "-p:DebugType=None",
                "-p:DebugSymbols=false"
            ) $log

            # Pruefen ob EXE da ist
            $builtExe = Join-Path $publishPath "NovaBrowser.CefSharp.exe"
            if (-not (Test-Path -LiteralPath $builtExe)) {
                $exes = Get-ChildItem -LiteralPath $publishPath -Recurse -Filter "*.exe" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
                throw "EXE nach Build nicht gefunden. Gefundene EXEs: $($exes -join ', ')"
            }
            Write-ProgressText $log "Build erfolgreich: $builtExe"

            # Installieren
            Set-InstallerProgress $bar $status 72 "Programmdateien installieren..."
            # Laufende NyxNova-Prozesse beenden bevor Dateien geloescht werden
            $runningProc = Get-Process -Name "NovaBrowser.CefSharp" -ErrorAction SilentlyContinue
            if ($runningProc) {
                Write-ProgressText $log "Beende laufende NyxNova-Instanz..."
                $runningProc | Stop-Process -Force -ErrorAction SilentlyContinue
                Start-Sleep -Milliseconds 1500
            }

            # Zielordner komplett neu anlegen um alte/gesperrte Dateien zu vermeiden
            if (Test-Path -LiteralPath $script:installPath) {
                Write-ProgressText $log "Loesche alten Installationsordner..."
                & cmd /c "rmdir /s /q `"$script:installPath`"" 2>$null
                Start-Sleep -Milliseconds 800
            }
            [System.IO.Directory]::CreateDirectory($script:installPath) | Out-Null
            Write-ProgressText $log "Kopiere nach: $script:installPath"

            # Reines .NET kopieren - keine PowerShell-Prompts moeglich
            function Copy-DirectoryNet {
                param([string]$Source, [string]$Dest)
                [System.IO.Directory]::CreateDirectory($Dest) | Out-Null
                foreach ($file in [System.IO.Directory]::GetFiles($Source)) {
                    $destFile = [System.IO.Path]::Combine($Dest, [System.IO.Path]::GetFileName($file))
                    [System.IO.File]::Copy($file, $destFile, $true)
                }
                foreach ($dir in [System.IO.Directory]::GetDirectories($Source)) {
                    $destDir = [System.IO.Path]::Combine($Dest, [System.IO.Path]::GetFileName($dir))
                    Copy-DirectoryNet $dir $destDir
                }
            }
            Copy-DirectoryNet $publishPath $script:installPath

            # PDB loeschen
            foreach ($pdb in [System.IO.Directory]::GetFiles($script:installPath, "*.pdb", [System.IO.SearchOption]::AllDirectories)) {
                [System.IO.File]::Delete($pdb)
            }

            $exePath = Join-Path $script:installPath "NovaBrowser.CefSharp.exe"
            if (-not (Test-Path -LiteralPath $exePath)) {
                throw "EXE nach dem Kopieren nicht gefunden: $exePath"
            }

            # Verknuepfungen
            Set-InstallerProgress $bar $status 86 "Verknuepfungen erstellen..."
            $iconPath = Join-Path $script:installPath "Assets\nyxnova-icon.ico"
            $startMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\NyxNova"
            if (-not (Test-Path -LiteralPath $startMenuDir)) { [System.IO.Directory]::CreateDirectory($startMenuDir) | Out-Null }
            New-Shortcut (Join-Path $startMenuDir "NyxNova.lnk") $exePath $script:installPath $iconPath
            if ($script:desktopShortcut) {
                New-Shortcut (Join-Path ([Environment]::GetFolderPath("Desktop")) "NyxNova.lnk") $exePath $script:installPath $iconPath
            }

            # Aufraumen
            Set-InstallerProgress $bar $status 94 "Temporaere Dateien bereinigen..."
            $cleanup = Remove-TempFiles $tempRoot
            Write-ProgressText $log $cleanup.Message

            Save-InstallState $script:installPath
            Set-InstallerProgress $bar $status 100 "Installation abgeschlossen."
            Write-ProgressText $log "Fertig! Installiert in: $script:installPath"
            Start-Sleep -Milliseconds 500
            $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
            $form.Close()
        }
        catch {
            $errorText = $_.Exception.Message
            Set-InstallerProgress $bar $status 0 "Installation fehlgeschlagen."
            Write-ProgressText $log "FEHLER: $errorText"
            Write-ProgressText $log "Bereinige Temp-Dateien..."
            $cleanup = Remove-TempFiles $tempRoot
            Write-ProgressText $log $cleanup.Message

            $cleanupText = if ($cleanup.Success) { "Temp-Dateien wurden geloescht." } else { "Achtung: $($cleanup.Message)" }

            $retry = [System.Windows.Forms.MessageBox]::Show(
                "Installation fehlgeschlagen:`r`n`r`n$errorText`r`n`r`n$cleanupText`r`n`r`nNochmal versuchen?",
                "Installation fehlgeschlagen",
                [System.Windows.Forms.MessageBoxButtons]::YesNo,
                [System.Windows.Forms.MessageBoxIcon]::Error
            )

            $form.DialogResult = if ($retry -eq [System.Windows.Forms.DialogResult]::Yes) {
                [System.Windows.Forms.DialogResult]::Retry
            } else {
                [System.Windows.Forms.DialogResult]::Cancel
            }
            $form.Close()
        }
    })

    return $form.ShowDialog()
}

# Hauptprogramm
$installed = Get-InstalledNyxNova
if ($installed) {
    $maintenanceResult = Show-MaintenancePage $installed
    if ($maintenanceResult -eq [System.Windows.Forms.DialogResult]::Cancel -or
        $maintenanceResult -eq [System.Windows.Forms.DialogResult]::No) {
        exit 0
    }
}

$step = 0
while ($true) {
    $result = switch ($step) {
        0 { Show-ShortcutPage }
        1 { Show-InstallPathPage }
        2 { Show-TermsPage }
        default { break }
    }
    if ($result -eq [System.Windows.Forms.DialogResult]::Cancel) { exit 0 }
    if ($result -eq [System.Windows.Forms.DialogResult]::Retry) { $step-- } else { $step++ }
    if ($step -gt 2) { break }
}

$installResult = [System.Windows.Forms.DialogResult]::Retry
while ($installResult -eq [System.Windows.Forms.DialogResult]::Retry) {
    $installResult = Install-NyxNova
}

if ($installResult -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }

if ((Show-LaunchPage) -eq [System.Windows.Forms.DialogResult]::OK -and $script:launchAfterInstall) {
    Start-Process -FilePath (Join-Path $script:installPath "NovaBrowser.CefSharp.exe") -WorkingDirectory $script:installPath
}