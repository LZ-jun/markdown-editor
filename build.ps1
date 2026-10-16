# Build script for the Markdown desktop editor (.NET Framework 4.8.1 + WinForms + WebView2)
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
# NOTE: kept ASCII-only on purpose (Windows PowerShell 5.1 reads .ps1 as ANSI otherwise).
$ErrorActionPreference = "Stop"

$root  = Split-Path -Parent $MyInvocation.MyCommand.Path
$src   = Join-Path $root "src"
$build = Join-Path $root "build"
$dist  = Join-Path $root "dist"
$wv2   = Join-Path $build "wv2"

$wv2version = "1.0.4258.31"
$cscExe = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

$outName = "MarkdownEditor.exe"

if (-not (Test-Path $cscExe)) { throw "csc.exe not found: $cscExe" }
New-Item -ItemType Directory -Force -Path $build, $dist | Out-Null

# ---------- 1. Fetch the WebView2 SDK ----------
$coreDll = Join-Path $wv2 "lib\net462\Microsoft.Web.WebView2.Core.dll"
$wfDll   = Join-Path $wv2 "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
$loader  = Join-Path $wv2 "runtimes\win-x64\native\WebView2Loader.dll"

$needNet = $false
if (-not (Test-Path $coreDll) -or -not (Test-Path $wfDll) -or -not (Test-Path $loader)) { $needNet = $true }
if ($needNet) {
    Write-Host "==> Downloading WebView2 SDK $wv2version ..."
    $nupkg = Join-Path $build "webview2.nupkg"
    $zip   = Join-Path $build "webview2.zip"
    if (-not (Test-Path $nupkg)) {
        Invoke-WebRequest -UseBasicParsing -OutFile $nupkg `
            -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$wv2version/microsoft.web.webview2.$wv2version.nupkg"
    }
    Copy-Item $nupkg $zip -Force
    if (Test-Path $wv2) { Remove-Item -Recurse -Force $wv2 }
    Write-Host "==> Extracting ..."
    Expand-Archive -Path $zip -DestinationPath $wv2 -Force
}

# ---------- 2. Compile ----------
$out    = Join-Path $dist $outName
$html   = Join-Path $src  "ui\index.html"
$hostCs = Join-Path $src  "Host.cs"

if (-not (Test-Path $html))   { throw "missing $html" }
if (-not (Test-Path $hostCs)) { throw "missing $hostCs" }

Write-Host "==> Compiling $out ..."
& $cscExe /nologo /noconfig /target:winexe /platform:x64 /optimize+ /langversion:5 `
    /nowarn:1701,1702,162 `
    /out:$out `
    /reference:"System.dll" `
    /reference:"System.Core.dll" `
    /reference:"System.Drawing.dll" `
    /reference:"System.Windows.Forms.dll" `
    /reference:"$coreDll" `
    /reference:"$wfDll" `
    /resource:"$html",index.html `
    /resource:"$loader",WebView2Loader.dll `
    /resource:"$coreDll",Microsoft.Web.WebView2.Core.dll `
    /resource:"$wfDll",Microsoft.Web.WebView2.WinForms.dll `
    "$hostCs"

if (-not (Test-Path $out)) { throw "compile failed, no output at $out" }
$size = (Get-Item $out).Length
Write-Host ("==> OK: {0}  ({1:N2} MB)" -f $out, ($size / 1MB))
