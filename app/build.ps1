# Build DFM80 Web Driver (desktop exe) with csc.
#   powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $dir

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

$refs = @(
  "System.dll",
  "System.Core.dll",
  "System.Drawing.dll",
  "System.Windows.Forms.dll",
  "System.Web.Extensions.dll",
  "$dir\Microsoft.Web.WebView2.Core.dll",
  "$dir\Microsoft.Web.WebView2.WinForms.dll"
)
$refArgs = $refs | ForEach-Object { "/r:$_" }

# Embed everything -> single-file exe
$resArgs = @(
  "/resource:$dir\index.html,index.html",
  "/resource:$dir\icon.ico,icon.ico",
  "/resource:$dir\WebView2Loader.dll,WebView2Loader.dll",
  "/resource:$dir\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll",
  "/resource:$dir\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll"
)

$argList = @(
  "/target:winexe",
  "/out:$dir\DFM80-Driver.exe",
  "/win32icon:$dir\icon.ico",
  "/win32manifest:$dir\app.manifest",
  "/platform:x64",
  "/codepage:65001",
  "/optimize+",
  "/nologo"
) + $refArgs + $resArgs + @("$dir\Program.cs")

Write-Host "Compiling DFM80 Web Driver (desktop, single-exe)..." -ForegroundColor Cyan
& $csc $argList
if ($LASTEXITCODE -ne 0) { Write-Host "BUILD FAILED" -ForegroundColor Red; exit 1 }
Write-Host "OK -> DFM80-Driver.exe" -ForegroundColor Green
