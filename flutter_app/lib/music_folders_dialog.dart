import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as p;
import 'music_folders.dart';

typedef FolderChoice = ({List<String> roots, bool openLibrary});

class MusicFoldersDialog extends StatefulWidget {
  const MusicFoldersDialog({super.key, required this.roots, required this.library});
  final List<String> roots;
  final bool library;

  @override
  State<MusicFoldersDialog> createState() => _MusicFoldersDialogState();
}

class _MusicFoldersDialogState extends State<MusicFoldersDialog> {
  late final roots = [...widget.roots];
  late bool openLibrary = widget.library;
  final path = TextEditingController();
  String? error;

  bool add(String input) {
    try {
      final folder = normalizeFolder(input);
      setState(() {
        if (!roots.any((entry) => p.equals(entry, folder))) roots.add(folder);
        path.clear();
        error = null;
      });
      return true;
    } on FormatException catch (e) {
      setState(() => error = e.message);
      return false;
    }
  }

  Future<void> browse() async {
    try {
      final folder = await FilePicker.platform.getDirectoryPath(dialogTitle: '新增音樂資料夾');
      if (mounted && folder != null) add(folder);
    } catch (e) {
      if (mounted) setState(() => error = '無法開啟資料夾選擇器：$e');
    }
  }

  @override
  void dispose() {
    path.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('音樂資料夾'),
    scrollable: true,
    insetPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 20),
    content: SizedBox(width: 440, child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      const Text('只掃描各資料夾最外層的音檔。子資料夾請另外新增。'),
      TextField(controller: path, onSubmitted: add,
          decoration: InputDecoration(labelText: '完整資料夾路徑', errorText: error)),
      Wrap(children: [
        TextButton(onPressed: () => add(path.text), child: const Text('新增')),
        TextButton(onPressed: browse, child: const Text('瀏覽…')),
      ]),
      for (var i = 0; i < roots.length; i++)
        Row(children: [
          Expanded(child: Text(roots[i])),
          PopupMenuButton<String>(tooltip: '管理此路徑', onSelected: (action) => setState(() {
            final folder = roots.removeAt(i);
            if (action == 'up') roots.insert(i - 1, folder);
            if (action == 'down') roots.insert(i + 1, folder);
          }), itemBuilder: (_) => [
            PopupMenuItem(value: 'up', enabled: i > 0, child: const Text('上移')),
            PopupMenuItem(value: 'down', enabled: i + 1 < roots.length, child: const Text('下移')),
            const PopupMenuItem(value: 'remove', child: Text('移除')),
          ]),
        ]),
      if (!widget.library) CheckboxListTile(contentPadding: EdgeInsets.zero,
          title: const Text('儲存後開啟資料夾歌庫'), value: openLibrary,
          onChanged: (value) => setState(() => openLibrary = value ?? false)),
    ])),
    actions: [
      TextButton(onPressed: () => Navigator.pop(context), child: const Text('取消')),
      FilledButton(onPressed: () {
        if (path.text.trim().isEmpty || add(path.text)) {
          Navigator.pop(context, (roots: roots, openLibrary: openLibrary));
        }
      }, child: const Text('儲存')),
    ],
  );
}
