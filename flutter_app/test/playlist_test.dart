import 'dart:io';
import 'package:flutter_test/flutter_test.dart';
import 'package:cai_player/playlist.dart';

void main() {
  test('real EDM fixture preserves all 68 ordered title/path pairs', () {
    final source = File('samples/EDM(no nightcore).m3u8').readAsStringSync();
    final playlist = PlaylistCodec.read(source, fallbackName: 'Fixture');
    expect(playlist.name, 'EDM(no nightcore)');
    expect(playlist.tracks.length, 68);
    final expected = source.split(RegExp(r'\r?\n')).where((line) =>
        line.trim().isNotEmpty && !line.startsWith('#')).toList();
    expect(playlist.tracks.map((track) => track.path), expected);
    final reparsed = PlaylistCodec.readM3u(PlaylistCodec.writeM3u(playlist));
    expect(reparsed.name, playlist.name);
    expect(reparsed.tracks.map((track) => track.toJson()), playlist.tracks.map((track) => track.toJson()));
  });
  test('historical CAI/VLC M3U8 header, EXTINF and paths', () {
    const input = '#EXTM3U\n#EDM(no nightcore)\n#EXTINF:-1,Lost Sky - Need You [NCS Release]\nLost Sky - Need You [NCS Release].mp3\n';
    final p = PlaylistCodec.readM3u(input);
    expect(p.name, 'EDM(no nightcore)');
    expect(p.tracks.length, 1);
    expect(p.tracks.first.title, 'Lost Sky - Need You [NCS Release]');
    expect(p.tracks.first.duration, -1);
    final roundtrip = PlaylistCodec.readM3u(PlaylistCodec.writeM3u(p));
    expect(roundtrip.tracks.first.path, p.tracks.first.path);
  });
  test('UTF8 BOM and missing EXTINF', () {
    final p = PlaylistCodec.readM3u('\uFEFF#EXTM3U\nFolder/song.mp3');
    expect(p.tracks.single.title, 'song');
  });
  test('JSON roundtrip', () {
    final p = CaiPlaylist('My List', [CaiTrack(title: '歌名', path: '音樂/歌.mp3')]);
    final result = PlaylistCodec.readJson(PlaylistCodec.writeJson(p));
    expect(result.tracks.single.title, '歌名');
  });
  test('custom template tokens', () {
    final p = CaiPlaylist('EDM', [CaiTrack(title: 'Song', path: 'a.mp3')]);
    expect(PlaylistCodec.writeTemplate(p, header: '{playlist}', item: '{index}|{title}|{path}', footer: ''), 'EDM\n1|Song|a.mp3');
  });
}
