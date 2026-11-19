# Opens STEP, .ipt or .iam files in the AutoDraw window of the running Inventor (a STEP is imported to an .ipt/.iam
# next to it, or the existing one of that name is used). Also an example of calling AutoDraw from another program.
# Usage:  powershell -ExecutionPolicy Bypass -File tools\open-in-autodraw.ps1 C:\x\a.stp C:\x\b.stp
#         add -Options "order=160261777; Tube=off; General=off" to fill in fields and switch ticks
param([string]$Options = '', [Parameter(ValueFromRemainingArguments = $true)][string[]]$Files)

$ErrorActionPreference = 'Stop'
if (-not $Files -or $Files.Count -eq 0) { throw 'Give one or more STEP, .ipt or .iam files.' }
$paths = $Files | ForEach-Object { (Resolve-Path $_).Path }

try { $inventor = [Runtime.InteropServices.Marshal]::GetActiveObject('Inventor.Application') }
catch { throw 'Start Inventor first.' }

$id = '{0091494E-410A-4CC7-BFFC-1248D26C64BD}'   # AutoDraw 1.2.0 or newer
$addin = $inventor.ApplicationAddIns.ItemById($id)
if (-not $addin.Activated) { $addin.Activate() }
$auto = $addin.Automation
# late bound by name (IDispatch), as Python's win32com does: the .NET 8 add-in has no registered type library
$result = [System.__ComObject].InvokeMember('OpenWith', [Reflection.BindingFlags]::InvokeMethod, $null, $auto, @(($paths -join ';'), $Options))
if ($result) { Write-Warning $result } else { "Opened in AutoDraw: $($paths.Count) file(s)" }
