import 'dart:io';

Future<void> copyDirectory(Directory source, Directory target) async {
  await target.create(recursive: true);
  await for (final child in source.list(followLinks: false)) {
    final name = child.uri.pathSegments.where((s) => s.isNotEmpty).last;
    final destination = target.uri.resolveUri(Uri(path: name));
    if (child is File) {
      await child.copy(File.fromUri(destination).path);
    } else if (child is Directory) {
      await copyDirectory(child, Directory.fromUri(destination));
    }
  }
}

Future<void> main() async {
  final root = File.fromUri(Platform.script).parent.parent;
  final platforms = ['android', 'ios', 'windows'];
  final missing = platforms.where((name) => !Directory.fromUri(root.uri.resolve(name)).existsSync()).toList();
  if (missing.isEmpty) {
    stdout.writeln('Platform projects are ready.');
    return;
  }
  final template = Directory.fromUri(root.uri.resolve('.dart_tool/cai_platform_template/'));
  await template.parent.create(recursive: true);
  final process = await Process.start(Platform.isWindows ? 'flutter.bat' : 'flutter', [
    'create', '--no-pub', '--project-name=cai_player', '--org=com.cai.player',
    '--platforms=${missing.join(',')}', template.path,
  ], workingDirectory: root.path, runInShell: Platform.isWindows, mode: ProcessStartMode.inheritStdio);
  final result = await process.exitCode;
  if (result != 0) { exitCode = result; return; }

  for (final platform in missing) {
    final generated = Directory.fromUri(template.uri.resolve(platform));
    if (platform == 'android') {
      final manifest = File.fromUri(generated.uri.resolve('app/src/main/AndroidManifest.xml'));
      final xml = await manifest.readAsString();
      await manifest.writeAsString(xml.replaceFirst('<application',
          '<uses-permission android:name="android.permission.INTERNET" />\n    <application')
          .replaceAll('android:label="cai_player"', 'android:label="CAI Player"'));
    } else if (platform == 'windows') {
      final source = File.fromUri(generated.uri.resolve('runner/main.cpp'));
      await source.writeAsString((await source.readAsString()).replaceAll('L"cai_player"', 'L"CAI Player"'));
    } else if (platform == 'ios') {
      final plist = File.fromUri(generated.uri.resolve('Runner/Info.plist'));
      await plist.writeAsString((await plist.readAsString()).replaceAll('<string>Cai Player</string>', '<string>CAI Player</string>'));
    }
    await copyDirectory(generated, Directory.fromUri(root.uri.resolve(platform)));
  }
  stdout.writeln('Platform projects are ready. Run flutter pub get, then flutter test.');
}
