# CAI Player V0.05 Alpha · Cross-Platform Flutter Source

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

Open **Music Folders** from the playlist menu to type or browse for one or more local directories. The player saves their order on this device and scans them at startup. Each folder contributes only its direct audio files. Add any desired subfolder as its own entry. Save the settings again to refresh the library. Folders must be accessible to the app under the device's storage permissions. Unavailable folders remain in the settings. An older single-folder setting is retained when upgrading.

For an imported playlist, the first saved folder containing the relative track path wins. The playlist's own location can also be used when accessible. Saving folder settings keeps your open playlist unless you select **Open folder library after saving**.

Select the queue icon in the app bar to open **My Playlists**. Create a playlist, save the current queue, reopen a playlist or use its menu to rename or delete it. An open named playlist saves track edits automatically. Each song's menu can add it to another playlist. The playback controls include graphical shuffle, previous, play/pause, next, repeat and sound settings. Open sound settings for volume, mute and speed.

1. Select **Add First Song** to choose a local audio file, or use the menu in the upper-right corner to add an HTTPS audio URL.
2. Import `samples/EDM(no nightcore).m3u8` to view the original 68-track playlist. Place the playlist next to the corresponding MP3 files, or select the music folder from the menu. The sample contains only the playlist, so you must provide the audio files for its tracks.
3. Select a track to play it. Use the controls at the bottom to pause, seek, or change tracks. Shuffle, repeat-one, and repeat-playlist modes are available. Drag the handle on the left side of a track to reorder it.
4. A mobile system file picker may provide a temporary copy of a file. If a track cannot be found after import, open its menu and select **Assign Local Audio File**. This mapping lasts for the current session only, while exported playlists preserve the original path.
5. Use the sun or moon button in the upper-right corner to switch between light and dark themes.
6. The playback bar displays compact album artwork and a frequency spectrum. Select the banner or the expand button in the upper-right corner to open the expanded player. On Windows, press F11 to toggle this view and Esc to return. The playlist reads durations from accessible audio files and also refreshes them from the decoder during playback.

CAI Player tries embedded front covers, other embedded pictures, and matching image files in the track's folder. It also recognizes names such as `cover`, `folder`, `front`, and `albumart`, including mixed-case extensions. For local tracks with accessible PCM and a known sample rate, the compact and expanded players show a 64-band FFT spectrum at the playback position. Bass is on the left and treble on the right. Streaming URLs and unsupported decoders display an unavailable indicator.

## Playlist Formats

The default export format is UTF-8 VLC M3U8, preserving display names, durations in seconds, and playlist order. **Song Path** can be set to a path relative to the exported file, the original path, or an absolute path. The example updates whenever this setting changes. Desktop platforms use relative paths by default, while mobile platforms preserve the original paths. To recalculate relative paths on mobile, select a music folder for a playlist containing relative file names, then choose a writable export folder.

**Custom Export Format** supports M3U8, CAI JSON, or a custom text template, each with a configurable file extension. A custom template writes an opening section, one line per track, and a closing section in that order. Available fields include `{playlist}`, `{index}`, `{title}`, `{path}`, and `{duration}`. For example, `{index} | {title} | {path}`. Import supports M3U, M3U8, and CAI JSON. Custom text templates are available for export. After editing a playlist, use **Export Playlist** to save it.

Automated tests are located in the `test` directory. They cover all 68 tracks in the original EDM playlist, M3U8 round trips, UTF-8 BOM handling, JSON, and custom templates. Android and iOS still require verification on physical devices. Supported audio formats depend on the operating system's decoders.
