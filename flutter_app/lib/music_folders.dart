import 'dart:io';
import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';

const audioExtensions = ['mp3', 'wav', 'flac', 'aac', 'm4a', 'ogg', 'opus', 'aiff', 'wma'];

String normalizeFolder(String input) {
  var path = input.trim();
  if (path.length >= 2 && path.startsWith('"') && path.endsWith('"')) {
    path = path.substring(1, path.length - 1);
  }
  final absolute = Platform.isWindows
      ? RegExp(r'^(?:[a-zA-Z]:[\\/]|[\\/]{2}[^\\/]+[\\/][^\\/]+)').hasMatch(path)
      : p.isAbsolute(path);
  if (!absolute || path.contains('\x00')) throw const FormatException('請輸入完整的資料夾路徑。');
  return p.normalize(path);
}

List<String> readMusicFolders(SharedPreferences prefs) {
  final saved = prefs.getStringList('musicRoots');
  final legacy = prefs.getString('musicRoot');
  return saved ?? (legacy == null || legacy.isEmpty ? [] : [legacy]);
}

Future<void> saveMusicFolders(SharedPreferences prefs, List<String> folders) async {
  if (!await prefs.setStringList('musicRoots', folders)) {
    throw const FileSystemException('無法儲存音樂資料夾設定');
  }
}

Future<({List<String> files, List<String> unavailable})> scanMusicFolders(List<String> roots) async {
  final files = <String>[];
  final unavailable = <String>[];
  final seen = <String>{};
  for (final root in roots) {
    final found = <String>[];
    try {
      await for (final entry in Directory(root).list(recursive: false, followLinks: false)) {
        if (entry is! File) continue;
        final ext = p.extension(entry.path).toLowerCase();
        if (ext.isNotEmpty && audioExtensions.contains(ext.substring(1))) found.add(entry.path);
      }
    } on FileSystemException {
      unavailable.add(root);
      continue;
    }
    found.sort((a, b) => Platform.isWindows ? a.toLowerCase().compareTo(b.toLowerCase()) : a.compareTo(b));
    for (final file in found) {
      if (seen.add(Platform.isWindows ? file.toLowerCase() : file)) files.add(file);
    }
  }
  return (files: files, unavailable: unavailable);
}

Future<String?> findMusicRoot(String source, List<String> roots, String? playlistFolder) async {
  final candidates = [...roots, if (playlistFolder != null) playlistFolder];
  if (candidates.isEmpty) return null;
  final path = source.replaceAll('\\', Platform.pathSeparator);
  if (p.isAbsolute(path) || Uri.tryParse(source)?.hasScheme == true || source.startsWith(r'\\')) return null;
  for (final root in candidates) {
    if (await File(p.join(root, path)).exists()) return root;
  }
  return candidates.first;
}
