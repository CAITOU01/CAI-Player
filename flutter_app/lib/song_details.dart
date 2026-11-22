import 'dart:io';
import 'dart:typed_data';

import 'package:audio_metadata_reader/audio_metadata_reader.dart';
import 'package:path/path.dart' as p;

int? localDuration(String path) {
  try {
    final file = File(path);
    if (!file.existsSync()) return null;
    return readMetadata(file).duration?.inSeconds;
  } catch (_) {
    return null;
  }
}

Uint8List? localCover(String path) {
  final file = File(path);
  try {
    final pictures = readMetadata(file, getImage: true).pictures;
    if (pictures.isNotEmpty) {
      final front = pictures.firstWhere(
        (p) => p.pictureType == PictureType.coverFront,
        orElse: () => pictures.first,
      );
      if (front.bytes.isNotEmpty && front.bytes.length <= 24 * 1024 * 1024)
        return front.bytes;
    }
  } catch (_) {
    /* Continue with artwork stored next to the song. */
  }
  for (final name in [
    p.basenameWithoutExtension(path),
    'cover',
    'folder',
    'front',
  ]) {
    for (final ext in ['jpg', 'jpeg', 'png']) {
      final cover = File(p.join(file.parent.path, '$name.$ext'));
      try {
        if (cover.existsSync() && cover.lengthSync() <= 24 * 1024 * 1024)
          return cover.readAsBytesSync();
      } on FileSystemException {
        continue;
      }
    }
  }
  return null;
}
