import 'dart:math';
import 'playlist.dart';

enum PlaylistRepeat { off, one, all }

class PlayOrder {
  final Random _random;
  final Set<CaiTrack> _played = {};
  final List<CaiTrack> _history = [];
  final List<CaiTrack> _forward = [];
  bool _shuffle = false;
  bool get shuffle => _shuffle;
  PlaylistRepeat repeat = PlaylistRepeat.off;

  PlayOrder({Random? random}) : _random = random ?? Random();

  void reset() {
    _played.clear();
    _history.clear();
    _forward.clear();
  }

  void setShuffle(bool enabled, CaiTrack? current) {
    if (enabled == _shuffle) return;
    _shuffle = enabled;
    reset();
    if (enabled && current != null) _played.add(current);
  }

  void remove(CaiTrack track) {
    _played.remove(track);
    _history.removeWhere((entry) => entry == track);
    _forward.removeWhere((entry) => entry == track);
  }

  CaiTrack? first(List<CaiTrack> tracks) {
    reset();
    if (tracks.isEmpty) return null;
    final track = shuffle ? tracks[_random.nextInt(tracks.length)] : tracks.first;
    if (shuffle) _played.add(track);
    return track;
  }

  CaiTrack choose(List<CaiTrack> tracks, CaiTrack? current, CaiTrack choice) {
    if (shuffle) {
      _forward.clear();
      if (current != null && current != choice) _remember(current, tracks.length);
      _played.add(choice);
    }
    return choice;
  }

  void _remember(CaiTrack current, int count) {
    _history.add(current);
    // Keep the current and preceding rounds available in long-running sessions.
    final limit = max(2, count * 2);
    if (_history.length > limit) _history.removeRange(0, _history.length - limit);
  }

  bool hasNext(List<CaiTrack> tracks, CaiTrack current) {
    if (repeat == PlaylistRepeat.all && tracks.isNotEmpty) return true;
    if (shuffle) return _forward.isNotEmpty || _played.length < tracks.length;
    return tracks.indexOf(current) + 1 < tracks.length;
  }

  bool hasPrevious(List<CaiTrack> tracks, CaiTrack current) {
    if (shuffle) return _history.isNotEmpty;
    return tracks.indexOf(current) > 0 || (repeat == PlaylistRepeat.all && tracks.isNotEmpty);
  }

  CaiTrack? next(List<CaiTrack> tracks, CaiTrack current, {bool completed = false}) {
    if (tracks.isEmpty || !tracks.contains(current)) return null;
    if (completed && repeat == PlaylistRepeat.one) return current;
    if (!hasNext(tracks, current)) return null;
    if (!shuffle) return tracks[(tracks.indexOf(current) + 1) % tracks.length];
    if (_forward.isNotEmpty) {
      _remember(current, tracks.length);
      return _forward.removeLast();
    }
    var choices = tracks.where((track) => !_played.contains(track)).toList();
    if (choices.isEmpty) {
      _played.clear();
      choices = tracks.length == 1 ? tracks : tracks.where((track) => track != current).toList();
    }
    final choice = choices[_random.nextInt(choices.length)];
    return choose(tracks, current, choice);
  }

  CaiTrack? previous(List<CaiTrack> tracks, CaiTrack current) {
    if (!hasPrevious(tracks, current)) return null;
    if (!shuffle) return tracks[(tracks.indexOf(current) - 1) % tracks.length];
    _forward.add(current);
    return _history.removeLast();
  }
}
