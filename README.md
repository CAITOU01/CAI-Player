# CAI Player V0.03 Alpha

The portable Windows app and the cross-platform Flutter project share the same CAI artwork and playlist fields. Dark mode uses the cyan play-button logo, while light mode uses the orange play-button logo.

## Quick Start

Extract the Windows release package and open `CAI Player.exe`. Import the included EDM playlist, then select the music folder containing the corresponding MP3 files. You can also drag your own audio files directly into the app.

## Building from Source

- **Portable Windows app** — Built with C#, WinForms, and the Windows media playback components. Run `.\windows_app\build.ps1` in PowerShell from the project root. The build produces a single executable in the `CAI Player V0.03` folder. Waveform analysis uses the included NAudio 1.10.0 library. See `windows_app/vendor/NAudio-LICENSE.txt` for its license. The project builds with the C# compiler included with .NET Framework 4.x.
- **Flutter app** — Continues from the earlier CAI Player Dart project. See `flutter_app/README.md` for Windows, Android, and iOS build instructions. Test playlists are available in `flutter_app/samples`.

## Playlist Compatibility

VLC M3U8 is the default export format. The Windows app was verified with libVLC 3.0.21 by comparing the user's original EDM playlist with a playlist re-exported by CAI Player. Both resolve to 68 tracks with identical display names, audio file paths, and ordering. The JSON format uses `name` and `tracks`. Each track contains `title`, `path`, and `duration`.
