param([ValidateSet('anycpu','x64','x86')][string]$Platform = 'anycpu')
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319'
}
Push-Location -LiteralPath $PSScriptRoot
try {
    $outputDir = if ($Platform -eq 'anycpu') { '..\CAI Player V0.05' } else { "bin\$Platform" }
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
    $output = Join-Path $outputDir 'CAI Player.exe'
    # The header is 52 logical pixels wide. Embed a 4x raster for high-DPI displays.
    Add-Type -AssemblyName System.Drawing
    $assetDir = New-Item -ItemType Directory -Force -Path 'obj\header'
    foreach ($theme in 'light','dark') {
        $logo = [Drawing.Image]::FromFile((Resolve-Path "..\flutter_app\assets\logo_$theme.png").Path)
        try {
            $small = [Drawing.Bitmap]::new(208, 208)
            try {
                $graphics = [Drawing.Graphics]::FromImage($small)
                try {
                    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.DrawImage($logo, [Drawing.Rectangle]::new(0, 0, 208, 208))
                } finally { $graphics.Dispose() }
                $small.Save((Join-Path $assetDir.FullName "logo_$theme.png"), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $small.Dispose() }
        } finally { $logo.Dispose() }
    }
    $options = @(
        '/nologo', '/target:winexe', "/platform:$Platform", '/optimize+', '/codepage:65001',
        "/out:$output", '/win32icon:app.ico',
        '/resource:obj\header\logo_light.png,logo_light.png',
        '/resource:obj\header\logo_dark.png,logo_dark.png',
        '/r:vendor\TagLibSharp.dll', '/r:vendor\NAudio.dll', '/resource:vendor\NAudio.dll,NAudio.dll',
        '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', '/r:System.Xaml.dll',
        "/r:$framework\WPF\PresentationCore.dll", "/r:$framework\WPF\WindowsBase.dll",
        "/r:$framework\WPF\PresentationFramework.dll", "/r:$framework\WPF\WindowsFormsIntegration.dll"
    )
    & (Join-Path $framework 'csc.exe') @options AssemblyInfo.cs Player.cs PlayerMetadata.cs TrackMetadata.cs TrackSignal.cs NowPlayingView.cs PlaybackOrder.cs MusicFolders.cs SavedPlaylists.cs PlaylistSidebar.cs TaskbarPlayback.cs LibraryBrowser.cs AudioCd.cs FlacWriter.cs Equalizer.cs VideoCaptions.cs Playlist.cs TrackEditor.cs ExportDialog.cs
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
    $libraries = @('TagLibSharp.dll','TagLibSharp-COPYING.txt','NAudio-LICENSE.txt',
        'webpdecoder-x86.dll','webpdecoder-x64.dll','libwebp-COPYING.txt','libwebp-PATENTS.txt',
        'libFLAC-x64.dll','libFLAC-x86.dll','libFLAC-COPYING.txt')
    foreach ($library in $libraries) {
        if ($Platform -ne 'anycpu' -and $library -match '-(x64|x86)\.dll$' -and $Matches[1] -ne $Platform) { continue }
        Copy-Item -LiteralPath (Join-Path 'vendor' $library) -Destination (Split-Path $output)
    }
    Copy-Item -LiteralPath 'UserGuide.zh-TW.txt' -Destination (Join-Path (Split-Path $output) '使用說明.txt')
    Copy-Item -LiteralPath '../samples/EDM(no nightcore).m3u8' -Destination (Split-Path $output)
    Get-Item -LiteralPath $output
} finally {
    Pop-Location
}
