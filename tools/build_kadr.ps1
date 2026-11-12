param(
    [Parameter(Mandatory)]
    [string] $MakeNsis
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'screen-recorder'
$build = Join-Path $root 'build/screen-recorder'
$stage = Join-Path $build 'app'
$sdk = Join-Path $build 'sdk'
$output = Join-Path $root 'build/installer'
$setup = Join-Path $output 'Kadr Setup.exe'
$icon = Join-Path $source 'Kadr.ico'
$csc = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'ui'), $output | Out-Null

function Get-Dependency($Name, $Url, $Hash) {
    $file = Join-Path $build $Name
    if (!(Test-Path -LiteralPath $file)) {
        $pending = "$file.tmp"
        & curl.exe --fail --silent --show-error --location --max-time 120 $Url -o $pending
        if ($LASTEXITCODE -ne 0) { throw "Could not download $Name." }
        Move-Item -LiteralPath $pending -Destination $file
    }
    if ($Hash -and (Get-FileHash -LiteralPath $file).Hash -ne $Hash) {
        throw "Unexpected SHA-256 for $file."
    }
    return $file
}

$webViewSdk = Get-Dependency 'WebView2.zip' 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3856.49/microsoft.web.webview2.1.0.3856.49.nupkg' 'BC0F76EB911B569838DC4AA8F8D325269B966BEDB592863D26211AEF3A099F1A'
$webViewSetup = Get-Dependency 'WebView2Setup.exe' 'https://go.microsoft.com/fwlink/p/?LinkId=2124703'
$signature = Get-AuthenticodeSignature -LiteralPath $webViewSetup
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation(?:,|$)') {
    throw 'WebView2 bootstrapper does not have a valid Microsoft signature.'
}
$presentMon = Get-Dependency 'PresentMon.exe' 'https://github.com/GameTechDev/PresentMon/releases/download/v2.3.1/PresentMon-2.3.1-x64.exe' '364E5D98D4D134BD54DD25C22ED2CA2F4883F8BC3ED6502BEE0C151E3436D30C'
$presentMonLicense = Get-Dependency 'PresentMon-LICENSE.txt' 'https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.3.1/LICENSE.txt' '4C949341B1893C8C6AD82F7FB4EEDF622CD1FD9C22A9AF8F19B2DAC19D1947B6'

Expand-Archive -LiteralPath $webViewSdk -DestinationPath $sdk -Force
$core = Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.Core.dll'
$winForms = Join-Path $sdk 'lib/net462/Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $sdk 'runtimes/win-x64/native/WebView2Loader.dll'
Copy-Item -LiteralPath $core, $winForms, $loader, $presentMon, $presentMonLicense -Destination $stage
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE.txt') -Destination (Join-Path $stage 'WebView2-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $source 'Screen Recorder.html') -Destination (Join-Path $stage 'ui')

$sources = (Join-Path $source 'Desktop.cs'), (Join-Path $source 'FpsOverlay.cs')
& $csc /nologo /utf8output /target:winexe /platform:x64 /optimize+ /codepage:65001 "/win32icon:$icon" "/out:$stage\Kadr.exe" "/r:$core" "/r:$winForms" /r:System.Windows.Forms.dll /r:System.Drawing.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Kadr compilation failed.' }

& $MakeNsis /INPUTCHARSET UTF8 /WX "/DAPP_DIR=$stage" "/DWEBVIEW_SETUP=$webViewSetup" "/DSETUP_EXE=$setup" "/DKADR_ICON=$icon" (Join-Path $source 'Kadr.nsi')
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $setup -PathType Leaf)) { throw 'Kadr installer build failed.' }
Get-Item -LiteralPath $setup
