# Opens STEP, .ipt or .iam files in the AutoDraw window of the running Inventor (a STEP is imported to an .ipt/.iam
# next to it, or the existing one of that name is used). Also an example of calling AutoDraw from another program.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\open-in-autodraw.ps1 C:\x\a.stp C:\x\b.stp
#         add -Dev to use the "AutoDraw dev" loader (the latest build) instead of the installed AutoDraw
param([switch]$Dev, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)

$ErrorActionPreference = 'Stop'
if (-not $Files -or $Files.Count -eq 0) { throw 'Give one or more STEP, .ipt or .iam files.' }
$paths = $Files | ForEach-Object { (Resolve-Path $_).Path }

try { $inventor = [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application') }
catch { throw 'Start Inventor first.' }

$id = if ($Dev) { '{5D2B8F0E-7C41-4E8A-9B6A-2F3C1D9E7A10}' } else { '{0091494E-410A-4CC7-BFFC-1248D26C64BD}' }
$addin = $inventor.ApplicationAddIns.ItemById($id)
if (-not $addin.Activated) { $addin.Activate() }
$result = $addin.Automation.Open(($paths -join ';'))
if ($result) { Write-Warning $result } else { "Opened in AutoDraw: $($paths.Count) file(s)" }
