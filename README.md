# CAI Player V0.05 Alpha

The portable Windows app and the cross-platform Flutter project share the same CAI artwork and playlist fields. Dark mode uses the cyan play-button logo, while light mode uses the orange play-button logo.

## Quick Start

Extract the Windows release package and open `CAI Player.exe`. Open **Music Folders** (音樂資料夾), type a full path or browse for a folder, then choose **Save** (儲存). You can add, remove and reorder folders. On startup, the player loads the saved folders and lists the audio files directly inside each one. Add a subfolder separately to include its songs. Open the folder settings and save again to rescan after adding files or reconnecting a drive.

You can also import the included EDM playlist or drag audio files into the app. For an imported relative path, the player tries the saved folders in order and then the playlist's own directory. Saving folder settings keeps the current playlist open. To switch back to the folder library, select **Open folder library after saving** in the same dialog.

## Saved Playlists

Open **My Playlists** (我的播放清單) to create an empty playlist, save the current queue under a name, or reopen a playlist. Changes to an open named playlist are saved automatically on this device. You can rename or delete playlists and add a selected song to another playlist. Use **Export Playlist** to make a file for another device or player.

## Windows Playback

- Open **Library** (音樂庫) to search by title, artist, album, genre or path. The source selector includes the folder library, the 50 most recent tracks and the video library. From a result you can play the visible list, play a song next or append it to the queue.
- Point to a playback icon to see its name and state. The repeat menu offers off, repeat one and repeat all. The speaker button mutes or restores the volume. The speed menu ranges from 0.5× to 2×. Use Ctrl+Left or Ctrl+Right to seek ten seconds.
- Use the equalizer to adjust ten bands from 31 Hz to 16 kHz on supported local audio files. **Flat** (平坦) resets the bands. Use the mini player for a floating, always-on-top window with artwork, spectrum and playback controls.
- Open a video with the film icon, or add video folders under **Library → Video Library**. Double-click the picture for full screen. A matching SRT or WebVTT file beside the video loads automatically. Right-click the picture to select or disable subtitles. Playback uses the codecs installed on Windows.
- Open **Audio CD** with an audio disc in a compatible drive. Select a track to play it or extract it as WAV or FLAC. MP3, AAC, WMA and Apple Lossless exports use Windows encoders. FLAC uses the bundled x86 and x64 builds of libFLAC 1.5.0. The BSD license is in `windows_app/vendor/libFLAC-COPYING.txt`. [Official FLAC downloads](https://xiph.org/flac/download.html)

## Building from Source

- **Portable Windows app** — Built with C#, WinForms, and the Windows media playback components. Run `.\windows_app\build.ps1` in PowerShell from the project root. The build produces the executable and its artwork library in the `CAI Player V0.05` folder. Audio spectrum analysis uses the included NAudio 1.10.0 library. See `windows_app/vendor/NAudio-LICENSE.txt` for its license. TagLibSharp 2.3.0 reads embedded cover art. Keep its DLL beside the executable. Its LGPL license is in `windows_app/vendor/TagLibSharp-COPYING.txt` and its [source is available here](https://github.com/mono/taglib-sharp/tree/TaglibSharp-2.3.0.0). The Windows app requires .NET Framework 4.6.2 or later.
- **Flutter app** — Continues from the earlier CAI Player Dart project. See `flutter_app/README.md` for Windows, Android, and iOS build instructions. Test playlists are available in `flutter_app/samples`.

## Playlist Compatibility

| Export format | When to use it | Import into CAI Player |
| --- | --- | --- |
| VLC M3U8 (UTF-8) | Play the same ordered list in VLC or share it with another compatible player. Each track has a title, duration in seconds, and path or URL. | Yes |
| CAI JSON | Edit or process the playlist as structured data. The playlist name and ordered track records are explicit fields. | Yes |
| Custom text template | Choose the fields and layout for a report or another program, for example `{index} - {title}` for a numbered track list. | Export only |

All three formats reference tracks. When moving music to a different computer, copy the audio files and preserve their layout relative to the playlist if you exported relative paths.

The Windows package includes the x86 and x64 WebP decoders built from [libwebp 1.6.0](https://storage.googleapis.com/downloads.webmproject.org/releases/webp/libwebp-1.6.0.tar.gz). Keep the DLLs alongside the player when extracting the ZIP. See the bundled libwebp copyright and patent notices in `windows_app/vendor`.

VLC M3U8 is the default export format. The Windows app was verified with libVLC 3.0.21 by comparing the user's original EDM playlist with a playlist re-exported by CAI Player. Both resolve to 68 tracks with identical display names, audio file paths, and ordering. The JSON format uses `name` and `tracks`. Each track contains `title`, `path`, and `duration`.
