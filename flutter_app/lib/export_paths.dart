import 'package:path/path.dart' as p;

enum ExportPathStyle {
  relative('相對於匯出檔案的位置'),
  original('保留清單原有路徑'),
  absolute('完整路徑');

  const ExportPathStyle(this.label);
  final String label;
}

bool _windowsAbsolute(String path) =>
    RegExp(r'^[A-Za-z]:[\\/]').hasMatch(path) || path.startsWith(r'\\');

String formatSongPath(
  String original,
  ExportPathStyle style, {
  String? musicRoot,
  String? boundFile,
  String? exportFolder,
  p.Context? context,
}) {
  if (style == ExportPathStyle.original) return original;
  final host = context ?? p.context;
  var source = boundFile ?? original;
  final uri = Uri.tryParse(source);
  if (uri?.scheme == 'https' || uri?.scheme == 'http') return source;
  if (uri?.scheme == 'file') {
    final windows = RegExp(r'^/[A-Za-z]:/').hasMatch(uri!.path) || uri.host.isNotEmpty;
    source = uri.toFilePath(windows: windows || host.style == p.Style.windows);
  }
  final windows = _windowsAbsolute(source) ||
      (!p.posix.isAbsolute(source) && musicRoot != null && _windowsAbsolute(musicRoot));
  final fs = windows ? p.windows : host;
  final local = source.replaceAll('\\', fs.separator);
  if (!fs.isAbsolute(local) && (musicRoot == null || !fs.isAbsolute(musicRoot))) {
    throw const FormatException('請先指定存放原始音檔的音樂資料夾。');
  }
  final full = fs.normalize(fs.isAbsolute(local) ? local : fs.join(musicRoot!, local));
  if (style == ExportPathStyle.absolute) return full;
  if (exportFolder == null) throw const FormatException('請選擇匯出資料夾。');
  if (windows && !_windowsAbsolute(exportFolder)) return full;
  final relative = fs.relative(full, from: exportFolder);
  return fs.isAbsolute(relative) ? full : relative.replaceAll(fs.separator, '/');
}
