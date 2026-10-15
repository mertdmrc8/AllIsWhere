$ErrorActionPreference = 'Stop'

$dotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetPath)) {
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($dotnetCommand) { $dotnetPath = $dotnetCommand.Source }
}
$desktopRuntime = if (Test-Path -LiteralPath $dotnetPath) {
    & $dotnetPath --list-runtimes 2>$null | Where-Object { $_ -match '^Microsoft\.WindowsDesktop\.App 10\.' }
}
if (-not $desktopRuntime) {
    Write-Host '.NET 10 Desktop Runtime bulunamadı. Kurulum için Microsoft indirme sayfası açılıyor.'
    Start-Process 'https://dotnet.microsoft.com/en-us/download/dotnet/10.0'
    Write-Host 'Windows Desktop Runtime 10.x (x64) kurduktan sonra setup dosyasını yeniden çalıştırın.'
    Read-Host 'Devam etmek için Enter'
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
$defaultInstallDir = Join-Path $env:LOCALAPPDATA 'Programs\AllIsWhere'
$startWithWindowsDefault = $true
$settingsFile = Join-Path $env:LOCALAPPDATA 'EverythingLite\settings.json'
if (Test-Path -LiteralPath $settingsFile) {
    try {
        $savedSettings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        if ($null -ne $savedSettings.StartWithWindows) {
            $startWithWindowsDefault = [bool]$savedSettings.StartWithWindows
        }
    }
    catch { }
}
$form = New-Object System.Windows.Forms.Form
$form.Text = 'AllIsWhere Kurulumu'
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ClientSize = New-Object System.Drawing.Size(620, 300)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)

$heading = New-Object System.Windows.Forms.Label
$heading.Text = 'AllIsWhere kurulumu'
$heading.Font = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Bold)
$heading.Location = New-Object System.Drawing.Point(20, 18)
$heading.Size = New-Object System.Drawing.Size(560, 32)
$form.Controls.Add($heading)

$pathLabel = New-Object System.Windows.Forms.Label
$pathLabel.Text = 'Kurulum klasörü:'
$pathLabel.Location = New-Object System.Drawing.Point(22, 68)
$pathLabel.Size = New-Object System.Drawing.Size(150, 22)
$form.Controls.Add($pathLabel)

$pathBox = New-Object System.Windows.Forms.TextBox
$pathBox.Text = $defaultInstallDir
$pathBox.Location = New-Object System.Drawing.Point(22, 92)
$pathBox.Size = New-Object System.Drawing.Size(465, 26)
$form.Controls.Add($pathBox)

$browseButton = New-Object System.Windows.Forms.Button
$browseButton.Text = 'Gözat…'
$browseButton.Location = New-Object System.Drawing.Point(497, 90)
$browseButton.Size = New-Object System.Drawing.Size(100, 30)
$browseButton.Add_Click({
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    $picker.Description = 'AllIsWhere uygulamasının kurulacağı klasörü seçin.'
    $picker.ShowNewFolderButton = $true
    $currentParent = Split-Path -Parent $pathBox.Text
    if (Test-Path -LiteralPath $currentParent) { $picker.SelectedPath = $currentParent }
    if ($picker.ShowDialog($form) -eq [System.Windows.Forms.DialogResult]::OK) {
        $pathBox.Text = $picker.SelectedPath
    }
    $picker.Dispose()
})
$form.Controls.Add($browseButton)

$desktopBox = New-Object System.Windows.Forms.CheckBox
$desktopBox.Text = 'Masaüstüne kısayol oluştur'
$desktopBox.Checked = $true
$desktopBox.Location = New-Object System.Drawing.Point(24, 140)
$desktopBox.Size = New-Object System.Drawing.Size(260, 24)
$form.Controls.Add($desktopBox)

$startMenuBox = New-Object System.Windows.Forms.CheckBox
$startMenuBox.Text = 'Başlat menüsüne kısayol oluştur'
$startMenuBox.Checked = $true
$startMenuBox.Location = New-Object System.Drawing.Point(300, 140)
$startMenuBox.Size = New-Object System.Drawing.Size(290, 24)
$form.Controls.Add($startMenuBox)

$startupBox = New-Object System.Windows.Forms.CheckBox
$startupBox.Text = 'Windows oturum açılışında başlat'
$startupBox.Checked = $startWithWindowsDefault
$startupBox.Location = New-Object System.Drawing.Point(24, 174)
$startupBox.Size = New-Object System.Drawing.Size(300, 24)
$form.Controls.Add($startupBox)

$installButton = New-Object System.Windows.Forms.Button
$installButton.Text = 'Kur'
$installButton.Location = New-Object System.Drawing.Point(390, 238)
$installButton.Size = New-Object System.Drawing.Size(96, 36)
$installButton.Add_Click({
    if ([string]::IsNullOrWhiteSpace($pathBox.Text)) {
        [System.Windows.Forms.MessageBox]::Show('Kurulum klasörünü belirtin.', 'AllIsWhere',
            [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        return
    }
    try {
        $fullPath = [System.IO.Path]::GetFullPath($pathBox.Text.Trim()).TrimEnd('\')
        $driveRoot = [System.IO.Path]::GetPathRoot($fullPath).TrimEnd('\')
        if ($fullPath.Equals($driveRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Sürücü köküne doğrudan kurulum yapılamaz; bir klasör seçin.'
        }
        $form.Tag = $fullPath
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show('Kurulum klasörü geçerli değil.', 'AllIsWhere',
            [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
        return
    }
    $form.DialogResult = [System.Windows.Forms.DialogResult]::OK
    $form.Close()
})
$form.Controls.Add($installButton)

$cancelButton = New-Object System.Windows.Forms.Button
$cancelButton.Text = 'İptal'
$cancelButton.Location = New-Object System.Drawing.Point(500, 238)
$cancelButton.Size = New-Object System.Drawing.Size(96, 36)
$cancelButton.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($cancelButton)
$adminNotice = New-Object System.Windows.Forms.Label
$adminNotice.Text = 'Uygulama açılırken Windows yönetici izni ister.'
$adminNotice.Location = New-Object System.Drawing.Point(24, 214)
$adminNotice.Size = New-Object System.Drawing.Size(350, 22)
$form.Controls.Add($adminNotice)
$form.AcceptButton = $installButton
$form.CancelButton = $cancelButton

if ($form.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
    $form.Dispose()
    Write-Host 'Kurulum iptal edildi.'
    exit 1
}
$installDir = [string]$form.Tag
$createDesktopShortcut = $desktopBox.Checked
$createStartMenuShortcut = $startMenuBox.Checked
$startWithWindows = $startupBox.Checked
$form.Dispose()
try { New-Item -ItemType Directory -Force -Path $installDir | Out-Null }
catch {
    [System.Windows.Forms.MessageBox]::Show("Bu klasöre kurulum yapılamadı:`n$installDir`n`n$($_.Exception.Message)",
        'AllIsWhere', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
try {
    $writeCheck = Join-Path $installDir '.all-is-where-write-check'
    [System.IO.File]::WriteAllText($writeCheck, '')
    Remove-Item -LiteralPath $writeCheck -Force
}
catch {
    [System.Windows.Forms.MessageBox]::Show("Bu klasöre yazma izni yok:`n$installDir`n`nBaşka bir klasör seçin.",
        'AllIsWhere', [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
foreach ($name in @('AllIsWhere.exe', 'AllIsWhere.dll', 'AllIsWhere.deps.json',
                    'AllIsWhere.runtimeconfig.json', 'uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $installDir -Force
}

$shell = New-Object -ComObject WScript.Shell
$appPath = Join-Path $installDir 'AllIsWhere.exe'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AllIsWhere.lnk'
if ($createStartMenuShortcut) {
    $startMenu = Split-Path -Parent $startMenuShortcut
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    $shortcut = $shell.CreateShortcut($startMenuShortcut)
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$appPath,0"
    $shortcut.Description = 'AllIsWhere dosya ve klasör arama'
    $shortcut.Save()
}
else { Remove-Item -LiteralPath $startMenuShortcut -Force -ErrorAction SilentlyContinue }
Remove-Item -LiteralPath (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Everything Lite.lnk') `
    -Force -ErrorAction SilentlyContinue

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$desktopShortcut = Join-Path $desktop 'AllIsWhere.lnk'
if ($createDesktopShortcut) {
    $shortcut = $shell.CreateShortcut($desktopShortcut)
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $installDir
    $shortcut.IconLocation = "$appPath,0"
    $shortcut.Description = 'AllIsWhere dosya ve klasör arama'
    $shortcut.Save()
}
else { Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue }

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AllIsWhere'
New-Item -Path $uninstallKey -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayName -Value 'AllIsWhere' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value '1.0.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name Publisher -Value 'AllIsWhere' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installDir -PropertyType String -Force | Out-Null
$uninstallCommand = "powershell.exe -STA -NoProfile -File `"$(Join-Path $installDir 'uninstall.ps1')`""
New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\EverythingLite' `
    -Recurse -Force -ErrorAction SilentlyContinue

# Register startup only when selected in the installer.
$settingsFile = Join-Path $env:LOCALAPPDATA 'EverythingLite\settings.json'
try {
    if (Test-Path -LiteralPath $settingsFile) {
        $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json
        $settings | Add-Member -NotePropertyName StartWithWindows -NotePropertyValue $startWithWindows -Force
    }
    else { $settings = [pscustomobject]@{ StartWithWindows = $startWithWindows } }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsFile) | Out-Null
    $settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $settingsFile -Encoding UTF8
}
catch { }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
if ($startWithWindows) {
    Set-ItemProperty -Path $runKey -Name 'AllIsWhere' -Value "`"$(Join-Path $installDir 'AllIsWhere.exe')`"" -Type String
}
else {
    Remove-ItemProperty -Path $runKey -Name 'AllIsWhere' -ErrorAction SilentlyContinue
}
Remove-ItemProperty -Path $runKey -Name 'EverythingLite' -ErrorAction SilentlyContinue

Write-Host "AllIsWhere kuruldu: $installDir"
[System.Windows.Forms.MessageBox]::Show("AllIsWhere kuruldu.`n`nKurulum konumu:`n$installDir`n`nMasaüstü kısayolu: $(if ($createDesktopShortcut) { 'Evet' } else { 'Hayır' })`nBaşlat menüsü kısayolu: $(if ($createStartMenuShortcut) { 'Evet' } else { 'Hayır' })",
    'AllIsWhere kurulumu tamamlandı', [System.Windows.Forms.MessageBoxButtons]::OK,
    [System.Windows.Forms.MessageBoxIcon]::Information) | Out-Null
