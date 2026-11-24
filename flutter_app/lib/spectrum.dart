import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter_soloud/flutter_soloud.dart' show SoLoudIsolate;

const spectrumBands = 64;
const framesPerSecond = 20;
const _size = 4096;

// Analyze one second plus the overlap for its last Hann window. All decoding
// and FFT work runs in a worker isolate, independent of the playback engine.
Future<Float32List?> readSpectrum(
  ({String path, int rate, int second}) job,
) async {
  try {
    final length = job.rate + _size;
    final pcm = await SoLoudIsolate.instance.readSamplesFromFile(
      job.path,
      length,
      startTime: job.second.toDouble(),
      endTime: job.second + length / job.rate,
    );
    return analyzeSpectrum(pcm, job.rate);
  } catch (_) {
    return null;
  }
}

Float32List analyzeSpectrum(Float32List pcm, int rate) {
  final result = Float32List(spectrumBands * framesPerSecond);
  final real = Float64List(_size), imag = Float64List(_size);
  final window = Float64List(_size), reversed = Int32List(_size);
  final bandOf = Int32List(_size ~/ 2);
  final upper = math.min(rate / 2, 20000);
  for (var i = 0; i < _size; i++) {
    window[i] = 0.5 - 0.5 * math.cos(2 * math.pi * i / (_size - 1));
    reversed[i] = (reversed[i >> 1] >> 1) | ((i & 1) * (_size >> 1));
  }
  for (var k = 1; k < bandOf.length; k++) {
    final hz = k * rate / _size;
    bandOf[k] = hz < 20 || hz > upper
        ? -1
        : math.min(
            spectrumBands - 1,
            (spectrumBands *
                    math.log((400 + hz) / 420) /
                    math.log((400 + upper) / 420))
                .floor(),
          );
  }
  for (var frame = 0; frame < framesPerSecond; frame++) {
    final offset = frame * rate ~/ framesPerSecond;
    for (var i = 0; i < _size; i++) {
      final j = reversed[i];
      real[j] = offset + i < pcm.length ? pcm[offset + i] * window[i] : 0;
      imag[j] = 0;
    }
    for (var width = 2; width <= _size; width *= 2) {
      final angle = -2 * math.pi / width;
      final c = math.cos(angle), s = math.sin(angle);
      for (var base = 0; base < _size; base += width) {
        var wr = 1.0, wi = 0.0;
        for (var j = base; j < base + width ~/ 2; j++) {
          final k = j + width ~/ 2;
          final x = wr * real[k] - wi * imag[k],
              y = wr * imag[k] + wi * real[k];
          real[k] = real[j] - x;
          imag[k] = imag[j] - y;
          real[j] += x;
          imag[j] += y;
          final next = wr * c - wi * s;
          wi = wr * s + wi * c;
          wr = next;
        }
      }
    }
    final first = frame * spectrumBands;
    for (var k = 1; k < bandOf.length; k++) {
      if (bandOf[k] < 0) continue;
      final i = first + bandOf[k];
      final power =
          (real[k] * real[k] + imag[k] * imag[k]) * 16 / (_size * _size);
      result[i] = math.max(result[i], power);
    }
    for (var i = first; i < first + spectrumBands; i++) {
      result[i] =
          (1 + 10 * math.log(math.max(1e-12, result[i])) / math.ln10 / 60)
              .clamp(0, 1);
    }
  }
  return result;
}
