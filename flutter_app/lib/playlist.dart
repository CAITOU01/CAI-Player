import 'dart:convert';

class CaiTrack {
  String title;
  String path;
  int duration;
  CaiTrack({required this.title, required this.path, this.duration = -1});
  Map<String, dynamic> toJson() => {'title': title, 'path': path, 'duration': duration};
  factory CaiTrack.fromJson(Map<String, dynamic> j) => CaiTrack(
      title: '${j['title'] ?? ''}', path: '${j['path'] ?? ''}',
      duration: (j['duration'] as num?)?.toInt() ?? -1);
}

class CaiPlaylist {
  String name;
  List<CaiTrack> tracks;
  CaiPlaylist(this.name, this.tracks);
}

class PlaylistCodec {
  static CaiPlaylist read(String input, {required String fallbackName}) {
    final raw = input.replaceFirst(RegExp(r'^\uFEFF'), '');
    if (raw.trimLeft().startsWith('{')) return readJson(raw);
    return readM3u(raw, fallbackName: fallbackName);
  }

  static CaiPlaylist readM3u(String raw, {String fallbackName = 'Imported Playlist'}) {
    final lines = raw.replaceFirst('\uFEFF', '').replaceAll('\r\n', '\n').replaceAll('\r', '\n').split('\n');
    String name = fallbackName;
    String? pendingTitle;
    int pendingDuration = -1;
    final tracks = <CaiTrack>[];
    for (final original in lines) {
      final line = original.trim();
      if (line.isEmpty || line == '#EXTM3U') continue;
      if (line.startsWith('#EXTINF:')) {
        final value = line.substring(8);
        final comma = value.indexOf(',');
        pendingDuration = int.tryParse((comma < 0 ? value : value.substring(0, comma)).split(' ').first) ?? -1;
        pendingTitle = comma < 0 ? null : value.substring(comma + 1).trim();
      } else if (line.startsWith('#PLAYLIST:')) {
        name = line.substring(10).trim();
      } else if (line.startsWith('#')) {
        // Historical CAI playlists include a single '#Playlist name' header.
        if (tracks.isEmpty && pendingTitle == null && name == fallbackName && !line.startsWith('#EXT')) {
          name = line.substring(1).trim();
        }
      } else {
        final uriPath = line.replaceAll('\\', '/');
        final basename = uriPath.split('/').last;
        final dot = basename.lastIndexOf('.');
        tracks.add(CaiTrack(title: pendingTitle?.isNotEmpty == true ? pendingTitle! : (dot > 0 ? basename.substring(0, dot) : basename), path: line, duration: pendingDuration));
        pendingTitle = null;
        pendingDuration = -1;
      }
    }
    return CaiPlaylist(name, tracks);
  }

  static CaiPlaylist readJson(String raw) {
    final data = jsonDecode(raw);
    if (data is! Map<String, dynamic>) throw const FormatException('Expected a JSON object');
    final items = data['tracks'];
    if (items is! List) throw const FormatException('Missing tracks array');
    return CaiPlaylist('${data['name'] ?? 'Imported Playlist'}',
        items.map((e) => CaiTrack.fromJson(Map<String, dynamic>.from(e as Map))).toList());
  }

  static String writeM3u(CaiPlaylist playlist) {
    final out = StringBuffer('#EXTM3U\n#PLAYLIST:${playlist.name.replaceAll(RegExp(r'[\r\n]'), ' ')}\n');
    for (final t in playlist.tracks) {
      out.writeln('#EXTINF:${t.duration},${t.title.replaceAll(RegExp(r'[\r\n]'), ' ')}');
      final path = t.path.replaceAll(RegExp(r'[\r\n]'), '');
      out.writeln(path.startsWith('#') ? './$path' : path);
    }
    return out.toString();
  }

  static String writeJson(CaiPlaylist p) => const JsonEncoder.withIndent('  ').convert({
    'format': 'CAI Player', 'version': 1, 'name': p.name,
    'tracks': p.tracks.map((t) => t.toJson()).toList(),
  });

  // Supported tokens: {playlist}, {title}, {path}, {duration}, {index}.
  // Custom text templates are export-only in V0.01.
  static String writeTemplate(CaiPlaylist p, {required String header, required String item, required String footer}) {
    String render(String template, CaiTrack? t, int index) {
      final fields = {
        'playlist': p.name, 'title': t?.title ?? '', 'path': t?.path ?? '',
        'duration': '${t?.duration ?? -1}', 'index': '$index',
      };
      return template.replaceAllMapped(RegExp(r'\{(playlist|title|path|duration|index)\}'),
          (token) => fields[token.group(1)]!);
    }
    return [render(header, null, 0),
      for (var i = 0; i < p.tracks.length; i++) render(item, p.tracks[i], i + 1),
      render(footer, null, 0)].where((s) => s.isNotEmpty).join('\n');
  }
}
