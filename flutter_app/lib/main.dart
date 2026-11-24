import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart' show compute;
import 'package:flutter/services.dart';
import 'dart:ui' as ui;
import 'package:just_audio/just_audio.dart';
import 'package:path/path.dart' as p;
import 'package:shared_preferences/shared_preferences.dart';
import 'playlist.dart';
import 'play_order.dart';
import 'export_paths.dart';
import 'song_details.dart';
import 'now_playing.dart';
import 'music_folders.dart';
import 'music_folders_dialog.dart';
import 'saved_playlists.dart';

void main() => runApp(const CaiApp());

class CaiApp extends StatefulWidget {
  const CaiApp({super.key});
  @override
  State<CaiApp> createState() => _CaiAppState();
}

class _CaiAppState extends State<CaiApp> {
  ThemeMode mode = ThemeMode.system;
  @override
  Widget build(BuildContext context) => MaterialApp(
    title: 'CAI Player V0.05 Alpha', debugShowCheckedModeBanner: false,
    theme: ThemeData(colorSchemeSeed: const Color(0xff306568), useMaterial3: true),
    darkTheme: ThemeData(colorSchemeSeed: const Color(0xff76c9c2), brightness: Brightness.dark, useMaterial3: true),
    themeMode: mode,
    home: PlayerHome(onTheme: (dark) => setState(() => mode = dark ? ThemeMode.dark : ThemeMode.light)),
  );
}

class PlayerHome extends StatefulWidget {
  final ValueChanged<bool> onTheme;
  const PlayerHome({super.key, required this.onTheme});
  @override
  State<PlayerHome> createState() => _PlayerHomeState();
}

class _PlayerHomeState extends State<PlayerHome> {
  final AudioPlayer audio = AudioPlayer();
  final order = PlayOrder();
  CaiPlaylist playlist = CaiPlaylist('我的播放清單', []);
  String? playlistFile;
  List<String> musicRoots = [];
  bool folderLibrary = true;
  int folderScan = 0;
  String? savedId;
  Future<void> pendingSave = Future.value();
  late final Future<void> foldersReady;
  CaiTrack? current;
  int playbackRequest = 0;
  bool busy = false;
  bool edited = false;
  final localFiles = <CaiTrack, String>{};
  final measuredAt = <CaiTrack, String>{};
  bool expanded = false;
  int scanRequest = 0;
  Uint8List? cover;
  int? sampleRate;
  String? displayFile;
  int detailsRequest = 0;
  late final chartPositions = audio.createPositionStream(
    minPeriod: const Duration(milliseconds: 50), maxPeriod: const Duration(milliseconds: 50));
  late final StreamSubscription<Duration?> durationChanges;
  late final StreamSubscription<ProcessingState> completion;
  String format = 'm3u8';
  String extension = 'm3u8';
  ExportPathStyle pathStyle = Platform.isAndroid || Platform.isIOS
      ? ExportPathStyle.original : ExportPathStyle.relative;
  String header = '# {playlist}';
  String item = '{index}|{title}|{path}|{duration}';
  String footer = '';
  String status = '匯入播放清單或加入音檔，即可開始播放';
  @override
  void initState() {
    super.initState();
    foldersReady = _guard(_restore);
    durationChanges = audio.durationStream.listen((value) {
      if (!mounted || busy || current == null || value == null || value.inMilliseconds <= 0 || audio.duration != value) return;
      _recordDuration(current!, value);
    });
    completion = audio.processingStateStream.distinct().listen((state) {
      if (state == ProcessingState.completed && audio.processingState == state &&
          mounted && !busy && current != null) {
        final next = order.next(playlist.tracks, current!, completed: true);
        if (next != null) {
          unawaited(_play(next));
        } else {
          setState(() => status = '清單播放完畢');
          unawaited(_guard(audio.pause));
        }
      }
    });
  }
  void _recordDuration(CaiTrack track, Duration value) {
    if (value.inMilliseconds <= 0) return;
    if (displayFile != null) measuredAt[track] = displayFile!;
    if (track.duration != value.inSeconds) setState(() => track.duration = value.inSeconds);
  }

  Future<void> _scanDurations() async {
    final request = ++scanRequest;
    for (final track in List<CaiTrack>.of(playlist.tracks)) {
      if (!mounted || request != scanRequest) return;
      String path;
      try { path = await _resolve(track); } catch (_) { continue; }
      if (!mounted || request != scanRequest) return;
      if (path.startsWith('https://') || path.startsWith('http://')) continue;
      if (measuredAt.containsKey(track) && measuredAt[track] != path) {
        measuredAt.remove(track);
        setState(() => track.duration = -1);
      }
      if (track.duration > 0 || measuredAt[track] == path) continue;
      final seconds = await compute(localDuration, path);
      if (!mounted || request != scanRequest) return;
      if (seconds != null && seconds >= 0 && playlist.tracks.contains(track) && track.duration <= 0) {
        setState(() { track.duration = seconds; measuredAt[track] = path; });
      }
    }
  }

  void _showFile(String? path) {
    if (path == displayFile) return;
    final request = ++detailsRequest;
    setState(() { displayFile = path; cover = null; sampleRate = null; });
    if (path != null) unawaited(_loadDetails(path, request));
  }

  Future<void> _loadDetails(String path, int request) async {
    bool currentRequest() => mounted && detailsRequest == request;
    final details = await compute(localDetails, path);
    if (!currentRequest()) return;
    setState(() => sampleRate = details.rate);
    // Try decoding each candidate. A broken front cover must not hide valid alternatives.
    Future<bool> display(Uint8List bytes) async {
      if (!currentRequest()) return true;
      try {
        final codec = await ui.instantiateImageCodec(bytes, targetWidth: 96, allowUpscaling: false);
        try {
          final frame = await codec.getNextFrame();
          frame.image.dispose();
        } finally { codec.dispose(); }
        if (currentRequest()) setState(() => cover = bytes);
        return true;
      } catch (_) { return false; }
    }
    for (final bytes in details.pictures) {
      if (await display(bytes)) return;
    }
    for (final path in details.imagePaths) {
      if (!currentRequest()) return;
      try {
        final file = File(path);
        final size = await file.length();
        if (size > 0 && size <= maxCoverBytes && await display(await file.readAsBytes())) return;
      } on FileSystemException { continue; }
    }
  }

  Future<void> _toggleView() async {
    setState(() => expanded = !expanded);
    if (Platform.isWindows) {
      try { await const MethodChannel('cai_player/window').invokeMethod<void>('expanded', expanded); }
      on MissingPluginException { /* The test host has no native window. */ }
    }
  }
  Future<void> _restore() async {
    final prefs = await SharedPreferences.getInstance();
    if (!mounted) return;
    musicRoots = readMusicFolders(prefs);
    if (folderLibrary && musicRoots.isNotEmpty) {
      unawaited(_guard(_refreshFolders));
    } else {
      unawaited(_scanDurations());
    }
  }

  void _markEdited() {
    edited = true;
    folderLibrary = false;
    folderScan++;
    final id = savedId;
    if (id != null) {
      final generation = folderScan;
      final data = _snapshot(id).encode();
      pendingSave = pendingSave.then((_) => _guard(() async {
        await _writeSaved(id, data);
        if (mounted && id == savedId && generation == folderScan) setState(() => edited = false);
      }));
    }
  }

  SavedPlaylist _snapshot(String id) => SavedPlaylist(id, playlist, playlistFile,
      {for (var i = 0; i < playlist.tracks.length; i++)
        if (localFiles.containsKey(playlist.tracks[i])) i: localFiles[playlist.tracks[i]]!});

  Future<void> _writeSaved(String id, String data) async {
    final prefs = await SharedPreferences.getInstance();
    if (!await prefs.setString(id, data)) throw const FileSystemException('無法儲存播放清單');
  }

  Future<bool> _confirm(String title, String message, String action) async =>
      await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(title: Text(title), content: Text(message),
        actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: Text(action))])) ?? false;

  Future<String?> _playlistName(String initial) async {
    var name = initial;
    return showDialog<String>(context: context, builder: (ctx) => AlertDialog(
      title: const Text('播放清單名稱'), content: TextFormField(initialValue: initial,
          maxLength: 120, autofocus: true, onChanged: (value) => name = value.trim()),
      actions: [TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('取消')),
        FilledButton(onPressed: () { if (name.isNotEmpty) Navigator.pop(ctx, name); }, child: const Text('儲存'))]));
  }

  Future<void> _managePlaylists([CaiTrack? track]) async {
    await pendingSave;
    final prefs = await SharedPreferences.getInstance();
    if (!mounted) return;
    final choice = await choosePlaylist(context, SavedPlaylist.read(prefs), addTrack: track != null);
    if (!mounted || choice == null) return;
    var entry = choice.entry;
    switch (choice.action) {
      case PlaylistAction.append:
        if (entry == null || track == null) return;
        final path = await _resolve(track);
        if (!mounted) return;
        final copy = CaiTrack(title: track.title, path: path, duration: track.duration);
        if (entry.id == savedId) {
          setState(() { playlist.tracks.add(copy); _markEdited(); });
        } else {
          entry.playlist.tracks.add(copy);
          await _writeSaved(entry.id, entry.encode());
        }
        if (mounted) setState(() => status = '已加入「${entry!.playlist.name}」');
        return;
      case PlaylistAction.remove:
        if (entry == null || !await _confirm('刪除播放清單？', '刪除「${entry.playlist.name}」後，音檔會保留。', '刪除')) return;
        if (!await prefs.remove(entry.id)) throw const FileSystemException('無法刪除播放清單');
        if (mounted && savedId == entry.id) setState(() => savedId = null);
        return;
      case PlaylistAction.rename:
        if (entry == null) return;
        final name = await _playlistName(entry.playlist.name);
        if (name == null) return;
        entry.playlist.name = name;
        await _writeSaved(entry.id, entry.encode());
        if (mounted && savedId == entry.id) setState(() => playlist.name = name);
        return;
      case PlaylistAction.saveCopy:
        final name = await _playlistName(playlist.name);
        if (!mounted || name == null) return;
        final copy = _snapshot('${SavedPlaylist.prefix}${DateTime.now().microsecondsSinceEpoch}');
        final named = SavedPlaylist(copy.id, CaiPlaylist(name, copy.playlist.tracks), copy.source, copy.bindings);
        await _writeSaved(named.id, named.encode());
        if (mounted) setState(() {
          savedId = named.id; playlist.name = name; folderLibrary = false; ++folderScan;
          edited = false; status = '已儲存「$name」';
        });
        return;
      case PlaylistAction.create:
      case PlaylistAction.open:
        if (edited && !await _confirm('取代尚未儲存的清單？', '目前佇列將由另一份清單取代。', '繼續')) return;
        if (!mounted) return;
        if (entry == null) {
          final name = await _playlistName('');
          if (name == null) return;
          entry = SavedPlaylist('${SavedPlaylist.prefix}${DateTime.now().microsecondsSinceEpoch}', CaiPlaylist(name, []), null, {});
          await _writeSaved(entry.id, entry.encode());
        }
        if (!mounted) return;
        final opened = entry;
        ++playbackRequest; ++folderScan;
        setState(() {
          savedId = opened.id; playlist = opened.playlist; playlistFile = opened.source;
          folderLibrary = false; edited = false; current = null; busy = false;
          measuredAt.clear(); order.reset(); localFiles.clear();
          for (final binding in opened.bindings.entries) {
            if (binding.key >= 0 && binding.key < playlist.tracks.length) localFiles[playlist.tracks[binding.key]] = binding.value;
          }
          status = '已開啟「${playlist.name}」';
        });
        _showFile(null); unawaited(_scanDurations()); await audio.stop();
    }
  }

  Future<void> _refreshFolders() async {
    final request = ++folderScan;
    setState(() => status = '正在掃描音樂資料夾…');
    final result = await scanMusicFolders(musicRoots);
    if (!mounted || request != folderScan || !folderLibrary) return;
    final previous = {for (final track in playlist.tracks) track.path: track};
    final songs = [for (final file in result.files)
      previous.remove(file) ?? CaiTrack(title: p.basenameWithoutExtension(file), path: file)];
    final stop = current != null && previous.containsKey(current!.path);
    if (stop) ++playbackRequest;
    setState(() {
      for (final removed in previous.values) {
        order.remove(removed);
        measuredAt.remove(removed);
        localFiles.remove(removed);
      }
      if (stop) { current = null; busy = false; }
      playlist = CaiPlaylist('資料夾歌庫', songs);
      status = result.unavailable.isEmpty ? '歌庫已更新，共 ${songs.length} 首' :
          '歌庫已更新，無法讀取：${result.unavailable.join("、")}';
    });
    unawaited(_scanDurations());
    if (stop) { _showFile(null); await audio.stop(); }
  }

  Future<void> _pickRoot() async {
    await foldersReady;
    if (!mounted) return;
    final choice = await showDialog<FolderChoice>(context: context, builder: (_) =>
        MusicFoldersDialog(roots: musicRoots, library: folderLibrary));
    if (!mounted || choice == null) return;
    final switchLibrary = choice.openLibrary && !folderLibrary;
    if (switchLibrary && edited) {
      final replace = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
        title: const Text('取代尚未匯出的清單？'), content: const Text('目前清單將由資料夾歌庫取代。'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
          FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('開啟歌庫')),
        ]));
      if (!mounted || replace != true) return;
    }
    final prefs = await SharedPreferences.getInstance();
    await saveMusicFolders(prefs, choice.roots);
    if (!mounted) return;
    setState(() {
      musicRoots = choice.roots;
      if (switchLibrary) {
        ++playbackRequest;
        ++scanRequest;
        current = null; busy = false; edited = false; folderLibrary = true; savedId = null;
        order.reset(); localFiles.clear(); measuredAt.clear();
        playlistFile = null; playlist = CaiPlaylist('資料夾歌庫', []);
      }
      status = '已儲存音樂資料夾';
    });
    if (switchLibrary) { _showFile(null); await audio.stop(); }
    if (!mounted) return;
    if (folderLibrary) {
      await _refreshFolders();
    } else {
      final track = current;
      final request = playbackRequest;
      unawaited(_scanDurations());
      if (track != null && displayFile != null) {
        String? resolved;
        try { resolved = await _resolve(track); } on FileSystemException { resolved = null; }
        if (mounted && request == playbackRequest && resolved != displayFile) {
          ++playbackRequest;
          setState(() { current = null; busy = false; });
          _showFile(null);
          await audio.stop();
        }
      }
    }
  }
  Future<void> _import() async {
    try {
      final result = await FilePicker.platform.pickFiles(dialogTitle: '匯入播放清單');
      if (result == null) return;
      final file = result.files.single;
      final bytes = file.bytes ?? (file.path != null ? await File(file.path!).readAsBytes() : null);
      if (bytes == null) throw const FormatException('Cannot access selected file');
      final raw = utf8.decode(bytes, allowMalformed: false);
      final parsed = PlaylistCodec.read(raw, fallbackName: p.basenameWithoutExtension(file.name));
      if (!mounted) return;
      if (edited) {
        final replace = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
          title: const Text('取代尚未匯出的清單？'),
          content: Text('目前清單將由「${parsed.name}」取代。'),
          actions: [
            TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
            FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('匯入')),
          ],
        ));
        if (replace != true) return;
      }
      if (!mounted) return;
      ++playbackRequest;
      setState(() {
        busy = false; current = null; order.reset(); measuredAt.clear();
        playlist = parsed; playlistFile = file.path; edited = false; folderLibrary = false; savedId = null; localFiles.clear();
        status = '已匯入 ${parsed.tracks.length} 首歌曲';
      });
      _showFile(null);
      unawaited(_scanDurations());
      await audio.stop();
    } catch (e) { _error('無法匯入清單：$e'); }
  }
  Future<String> _resolve(CaiTrack t) async {
    final bound = localFiles[t];
    if (bound != null && await File(bound).exists()) return bound;
    final path = t.path;
    final uri = Uri.tryParse(path);
    if (uri?.scheme == 'https' || uri?.scheme == 'http') return path;
    if (uri?.scheme == 'file') return uri!.toFilePath();
    final local = path.replaceAll('\\', Platform.pathSeparator);
    if (p.isAbsolute(local) || RegExp(r'^[a-zA-Z]:[\\/]').hasMatch(path)) return local;
    final root = await findMusicRoot(path, musicRoots, playlistFile == null ? null : p.dirname(playlistFile!));
    if (root == null) throw const FileSystemException('請先選擇音樂資料夾，或從歌曲選單指定檔案');
    return p.normalize(p.join(root, local));
  }
  Future<void> _play(CaiTrack track) async {
    if (!playlist.tracks.contains(track)) return;
    final request = ++playbackRequest;
    setState(() { busy = true; current = track; status = '開啟 ${track.title}'; });
    try {
      await audio.stop();
      if (request != playbackRequest || !mounted) return;
      final source = await _resolve(track);
      if (request != playbackRequest || !mounted) return;
      final online = RegExp(r'^https?://', caseSensitive: false).hasMatch(source);
      _showFile(online ? null : source);
      if (online) {
        await audio.setUrl(source);
      } else {
        if (!await File(source).exists()) throw FileSystemException('找不到音檔。可從歌曲選單指定本機檔案', source);
        if (request != playbackRequest || !mounted) return;
        await audio.setFilePath(source);
      }
      if (request != playbackRequest || !mounted) return;
      setState(() { current = track; busy = false; status = track.title; });
      if (audio.duration != null) _recordDuration(track, audio.duration!);
      await audio.play();
    } catch (e) {
      if (request == playbackRequest && mounted) {
        _showFile(null);
        setState(() { busy = false; status = '播放失敗：$e'; });
      }
    }
  }
  Future<void> _addAudio() async {
    final result = await FilePicker.platform.pickFiles(allowMultiple: true,
      dialogTitle: '加入音檔', type: FileType.custom,
      allowedExtensions: audioExtensions);
    if (result == null || !mounted) return;
    final mobile = Platform.isAndroid || Platform.isIOS;
    setState(() {
      for (final f in result.files) {
        if (f.path == null) continue;
        final track = CaiTrack(title: p.basenameWithoutExtension(f.name), path: mobile ? f.name : f.path!);
        playlist.tracks.add(track);
        if (mobile) localFiles[track] = f.path!;
      }
      _markEdited(); status = '已加入音檔';
    });
    unawaited(_scanDurations());
  }
  Future<void> _bindFile(CaiTrack track) async {
    final result = await FilePicker.platform.pickFiles(dialogTitle: '指定「${track.title}」的本機音檔');
    final path = result?.files.single.path;
    if (path != null && mounted) setState(() {
      localFiles[track] = path; track.duration = -1; measuredAt.remove(track);
      if (savedId != null) _markEdited();
      status = '已為「${track.title}」指定本機檔案';
    });
    if (path != null && mounted) unawaited(_scanDurations());
  }
  Future<void> _addUrl() async {
    var url = '';
    final form = GlobalKey<FormState>();
    final save = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
      title: const Text('加入串流網址'), scrollable: true,
      content: Form(key: form, child: TextFormField(
        keyboardType: TextInputType.url, autofocus: true,
        decoration: const InputDecoration(labelText: 'HTTPS 音訊網址'),
        onChanged: (v) => url = v.trim(), validator: (_) {
          final uri = Uri.tryParse(url);
          return uri != null && uri.scheme == 'https' && uri.host.isNotEmpty ? null : '請輸入完整的 HTTPS 網址';
        })),
      actions: [
        TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
        FilledButton(onPressed: () { if (form.currentState!.validate()) Navigator.pop(ctx, true); }, child: const Text('加入')),
      ]));
    if (save == true && mounted) setState(() {
      final uri = Uri.parse(url);
      final name = uri.pathSegments.isEmpty ? '' : uri.pathSegments.last;
      playlist.tracks.add(CaiTrack(title: name.isEmpty ? uri.host : name, path: url));
      _markEdited();
    });
  }
  Future<void> _guard(Future<void> Function() operation) async {
    try { await operation(); } catch (e) { _error('操作失敗：$e'); }
  }
  void _error(String message) { if (mounted) setState(() => status = message); }
  Future<void> _edit(int index) async {
    final track = playlist.tracks[index];
    var title = track.title;
    var path = track.path;
    final save = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
      scrollable: true, title: const Text('編輯歌曲'), content: SizedBox(width: 440, child: Column(mainAxisSize: MainAxisSize.min, children: [
        TextFormField(initialValue: title, onChanged: (v) => title = v, decoration: const InputDecoration(labelText: '顯示名稱')),
        TextFormField(initialValue: path, onChanged: (v) => path = v, decoration: const InputDecoration(labelText: '音檔路徑或 HTTPS 網址')),
      ])), actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
      FilledButton(onPressed: () { if (path.trim().isNotEmpty) Navigator.pop(ctx, true); }, child: const Text('儲存'))],
    ));
    if (save == true && mounted) setState(() {
      if (track.path != path.trim()) { localFiles.remove(track); track.duration = -1; measuredAt.remove(track); }
      track.title = title.trim(); track.path = path.trim(); _markEdited();
    });
    if (save == true && mounted) unawaited(_scanDurations());
  }
  bool get _mobileExport => Platform.isAndroid || Platform.isIOS;

  Future<Uint8List> _exportBytes(String? folder) async {
    final playlistFolder = !_mobileExport && playlistFile != null ? p.dirname(playlistFile!) : null;
    final exported = CaiPlaylist(playlist.name, []);
    for (final track in List.of(playlist.tracks)) {
      final root = pathStyle == ExportPathStyle.original ? null : await findMusicRoot(track.path, musicRoots, playlistFolder);
      exported.tracks.add(CaiTrack(
        title: track.title, duration: track.duration,
        path: formatSongPath(track.path, pathStyle, musicRoot: root, exportFolder: folder,
            boundFile: _mobileExport ? null : localFiles[track]),
      ));
    }
    final text = format == 'json' ? PlaylistCodec.writeJson(exported)
        : format == 'custom' ? PlaylistCodec.writeTemplate(exported, header: header, item: item, footer: footer)
        : PlaylistCodec.writeM3u(exported);
    return Uint8List.fromList(utf8.encode(text));
  }

  Future<void> _export() async {
    try {
      if (!await _formatDialog(exporting: true) || !mounted) return;
      final ext = extension.trim().replaceFirst(RegExp(r'^\.+'), '');
      if (!RegExp(r'^[a-zA-Z0-9]{1,12}$').hasMatch(ext)) throw const FormatException('副檔名需使用 1 至 12 個英文字母或數字。');
      final safeName = playlist.name.replaceAll(RegExp(r'[<>:"/\\|?*\x00-\x1f]'), '_');
      final fileName = '${safeName}-export.$ext';
      String? output;
      if (_mobileExport && pathStyle == ExportPathStyle.relative) {
        final folder = await FilePicker.platform.getDirectoryPath(dialogTitle: '選擇匯出資料夾');
        if (folder == null) return;
        final bytes = await _exportBytes(folder);
        // A mobile save dialog consumes bytes before the destination is known.
        // Write into the chosen folder so every relative path has the correct base.
        final file = File(p.join(folder, fileName));
        if (await file.exists()) {
          if (!mounted) return;
          final replace = await showDialog<bool>(context: context, builder: (ctx) => AlertDialog(
            title: const Text('取代已存在的匯出檔？'), content: Text(file.path),
            actions: [
              TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
              FilledButton(onPressed: () => Navigator.pop(ctx, true), child: const Text('取代')),
            ],
          ));
          if (replace != true) return;
        }
        await _writeExport(file.path, bytes);
        output = file.path;
      } else {
        output = await FilePicker.platform.saveFile(dialogTitle: '匯出播放清單', fileName: fileName,
            type: FileType.any, bytes: _mobileExport ? await _exportBytes(null) : null);
        if (output == null) return;
        if (!_mobileExport) await _writeExport(output, await _exportBytes(p.dirname(output)));
      }
      if (mounted) setState(() { edited = false; status = _mobileExport && pathStyle != ExportPathStyle.relative
          ? '已匯出播放清單' : '已匯出 $output'; });
    } catch (e) { _error('匯出失敗：$e'); }
  }

  Future<void> _writeExport(String output, Uint8List bytes) async {
    if (playlistFile != null && p.equals(output, playlistFile!)) {
      throw const FileSystemException('請選擇新檔名以保留來源清單');
    }
    final temp = await Directory(p.dirname(output)).createTemp('.cai-export-');
    try {
      final file = File(p.join(temp.path, 'playlist'));
      await file.writeAsBytes(bytes, flush: true);
      await file.rename(output);
    } finally {
      await temp.delete(recursive: true);
    }
  }
  Future<bool> _formatDialog({bool exporting = false}) async {
    String f = format, ext = extension;
    String h = header, i = item, ft = footer;
    var style = pathStyle;
    final sampleRoot = Platform.isWindows ? r'C:\Music' : '/Music';
    final sampleFolder = Platform.isWindows ? r'C:\Playlists' : '/Playlists';
    final save = await showDialog<bool>(context: context, builder: (ctx) => StatefulBuilder(builder: (ctx, update) => AlertDialog(
      title: const Text('播放清單匯出格式'),
      content: SizedBox(width: 480, child: SingleChildScrollView(child: Column(mainAxisSize: MainAxisSize.min, children: [
        DropdownButtonFormField<String>(value: f, isExpanded: true, decoration: const InputDecoration(labelText: '內容格式'), items: const [
          DropdownMenuItem(value: 'm3u8', child: Text('VLC 相容 M3U / M3U8')),
          DropdownMenuItem(value: 'json', child: Text('CAI JSON')),
          DropdownMenuItem(value: 'custom', child: Text('自訂文字範本（匯出）')),
        ], onChanged: (v) => update(() { f = v!; ext = f == 'json' ? 'json' : f == 'custom' ? 'txt' : 'm3u8'; })),
        TextFormField(key: ValueKey(f), initialValue: ext, onChanged: (v) => ext = v, decoration: const InputDecoration(labelText: '副檔名，例如 m3u8')),
        DropdownButtonFormField<ExportPathStyle>(value: style, isExpanded: true,
          decoration: const InputDecoration(labelText: '歌曲路徑'),
          items: [for (final value in ExportPathStyle.values)
            DropdownMenuItem(value: value, child: Text(value.label))],
          onChanged: (value) => update(() => style = value!)),
        const SizedBox(height: 12),
        Align(alignment: Alignment.centerLeft, child: SelectableText(
          '路徑格式範例\n音樂資料夾　$sampleRoot\n匯出資料夾　$sampleFolder\n'
          '原有路徑　Album/song.mp3\n寫入清單　${formatSongPath("Album/song.mp3", style, musicRoot: sampleRoot, exportFolder: sampleFolder)}')),
        if (_mobileExport && style == ExportPathStyle.relative)
          const Text('請選擇可寫入的匯出資料夾。清單會直接存入該資料夾，並以它為基準計算相對路徑。'),
        if (f == 'custom') ...[
          TextFormField(initialValue: h, onChanged: (v) => h = v, decoration: const InputDecoration(labelText: '開頭')),
          TextFormField(initialValue: i, onChanged: (v) => i = v, decoration: const InputDecoration(labelText: '每首歌的範本')),
          TextFormField(initialValue: ft, onChanged: (v) => ft = v, decoration: const InputDecoration(labelText: '結尾')),
          const SizedBox(height: 8), const Text('可用欄位　{playlist} {index} {title} {path} {duration}'),
        ],
      ]))), actions: [TextButton(onPressed: () => Navigator.pop(ctx, false), child: const Text('取消')),
      FilledButton(onPressed: () => Navigator.pop(ctx, true), child: Text(exporting ? '選擇儲存位置…' : '套用'))],
    )));
    if (save != true || !mounted) return false;
    setState(() { format = f; extension = ext; header = h; item = i; footer = ft; pathStyle = style; });
    return true;
  }
  @override
  void dispose() {
    ++playbackRequest;
    ++scanRequest;
    unawaited(durationChanges.cancel());
    unawaited(completion.cancel());
    unawaited(audio.dispose());
    super.dispose();
  }

  Future<void> _togglePlay() async {
    if (playlist.tracks.isEmpty || busy) return;
    if (current == null || audio.processingState == ProcessingState.completed) {
      final first = order.first(playlist.tracks);
      if (first != null) await _play(first);
      return;
    }
    if (audio.processingState == ProcessingState.idle) {
      await _play(current!); return;
    }
    if (audio.playing) {
      await audio.pause();
    } else {
      await audio.play();
    }
  }

  Future<void> _audioControls() async {
    var gain = audio.volume.clamp(0.0, 1.0).toDouble();
    var speed = audio.speed;
    var audible = gain > 0 ? gain : 0.75;
    await showDialog<void>(context: context, builder: (ctx) => StatefulBuilder(
      builder: (ctx, update) => AlertDialog(title: const Text('播放控制'),
        content: Column(mainAxisSize: MainAxisSize.min, children: [
          Row(children: [
            IconButton(tooltip: gain == 0 ? '取消靜音' : '靜音', icon: Icon(gain == 0 ? Icons.volume_off : Icons.volume_up),
              onPressed: () { update(() { if (gain == 0) { gain = audible; } else { audible = gain; gain = 0; } });
                unawaited(_guard(() => audio.setVolume(gain))); }),
            Expanded(child: Slider(value: gain, label: '${(gain * 100).round()}%', onChanged: (v) {
              update(() { gain = v; if (v > 0) audible = v; }); unawaited(_guard(() => audio.setVolume(v)));
            })),
          ]),
          DropdownButtonFormField<double>(value: speed, decoration: const InputDecoration(labelText: '播放速度'),
            items: [for (final rate in [0.5, 0.75, 1.0, 1.25, 1.5, 2.0]) DropdownMenuItem(value: rate, child: Text('$rate×'))],
            onChanged: (v) { if (v != null) { update(() => speed = v); unawaited(_guard(() => audio.setSpeed(v))); } }),
        ]),
        actions: [TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('完成'))])));
  }

  Future<void> _skip({required bool forward}) async {
    final track = current;
    if (busy || track == null) return;
    final target = forward
        ? order.next(playlist.tracks, track)
        : order.previous(playlist.tracks, track);
    if (target != null) await _play(target);
  }

  Future<void> _remove(CaiTrack track) async {
    final stop = track == current;
    if (stop) ++playbackRequest;
    setState(() {
      if (stop) { current = null; busy = false; order.reset(); }
      order.remove(track);
      playlist.tracks.remove(track);
      localFiles.remove(track);
      measuredAt.remove(track);
      _markEdited();
    });
    if (stop) { _showFile(null); await audio.stop(); }
  }

  String _clock(Duration value) {
    final seconds = (value.inSeconds % 60).toString().padLeft(2, '0');
    return '${value.inMinutes}:$seconds';
  }

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    final knownDuration = Duration(seconds: current != null && current!.duration > 0 ? current!.duration : 0);
    final nowPlaying = NowPlaying(title: current?.title ?? '尚未播放', expanded: expanded,
        cover: cover, path: displayFile, rate: sampleRate, positions: chartPositions,
        position: audio.position, onToggle: _toggleView);
    return CallbackShortcuts(bindings: {
      const SingleActivator(LogicalKeyboardKey.f11): _toggleView,
      if (expanded) const SingleActivator(LogicalKeyboardKey.escape): _toggleView,
    }, child: Focus(autofocus: true, child: PopScope(
      canPop: !expanded,
      onPopInvokedWithResult: (popped, _) { if (!popped && expanded) _toggleView(); },
      child: Scaffold(
      appBar: AppBar(
        leading: Padding(padding: const EdgeInsets.all(7), child: Image.asset(
          dark ? 'assets/logo_dark.png' : 'assets/logo_light.png', fit: BoxFit.contain)),
        title: const Text('CAI Player'),
        actions: [
          IconButton(tooltip: '我的播放清單', onPressed: () => _guard(_managePlaylists), icon: const Icon(Icons.queue_music)),
          IconButton(tooltip: expanded ? '返回清單' : '大視窗模式',
              onPressed: _toggleView, icon: Icon(expanded ? Icons.close_fullscreen : Icons.open_in_full)),
          IconButton(tooltip: dark ? '切換淺色模式' : '切換深色模式',
            onPressed: () => widget.onTheme(!dark),
            icon: Icon(dark ? Icons.light_mode_outlined : Icons.dark_mode_outlined)),
          PopupMenuButton<String>(tooltip: '播放清單選單',
            onSelected: (action) => _guard(() async {
              switch (action) {
                case 'import': await _import(); break;
                case 'add': await _addAudio(); break;
                case 'stream': await _addUrl(); break;
                case 'root': await _pickRoot(); break;
                case 'format': await _formatDialog(); break;
                case 'export': await _export(); break;
              }
            }),
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'import', child: Text('匯入播放清單')),
              PopupMenuItem(value: 'add', child: Text('加入音檔')),
              PopupMenuItem(value: 'stream', child: Text('加入串流網址')),
              PopupMenuItem(value: 'root', child: Text('音樂資料夾')),
              PopupMenuItem(value: 'format', child: Text('自訂匯出格式')),
              PopupMenuItem(value: 'export', child: Text('匯出播放清單')),
            ]),
        ]),
      body: SafeArea(child: Column(children: [
        if (!expanded) ...[
        Padding(padding: const EdgeInsets.fromLTRB(20, 10, 16, 12),
          child: Row(children: [
            Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(playlist.name, maxLines: 1, overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.titleLarge),
              Text('${playlist.tracks.length} 首歌曲　·　V0.05 Alpha',
                style: Theme.of(context).textTheme.bodySmall),
            ])),
            IconButton(tooltip: '加入音檔', icon: const Icon(Icons.add), onPressed: () => _guard(_addAudio)),
            IconButton(tooltip: '匯入清單', icon: const Icon(Icons.file_open_outlined),
              onPressed: () => _guard(_import)),
          ])),
        const Divider(height: 1),
        Expanded(child: playlist.tracks.isEmpty
          ? Center(child: OutlinedButton.icon(onPressed: () => _guard(_addAudio),
              icon: const Icon(Icons.library_music_outlined), label: const Text('加入第一首歌')))
          : ReorderableListView.builder(
              buildDefaultDragHandles: false,
              itemCount: playlist.tracks.length,
              onReorder: (from, to) => setState(() {
                final moved = playlist.tracks.removeAt(from);
                playlist.tracks.insert(to > from ? to - 1 : to, moved);
                _markEdited();
              }),
              itemBuilder: (_, i) {
                final track = playlist.tracks[i];
                return ListTile(key: ObjectKey(track), selected: current == track,
                  onTap: () => _play(order.choose(playlist.tracks, current, track)),
                  leading: ReorderableDragStartListener(index: i,
                    child: const Tooltip(message: '拖曳排序', child: Icon(Icons.drag_indicator))),
                  title: Row(children: [Expanded(child: Text(track.title, maxLines: 1, overflow: TextOverflow.ellipsis)),
                    const SizedBox(width: 8), Text(track.duration >= 0 ? _clock(Duration(seconds: track.duration)) : '—',
                        style: Theme.of(context).textTheme.bodySmall)]),
                  subtitle: Text(track.path, maxLines: 1, overflow: TextOverflow.ellipsis),
                  trailing: PopupMenuButton<String>(tooltip: '歌曲選單',
                    onSelected: (action) => _guard(() async {
                      switch (action) {
                        case 'playlist': await _managePlaylists(track); break;
                        case 'edit': await _edit(i); break;
                        case 'bind': await _bindFile(track); break;
                        case 'remove': await _remove(track); break;
                      }
                    }), itemBuilder: (_) => const [
                      PopupMenuItem(value: 'playlist', child: Text('加入已儲存的播放清單')),
                      PopupMenuItem(value: 'edit', child: Text('編輯歌曲')),
                      PopupMenuItem(value: 'bind', child: Text('指定本機音檔')),
                      PopupMenuItem(value: 'remove', child: Text('從清單移除')),
                    ]));
              })),
        const Divider(height: 1),
        ],
        if (expanded) Expanded(child: nowPlaying) else nowPlaying,
        StreamBuilder<Duration>(stream: audio.positionStream, builder: (context, snap) {
          final pos = snap.data ?? Duration.zero;
          final total = audio.duration ?? knownDuration;
          final max = total.inMilliseconds > 0 ? total.inMilliseconds.toDouble() : 1.0;
          return Padding(padding: const EdgeInsets.symmetric(horizontal: 16), child: Row(children: [
            Text(_clock(pos)),
            Expanded(child: Slider(value: pos.inMilliseconds.toDouble().clamp(0.0, max).toDouble(), max: max,
              onChanged: total > Duration.zero ? (v) => _guard(() => audio.seek(Duration(milliseconds: v.toInt()))) : null)),
            Text(total > Duration.zero ? _clock(total) : '—'),
          ]));
        }),
        Row(mainAxisAlignment: MainAxisAlignment.center, children: [
          IconButton(tooltip: order.shuffle ? '隨機播放已開啟' : '隨機播放已關閉',
            isSelected: order.shuffle, selectedIcon: const Icon(Icons.shuffle_on),
            onPressed: () => setState(() => order.setShuffle(!order.shuffle, current)),
            icon: const Icon(Icons.shuffle)),
          IconButton(tooltip: '上一首', onPressed: !busy && current != null && order.hasPrevious(playlist.tracks, current!)
              ? () => _skip(forward: false) : null,
            icon: const Icon(Icons.skip_previous)),
          StreamBuilder<PlayerState>(stream: audio.playerStateStream, builder: (_, snap) {
            final playing = audio.playing && audio.processingState != ProcessingState.completed;
            return IconButton.filled(tooltip: playing ? '暫停' : '播放',
              onPressed: busy || playlist.tracks.isEmpty ? null : () => _guard(_togglePlay),
              icon: Icon(playing ? Icons.pause : Icons.play_arrow), iconSize: 28);
          }),
          IconButton(tooltip: '下一首', onPressed: !busy && current != null && order.hasNext(playlist.tracks, current!)
              ? () => _skip(forward: true) : null, icon: const Icon(Icons.skip_next)),
          PopupMenuButton<PlaylistRepeat>(
            tooltip: const {PlaylistRepeat.off: '循環已關閉', PlaylistRepeat.one: '單曲循環', PlaylistRepeat.all: '播放清單循環'}[order.repeat],
            initialValue: order.repeat,
            icon: Icon(order.repeat == PlaylistRepeat.one ? Icons.repeat_one : Icons.repeat,
              color: order.repeat == PlaylistRepeat.off ? null : Theme.of(context).colorScheme.primary),
            onSelected: (mode) => setState(() => order.repeat = mode),
            itemBuilder: (_) => const [
              PopupMenuItem(value: PlaylistRepeat.off, child: Text('關閉循環')),
              PopupMenuItem(value: PlaylistRepeat.one, child: Text('單曲循環')),
              PopupMenuItem(value: PlaylistRepeat.all, child: Text('播放清單循環')),
            ]),
          IconButton(tooltip: '音量與播放速度', onPressed: _audioControls, icon: const Icon(Icons.tune)),
        ]),
        Padding(padding: const EdgeInsets.fromLTRB(16, 4, 16, 9),
          child: Text(status, maxLines: 2, overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodySmall)),
      ]))))));
  }
}
