import 'package:cai_player/main.dart';
import 'package:cai_player/now_playing.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  testWidgets(
    'Compact banner, expand and return with transport still available',
    (tester) async {
      SharedPreferences.setMockInitialValues({});
      tester.view.physicalSize = const Size(390, 844);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(const CaiApp());
      await tester.pumpAndSettle();
      expect(
        tester.widget<NowPlaying>(find.byType(NowPlaying)).expanded,
        isFalse,
      );
      await tester.tap(find.byType(NowPlaying));
      await tester.pumpAndSettle();
      expect(
        tester.widget<NowPlaying>(find.byType(NowPlaying)).expanded,
        isTrue,
      );
      expect(find.byTooltip('播放'), findsOneWidget);
      await tester.tap(find.byTooltip('返回清單'));
      await tester.pumpAndSettle();
      expect(
        tester.widget<NowPlaying>(find.byType(NowPlaying)).expanded,
        isFalse,
      );
      expect(find.text('加入第一首歌'), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );
}
