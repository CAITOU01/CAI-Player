import 'package:cai_player/export_paths.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

void main() {
  test('preview and export produce each supported Windows path style', () {
    String path(ExportPathStyle style) => formatSongPath(
      'Album/song.mp3', style, musicRoot: r'C:\Music', exportFolder: r'C:\Playlists', context: p.windows,
    );
    expect(path(ExportPathStyle.original), 'Album/song.mp3');
    expect(path(ExportPathStyle.relative), '../Music/Album/song.mp3');
    expect(path(ExportPathStyle.absolute), r'C:\Music\Album\song.mp3');
  });

  test('exports local paths against the chosen folder and preserves streams', () {
    const stream = 'https://example.org/曲.mp3?key=a%2Fb';
    expect(formatSongPath(stream, ExportPathStyle.relative), stream);
    expect(formatSongPath('song.mp3', ExportPathStyle.relative, boundFile: '/music/song #1.mp3',
        exportFolder: '/music/lists', context: p.posix), '../song #1.mp3');
    expect(formatSongPath('file:///music/song%20%231.mp3', ExportPathStyle.absolute, context: p.posix), '/music/song #1.mp3');
    expect(formatSongPath(r'D:\音樂\原曲.mp3', ExportPathStyle.relative,
        exportFolder: r'C:\Lists', context: p.windows), r'D:\音樂\原曲.mp3');
    expect(formatSongPath(r'D:\音樂\原曲.mp3', ExportPathStyle.relative,
        exportFolder: '/storage/lists', context: p.posix), r'D:\音樂\原曲.mp3');
    expect(() => formatSongPath('song.mp3', ExportPathStyle.absolute, context: p.posix), throwsFormatException);
  });
}
