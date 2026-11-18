# Temporary: installs the "AutoDraw dev" button, which runs the latest build of AutoDraw without restarting Inventor.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\DevLoader\install-dev.ps1          (install; restart Inventor once)
#         powershell -ExecutionPolicy Bypass -File tools\DevLoader\install-dev.ps1 -Remove  (remove it again)
# After installing: dotnet build AutoDraw.csproj -c Release, then click "AutoDraw dev" in Inventor.
# Installed as a bundle in ApplicationPlugins, like AutoDraw itself (Inventor does not load it from the Addins folder).
param([switch]$Remove)

$ErrorActionPreference = 'Stop'
$here   = $PSScriptRoot
$repo   = Resolve-Path (Join-Path $here '..\..')
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AutoDrawDev.bundle'
$target = Join-Path $bundle 'Contents\2025'
$old    = Join-Path $env:APPDATA 'Autodesk\Inventor 2025\Addins\AutoDrawDev.addin'   # first attempt, not loaded by Inventor
$utf8   = New-Object System.Text.UTF8Encoding($false)

if (Test-Path $old) { Remove-Item $old -Force }
foreach ($d in (Join-Path $env:LOCALAPPDATA 'AutoDrawDev\loader')) { if (Test-Path $d) { Remove-Item $d -Recurse -Force -ErrorAction SilentlyContinue } }

if ($Remove) {
    if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
    $tmp = Join-Path $env:TEMP 'AutoDrawDev'
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    "Removed the AutoDraw dev loader (restart Inventor)."
    return
}

dotnet build (Join-Path $here 'DevLoader.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
New-Item -ItemType Directory -Force $target | Out-Null
$out = Join-Path $here 'bin\Release\net8.0-windows'
foreach ($f in 'AutoDrawDevLoader.dll', 'AutoDrawDevLoader.deps.json') { Copy-Item (Join-Path $out $f) $target -Force }
[IO.File]::WriteAllText((Join-Path $target 'build.txt'), (Join-Path $repo 'bin\Release\net8.0-windows'), $utf8)

[IO.File]::WriteAllText((Join-Path $target 'AutoDrawDev.Inventor.addin'), @"
<Addin Type="Standard">
  <ClassId>{5d2b8f0e-7c41-4e8a-9b6a-2f3c1d9e7a10}</ClassId>
  <ClientId>{5d2b8f0e-7c41-4e8a-9b6a-2f3c1d9e7a10}</ClientId>
  <DisplayName>AutoDraw dev loader</DisplayName>
  <Description>Temporary: runs the latest AutoDraw build without restarting Inventor.</Description>
  <Assembly>AutoDrawDevLoader.dll</Assembly>
  <LoadOnStartUp>1</LoadOnStartUp>
  <UserUnloadable>1</UserUnloadable>
  <Hidden>0</Hidden>
  <SupportedSoftwareVersionGreaterThan>28..</SupportedSoftwareVersionGreaterThan>
  <SupportedSoftwareVersionLessThan>30..</SupportedSoftwareVersionLessThan>
  <DataVersion>1</DataVersion>
  <UserInterfaceVersion>1</UserInterfaceVersion>
  <LoadBehavior>1</LoadBehavior>
</Addin>
"@, $utf8)

[IO.File]::WriteAllText((Join-Path $bundle 'PackageContents.xml'), @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Inventor" Name="AutoDraw dev loader" Description="Temporary development loader for AutoDraw" AppVersion="0.1.0" FriendlyVersion="0.1.0" ProductType="Application" SupportedLocales="Enu" AppNameSpace="local.autodrawdev" Author="Local" ProductCode="{3f6c2a1e-8b4d-4c7a-9e21-6a5b0d4c8f31}" UpgradeCode="{9a1e4c7b-2d5f-4b8a-a3c6-1e7f0b2d5c94}">
  <CompanyDetails Name="Local" />
  <RuntimeRequirements />
  <Components Description="AutoDraw dev loader for Inventor 2025">
    <RuntimeRequirements OS="Win64" Platform="Inventor" SeriesMin="Ir29" SeriesMax="Ir29" />
    <ComponentEntry AppName="AutoDrawDev" Version="0.1.0" ModuleName="./Contents/2025/AutoDrawDev.Inventor.addin" AppDescription="AutoDraw dev loader" />
  </Components>
</ApplicationPackage>
"@, $utf8)
"Installed $bundle"
"Restart Inventor; the 'AutoDraw dev' button sits next to AutoDraw. Rebuild AutoDraw, click it again: the new build runs."
