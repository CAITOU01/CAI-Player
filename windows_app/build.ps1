$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319'
}
Push-Location -LiteralPath $PSScriptRoot
try {
    $output = '..\CAI Player V0.03\CAI Player.exe'
    New-Item -ItemType Directory -Force -Path '..\CAI Player V0.03' | Out-Null
    $options = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
        "/out:$output", '/win32icon:app.ico',
        '/resource:..\flutter_app\assets\logo_light.png,logo_light.png',
        '/resource:..\flutter_app\assets\logo_dark.png,logo_dark.png',
        '/r:vendor\NAudio.dll', '/resource:vendor\NAudio.dll,NAudio.dll',
        '/resource:vendor\NAudio-LICENSE.txt,NAudio-LICENSE.txt',
        '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
        "/r:$framework\WPF\PresentationCore.dll", "/r:$framework\WPF\WindowsBase.dll"
    )
    & (Join-Path $framework 'csc.exe') @options AssemblyInfo.cs Player.cs PlayerMetadata.cs TrackMetadata.cs TrackSignal.cs NowPlayingView.cs PlaybackOrder.cs Playlist.cs TrackEditor.cs ExportDialog.cs
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
    Get-Item -LiteralPath $output
} finally {
    Pop-Location
}
