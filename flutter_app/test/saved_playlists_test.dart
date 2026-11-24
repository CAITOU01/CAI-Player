import 'package:cai_player/main.dart';
import 'package:cai_player/saved_playlists.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  testWidgets('save the queue, reopen the app and open a renamed playlist', (tester) async {
    SharedPreferences.setMockInitialValues({});
    await tester.pumpWidget(const CaiApp());
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('我的播放清單'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('另存目前佇列'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), '夜間歌單');
    await tester.tap(find.text('儲存'));
    await tester.pumpAndSettle();
    final prefs = await SharedPreferences.getInstance();
    expect(SavedPlaylist.read(prefs).single.playlist.name, '夜間歌單');
    await tester.pumpWidget(const SizedBox());
    await tester.pumpAndSettle();
    await tester.pumpWidget(const CaiApp());
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('我的播放清單'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('管理清單'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('重新命名'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), '通勤');
    await tester.tap(find.text('儲存'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('我的播放清單'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('通勤'));
    await tester.pumpAndSettle();
    expect(find.text('通勤'), findsOneWidget);
    expect(SavedPlaylist.read(prefs).single.playlist.name, '通勤');
  });
}
