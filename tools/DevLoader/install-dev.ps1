# Temporary: installs the "AutoDraw dev" button, which runs the latest build of AutoDraw without restarting Inventor.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\DevLoader\install-dev.ps1          (install; close Inventor once)
#         powershell -ExecutionPolicy Bypass -File tools\DevLoader\install-dev.ps1 -Remove  (remove it again)
# After installing: dotnet build AutoDraw.csproj -c Release, then click "AutoDraw dev" in Inventor.
param([switch]$Remove)

$ErrorActionPreference = 'Stop'
$here   = $PSScriptRoot
$repo   = Resolve-Path (Join-Path $here '..\..')
$target = Join-Path $env:LOCALAPPDATA 'AutoDrawDev\loader'
$addins = Join-Path $env:APPDATA 'Autodesk\Inventor 2025\Addins'
$addin  = Join-Path $addins 'AutoDrawDev.addin'

if ($Remove) {
    if (Test-Path $addin) { Remove-Item $addin -Force }
    foreach ($d in $target, (Join-Path $env:TEMP 'AutoDrawDev')) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } }
    "Removed the AutoDraw dev loader (restart Inventor)."
    return
}

dotnet build (Join-Path $here 'DevLoader.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Force $target, $addins | Out-Null
$out = Join-Path $here 'bin\Release\net8.0-windows'
foreach ($f in 'AutoDrawDevLoader.dll', 'AutoDrawDevLoader.deps.json') { Copy-Item (Join-Path $out $f) $target -Force }
Set-Content -Path (Join-Path $target 'build.txt') -Value (Join-Path $repo 'bin\Release\net8.0-windows') -Encoding utf8

@"
<Addin Type="Standard">
  <ClassId>{5d2b8f0e-7c41-4e8a-9b6a-2f3c1d9e7a10}</ClassId>
  <ClientId>{5d2b8f0e-7c41-4e8a-9b6a-2f3c1d9e7a10}</ClientId>
  <DisplayName>AutoDraw dev loader</DisplayName>
  <Description>Temporary: runs the latest AutoDraw build without restarting Inventor.</Description>
  <Assembly>$(Join-Path $target 'AutoDrawDevLoader.dll')</Assembly>
  <LoadOnStartUp>1</LoadOnStartUp>
  <UserUnloadable>1</UserUnloadable>
  <Hidden>0</Hidden>
  <SupportedSoftwareVersionGreaterThan>28..</SupportedSoftwareVersionGreaterThan>
  <SupportedSoftwareVersionLessThan>30..</SupportedSoftwareVersionLessThan>
  <DataVersion>1</DataVersion>
  <UserInterfaceVersion>1</UserInterfaceVersion>
  <LoadBehavior>1</LoadBehavior>
</Addin>
"@ | Set-Content -Path $addin -Encoding utf8
"Installed $addin"
"Start Inventor; the 'AutoDraw dev' button sits next to AutoDraw. Rebuild AutoDraw, click it again: the new build runs."
