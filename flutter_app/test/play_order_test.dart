import 'dart:math';
import 'package:cai_player/play_order.dart';
import 'package:cai_player/playlist.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  late PlayOrder order;
  late List<CaiTrack> tracks;
  setUp(() {
    order = PlayOrder(random: Random(27));
    tracks = List.generate(5, (i) => CaiTrack(title: 'Song $i', path: '$i.mp3'));
  });

  test('sequential playback stops, repeats a track, or wraps the playlist', () {
    expect(order.first(tracks), tracks.first);
    expect(order.next(tracks, tracks.last, completed: true), isNull);
    order.repeat = PlaylistRepeat.one;
    expect(order.next(tracks, tracks[1], completed: true), same(tracks[1]));
    expect(order.next(tracks, tracks[1]), same(tracks[2]));
    expect(order.next(tracks, tracks.last), isNull);
    order.repeat = PlaylistRepeat.all;
    expect(order.next(tracks, tracks.last, completed: true), same(tracks.first));
    expect(order.previous(tracks, tracks.first), same(tracks.last));
  });

  test('shuffle visits all tracks once and follows back/forward history', () {
    order.setShuffle(true, null);
    final sequence = [order.first(tracks)!];
    for (var i = 1; i < tracks.length; i++) {
      sequence.add(order.next(tracks, sequence.last, completed: true)!);
    }
    expect(sequence.toSet(), tracks.toSet());
    expect(order.next(tracks, sequence.last, completed: true), isNull);
    final back = order.previous(tracks, sequence.last)!;
    expect(back, same(sequence[sequence.length - 2]));
    expect(order.next(tracks, back), same(sequence.last));
    expect(order.next(tracks, sequence.last), isNull);
  });

  test('a shuffled playlist repeat avoids adjacent duplicates across rounds', () {
    order.setShuffle(true, null);
    order.repeat = PlaylistRepeat.all;
    var current = order.first(tracks)!;
    for (var round = 0; round < 10; round++) {
      final visited = {current};
      for (var i = 1; i < tracks.length; i++) {
        current = order.next(tracks, current, completed: true)!;
        visited.add(current);
      }
      expect(visited, tracks.toSet());
      final last = current;
      current = order.next(tracks, last)!;
      expect(current, isNot(same(last)));
    }
  });

  test('editing a shuffled list includes new entries and drops removed history', () {
    final original = tracks[2];
    order.setShuffle(true, original);
    final current = order.next(tracks, original)!;
    final added = CaiTrack(title: original.title, path: original.path);
    tracks.remove(original);
    order.remove(original);
    tracks = [added, ...tracks.reversed];
    expect(order.previous(tracks, current), isNull);
    final visited = {current};
    var next = order.next(tracks, current);
    while (next != null) {
      expect(visited.add(next), isTrue);
      next = order.next(tracks, next);
    }
    expect(visited, tracks.toSet());
  });

  test('single and empty queues respect each loop mode and reset', () {
    order.setShuffle(true, null);
    expect(order.first([]), isNull);
    final only = [tracks.first];
    final current = order.first(only)!;
    expect(order.next(only, current), isNull);
    order.repeat = PlaylistRepeat.one;
    expect(order.next(only, current, completed: true), same(current));
    expect(order.next(only, current), isNull);
    order.repeat = PlaylistRepeat.all;
    expect(order.next(only, current), same(current));
    order.reset();
    order.setShuffle(false, null);
    expect(order.first(tracks), same(tracks.first));
  });
}
