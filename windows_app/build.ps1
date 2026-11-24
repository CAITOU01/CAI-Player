$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319'
}
Push-Location -LiteralPath $PSScriptRoot
try {
    $output = '..\CAI Player V0.05\CAI Player.exe'
    New-Item -ItemType Directory -Force -Path '..\CAI Player V0.05' | Out-Null
    $options = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001',
        "/out:$output", '/win32icon:app.ico',
        '/resource:..\flutter_app\assets\logo_light.png,logo_light.png',
        '/resource:..\flutter_app\assets\logo_dark.png,logo_dark.png',
        '/r:vendor\TagLibSharp.dll', '/r:vendor\NAudio.dll', '/resource:vendor\NAudio.dll,NAudio.dll',
        '/resource:vendor\NAudio-LICENSE.txt,NAudio-LICENSE.txt',
        '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', '/r:System.Xaml.dll',
        "/r:$framework\WPF\PresentationCore.dll", "/r:$framework\WPF\WindowsBase.dll",
        "/r:$framework\WPF\PresentationFramework.dll", "/r:$framework\WPF\WindowsFormsIntegration.dll"
    )
    & (Join-Path $framework 'csc.exe') @options AssemblyInfo.cs Player.cs PlayerMetadata.cs TrackMetadata.cs TrackSignal.cs NowPlayingView.cs PlaybackOrder.cs MusicFolders.cs SavedPlaylists.cs LibraryBrowser.cs AudioCd.cs FlacWriter.cs Equalizer.cs VideoCaptions.cs Playlist.cs TrackEditor.cs ExportDialog.cs
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
    $libraries = @('TagLibSharp.dll','TagLibSharp-COPYING.txt','NAudio-LICENSE.txt',
        'webpdecoder-x86.dll','webpdecoder-x64.dll','libwebp-COPYING.txt','libwebp-PATENTS.txt',
        'libFLAC-x64.dll','libFLAC-x86.dll','libFLAC-COPYING.txt')
    foreach ($library in $libraries) {
        Copy-Item -LiteralPath (Join-Path 'vendor' $library) -Destination (Split-Path $output)
    }
    Copy-Item -LiteralPath 'UserGuide.zh-TW.txt' -Destination (Join-Path (Split-Path $output) '使用說明.txt')
    Copy-Item -LiteralPath '../samples/EDM(no nightcore).m3u8' -Destination (Split-Path $output)
    Get-Item -LiteralPath $output
} finally {
    Pop-Location
}
