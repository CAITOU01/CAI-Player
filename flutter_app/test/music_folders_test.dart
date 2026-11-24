import 'dart:io';
import 'package:cai_player/music_folders.dart';
import 'package:cai_player/music_folders_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('folder preferences survive reload, including removing the last legacy root', () async {
    SharedPreferences.setMockInitialValues({'musicRoot': 'old'});
    final prefs = await SharedPreferences.getInstance();
    expect(readMusicFolders(prefs), ['old']);
    await saveMusicFolders(prefs, ['second', 'first']);
    await prefs.reload();
    expect(readMusicFolders(prefs), ['second', 'first']);
    await saveMusicFolders(prefs, []);
    await prefs.reload();
    expect(readMusicFolders(prefs), isEmpty);
  });

  test('scan and resolve use the saved roots in order and only enumerate direct children', () async {
    final temp = await Directory.systemTemp.createTemp('cai-music-folders-');
    addTearDown(() => temp.delete(recursive: true));
    final a = await Directory(p.join(temp.path, 'A')).create();
    final b = await Directory(p.join(temp.path, 'B')).create();
    final sub = await Directory(p.join(a.path, 'Album')).create();
    final missing = p.join(temp.path, 'offline');
    for (final dir in [a, b, sub]) {
      await File(p.join(dir.path, 'Song.MP3')).create();
      await File(p.join(dir.path, 'cover.jpg')).create();
    }
    await File(p.join(b.path, 'Other.wav')).create();
    final scan = await scanMusicFolders([b.path, missing, a.path, b.path]);
    expect(scan.files, [p.join(b.path, 'Other.wav'), p.join(b.path, 'Song.MP3'), p.join(a.path, 'Song.MP3')]);
    expect(scan.unavailable, [missing]);
    expect((await scanMusicFolders([a.path, sub.path])).files,
      [p.join(a.path, 'Song.MP3'), p.join(sub.path, 'Song.MP3')]);
    expect(await findMusicRoot('Song.MP3', [b.path, a.path], sub.path), b.path);
    expect(await findMusicRoot('Other.wav', [a.path, b.path], null), b.path);
    expect(await findMusicRoot('Other.wav', [a.path], b.path), b.path);
    expect(await findMusicRoot('Album/Song.MP3', [a.path], null), a.path);
    expect(await findMusicRoot('gone.wav', [a.path, b.path], null), a.path);
    expect(normalizeFolder(p.join(a.path, '.')), a.path);
    expect(() => normalizeFolder('Album'), throwsFormatException);
  });

  testWidgets('folder manager adds typed paths once and saves their new order', (tester) async {
    FolderChoice? choice;
    final a = p.absolute('first');
    final b = p.absolute('second');
    await tester.pumpWidget(MaterialApp(home: Builder(builder: (context) => Scaffold(
      body: TextButton(child: const Text('open'), onPressed: () async {
        choice = await showDialog<FolderChoice>(context: context,
            builder: (_) => MusicFoldersDialog(roots: [a], library: false));
      })))));
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    for (final path in [a, b]) {
      await tester.enterText(find.byType(TextField), path);
      await tester.tap(find.text('新增'));
      await tester.pumpAndSettle();
    }
    expect(find.byTooltip('管理此路徑'), findsNWidgets(2));
    await tester.tap(find.byTooltip('管理此路徑').last);
    await tester.pumpAndSettle();
    await tester.tap(find.text('上移'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('儲存'));
    await tester.pumpAndSettle();
    expect(choice?.roots, [b, a]);
    expect(choice?.openLibrary, false);
  });
}
