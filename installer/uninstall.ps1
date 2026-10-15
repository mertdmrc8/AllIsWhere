$ErrorActionPreference = 'Stop'

$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AllIsWhere'
$registeredLocation = (Get-ItemProperty -LiteralPath $uninstallKey -Name InstallLocation -ErrorAction Stop).InstallLocation
$expected = [System.IO.Path]::GetFullPath($registeredLocation).TrimEnd('\')
$actual = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
if (-not $actual.Equals($expected, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Beklenmeyen kurulum konumu: $actual"
}
$installDir = $actual

$startShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\AllIsWhere.lnk'
$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
$desktopShortcut = Join-Path $desktop 'AllIsWhere.lnk'
Remove-Item -LiteralPath $startShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $desktopShortcut -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'AllIsWhere' -ErrorAction SilentlyContinue
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
    -Name 'EverythingLite' -ErrorAction SilentlyContinue
foreach ($name in @('AllIsWhere.exe', 'AllIsWhere.dll', 'AllIsWhere.deps.json',
                    'AllIsWhere.runtimeconfig.json', 'uninstall.ps1')) {
    Remove-Item -LiteralPath (Join-Path $installDir $name) -Force -ErrorAction SilentlyContinue
}
if (-not (Get-ChildItem -LiteralPath $installDir -Force -ErrorAction SilentlyContinue)) {
    Remove-Item -LiteralPath $installDir -Force -ErrorAction SilentlyContinue
}
Write-Host 'AllIsWhere kaldırıldı. Kullanıcı ayarları korunmuştur.'
Read-Host 'Devam etmek için Enter'
