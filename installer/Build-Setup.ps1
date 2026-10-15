$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$outputDir = Join-Path $workspaceRoot 'outputs'
$releaseDir = Join-Path $projectRoot 'bin\Release\net10.0-windows'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$setupSource = Join-Path $PSScriptRoot 'SetupProgram.cs'
$iconPath = Join-Path $projectRoot 'Assets\AllIsWhere.ico'
$setupPath = Join-Path $outputDir 'AllIsWhere-Setup-Update8.exe'

if (-not (Test-Path -LiteralPath $csc)) { throw 'Windows .NET Framework C# derleyicisi bulunamadı.' }
if (-not (Test-Path -LiteralPath (Join-Path $releaseDir 'AllIsWhere.exe'))) {
    throw 'Release çıktısı bulunamadı. Önce uygulamayı Release modunda derleyin.'
}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Build the installer UI with the .NET Framework compiler included with Windows.
# This keeps the setup UI independent of PowerShell script execution policy.
$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', "/out:$setupPath", "/win32icon:$iconPath",
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Web.Extensions.dll',
    "/resource:$(Join-Path $releaseDir 'AllIsWhere.exe'),AllIsWhere.exe",
    "/resource:$(Join-Path $releaseDir 'AllIsWhere.dll'),AllIsWhere.dll",
    "/resource:$(Join-Path $releaseDir 'AllIsWhere.deps.json'),AllIsWhere.deps.json",
    "/resource:$(Join-Path $releaseDir 'AllIsWhere.runtimeconfig.json'),AllIsWhere.runtimeconfig.json",
    $setupSource
)
& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw "Kurulum derlemesi başarısız oldu (csc exit code $LASTEXITCODE)." }
Write-Output "Kurulum dosyası oluşturuldu: $setupPath"
