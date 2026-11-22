# CAI Player V0.03 Alpha · Cross-Platform Flutter Source

This directory continues from the CAI Player V0.01 Dart codebase and includes the original EDM playlist. The interface is built with Flutter, while just_audio connects to each platform's playback backend. Windows uses just_audio_windows.

## Build and Test

Open a terminal in this directory. This release was verified with Flutter 3.50.1 stable and Dart 3.14.0. Add Flutter's `bin` directory to your PATH. Windows requires the Visual Studio Desktop development with C++ workload, Android requires the Android SDK, and iOS requires Xcode on macOS. Run `flutter doctor` to check the installed platform tools.

```sh
flutter pub get
flutter test
```

The project includes native runner projects for Windows, Android, and iOS, along with CAI launcher icons and a package lockfile. An internet connection is required for the first build.

- Run `flutter run -d windows` to launch a Windows development build. Use `flutter build windows --release` to produce a release build.
- Connect an Android device and run `flutter run -d <device ID>`. Use `flutter build apk --release` to produce a test APK.
- On a Mac, run `flutter build ios --simulator` to create a simulator build. Installing on a physical device requires configuring your own signing team in Xcode.

## Playing Music

1. Select **Add First Song** to choose a local audio file, or use the menu in the upper-right corner to add an HTTPS audio URL.
2. Import `samples/EDM(no nightcore).m3u8` to view the original 68-track playlist. Place the playlist next to the corresponding MP3 files, or select the music folder from the menu. The sample contains only the playlist, so you must provide the audio files for its tracks.
3. Select a track to play it. Use the controls at the bottom to pause, seek, or change tracks. Shuffle, repeat-one, and repeat-playlist modes are available. Drag the handle on the left side of a track to reorder it.
4. A mobile system file picker may provide a temporary copy of a file. If a track cannot be found after import, open its menu and select **Assign Local Audio File**. This mapping lasts for the current session only, while exported playlists preserve the original path.
5. Use the sun or moon button in the upper-right corner to switch between light and dark themes.
6. The playback bar displays compact album artwork and a waveform. Select the banner or the expand button in the upper-right corner to open the expanded player. On Windows, press F11 to toggle this view and Esc to return. The playlist reads durations from accessible audio files and also refreshes them from the decoder during playback.

Embedded artwork, an image with the same base name as the audio file, or a `cover`, `folder`, or `front` image in the same directory can be used as album artwork. The waveform uses SoLoud to read samples from local audio files and displays them according to the current playback position. Available formats and durations depend on what the decoder can read. The graph displays a flat line for streaming URLs or files whose samples cannot be accessed.

## Playlist Formats

The default export format is UTF-8 VLC M3U8, preserving display names, durations in seconds, and playlist order. **Song Path** can be set to a path relative to the exported file, the original path, or an absolute path. The example updates whenever this setting changes. Desktop platforms use relative paths by default, while mobile platforms preserve the original paths. To recalculate relative paths on mobile, select a music folder for a playlist containing relative file names, then choose a writable export folder.

**Custom Export Format** supports M3U8, CAI JSON, or a custom text template, each with a configurable file extension. A custom template writes an opening section, one line per track, and a closing section in that order. Available fields include `{playlist}`, `{index}`, `{title}`, `{path}`, and `{duration}`. For example, `{index} | {title} | {path}`. Import supports M3U, M3U8, and CAI JSON. Custom text templates are available for export. After editing a playlist, use **Export Playlist** to save it.

Automated tests are located in the `test` directory. They cover all 68 tracks in the original EDM playlist, M3U8 round trips, UTF-8 BOM handling, JSON, and custom templates. Android and iOS still require verification on physical devices. Supported audio formats depend on the operating system's decoders.
