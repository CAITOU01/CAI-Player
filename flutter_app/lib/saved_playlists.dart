import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'playlist.dart';

class SavedPlaylist {
  SavedPlaylist(this.id, this.playlist, this.source, this.bindings);
  final String id;
  final CaiPlaylist playlist;
  final String? source;
  final Map<int, String> bindings;
  static const prefix = 'savedPlaylist.';

  String encode() => jsonEncode({
    'queue': jsonDecode(PlaylistCodec.writeJson(playlist)), 'source': source,
    'bindings': {for (final e in bindings.entries) e.key.toString(): e.value},
  });

  static List<SavedPlaylist> read(SharedPreferences prefs) {
    final items = <SavedPlaylist>[];
    for (final id in prefs.getKeys().where((key) => key.startsWith(prefix))) {
      final data = jsonDecode(prefs.getString(id)!) as Map<String, dynamic>;
      final tracks = PlaylistCodec.readJson(jsonEncode(data['queue']));
      final files = (data['bindings'] as Map<String, dynamic>?) ?? {};
      items.add(SavedPlaylist(id, tracks, data['source'] as String?,
          {for (final e in files.entries) int.parse(e.key): e.value as String}));
    }
    return items..sort((a, b) => a.playlist.name.compareTo(b.playlist.name));
  }
}

enum PlaylistAction { open, create, saveCopy, rename, remove, append }
typedef PlaylistChoice = ({PlaylistAction action, SavedPlaylist? entry});

Future<PlaylistChoice?> choosePlaylist(BuildContext context, List<SavedPlaylist> entries, {bool addTrack = false}) =>
    showDialog<PlaylistChoice>(context: context, builder: (ctx) => AlertDialog(
      title: Text(addTrack ? '將歌曲加入播放清單' : '我的播放清單'),
      content: SizedBox(width: 420, child: entries.isEmpty ? const Text('還沒有儲存的播放清單。')
        : ListView(shrinkWrap: true, children: [for (final entry in entries) ListTile(
          leading: const Icon(Icons.queue_music), title: Text(entry.playlist.name),
          subtitle: Text('${entry.playlist.tracks.length} 首'),
          onTap: () => Navigator.pop(ctx, (action: addTrack ? PlaylistAction.append : PlaylistAction.open, entry: entry)),
          trailing: addTrack ? null : PopupMenuButton<PlaylistAction>(tooltip: '管理清單',
            itemBuilder: (_) => const [
              PopupMenuItem(value: PlaylistAction.rename, child: Text('重新命名')),
              PopupMenuItem(value: PlaylistAction.remove, child: Text('刪除')),
            ], onSelected: (action) => Navigator.pop(ctx, (action: action, entry: entry))),
        )])),
      actions: [
        if (!addTrack) ...[
          TextButton.icon(icon: const Icon(Icons.playlist_add), label: const Text('新增空白清單'),
              onPressed: () => Navigator.pop(ctx, (action: PlaylistAction.create, entry: null))),
          TextButton.icon(icon: const Icon(Icons.save_outlined), label: const Text('另存目前佇列'),
              onPressed: () => Navigator.pop(ctx, (action: PlaylistAction.saveCopy, entry: null))),
        ],
        TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('關閉')),
      ],
    ));
