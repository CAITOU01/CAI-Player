import 'dart:math' as math;
import 'dart:typed_data';

import 'package:cai_player/spectrum.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('FFT separates bass and treble and produces zero for silence', () {
    for (final rate in [22050, 48000]) {
      final pcm = Float32List(rate + 4096);
      for (var i = 0; i < pcm.length; i++) {
        final freq = i < rate ~/ 3
            ? 120
            : i < 2 * rate ~/ 3
            ? 6400
            : 0;
        pcm[i] = .7 * math.sin(2 * math.pi * freq * i / rate);
      }
      final frames = analyzeSpectrum(pcm, rate);
      int peak(int frame) {
        final offset = frame * spectrumBands;
        final bands = frames.sublist(offset, offset + spectrumBands);
        expect(bands.reduce(math.max), greaterThan(.8));
        return bands.indexOf(bands.reduce(math.max));
      }

      expect(peak(0), lessThan(8));
      expect(peak(8), greaterThan(40));
      expect(
        frames.sublist(15 * spectrumBands, 16 * spectrumBands),
        everyElement(0),
      );
    }
  });
}
