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

const maxCoverBytes = 24 * 1024 * 1024;

class SongDetails {
  final int? rate;
  final List<Uint8List> pictures;
  final List<String> imagePaths;
  SongDetails(this.rate, this.pictures, this.imagePaths);
}

SongDetails localDetails(String path) {
  final file = File(path);
  final images = <Uint8List>[];
  int? rate;
  String? album;
  try {
    final tags = readMetadata(file, getImage: true);
    rate = tags.sampleRate;
    album = tags.album?.toLowerCase();
    for (final front in [true, false]) {
      for (final pic in tags.pictures) {
        if ((pic.pictureType == PictureType.coverFront) != front) continue;
        if (pic.bytes.isNotEmpty && pic.bytes.length <= maxCoverBytes) {
          images.add(pic.bytes);
        }
      }
    }
  } catch (_) {
    // A bad picture tag should not prevent spectrum analysis or sidecar discovery.
    try {
      rate = readMetadata(file).sampleRate;
    } catch (_) {}
  }
  final name = p.basenameWithoutExtension(path).toLowerCase();
  final stems = [
    name,
    p.basename(path).toLowerCase(),
    '$name.cover',
    if (album != null) album,
    'cover',
    'folder',
    'front',
    'album',
    'albumart',
    'albumartsmall',
  ];
  const extensions = [
    '.jpg',
    '.jpeg',
    '.png',
    '.webp',
    '.jfif',
    '.bmp',
    '.gif',
  ];
  final entries = <String, String>{};
  try {
    for (final entry in file.parent.listSync(followLinks: false)) {
      final filename = p.basename(entry.path).toLowerCase();
      if (extensions.contains(p.extension(filename)) &&
          stems.contains(p.basenameWithoutExtension(filename))) {
        entries[filename] = entry.path;
      }
    }
  } on FileSystemException {
    /* Mobile file pickers may only expose the audio file itself. */
  }
  return SongDetails(rate, images, [
    for (final stem in stems.toSet())
      for (final ext in extensions)
        if (entries.containsKey('$stem$ext')) entries['$stem$ext']!,
  ]);
}
