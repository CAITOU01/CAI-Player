import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart' show compute;

import 'spectrum.dart';

class NowPlaying extends StatelessWidget {
  final String title;
  final bool expanded;
  final Uint8List? cover;
  final String? path;
  final int? rate;
  final Stream<Duration> positions;
  final Duration position;
  final VoidCallback onToggle;

  const NowPlaying({
    super.key,
    required this.title,
    required this.expanded,
    required this.cover,
    required this.path,
    required this.rate,
    required this.positions,
    required this.position,
    required this.onToggle,
  });

  @override
  Widget build(BuildContext context) {
    final palette = Theme.of(context).colorScheme;
    final fallback = Icon(
      Icons.album_outlined,
      size: expanded ? 80 : 30,
      color: palette.primary,
    );
    final art = ColoredBox(
      color: palette.surfaceContainerHighest,
      child: cover == null
          ? Center(child: fallback)
          : Image.memory(
              cover!,
              fit: BoxFit.contain,
              cacheWidth: expanded ? 640 : 96,
              errorBuilder: (_, __, ___) => Center(child: fallback),
            ),
    );
    final name = Text(
      title,
      maxLines: expanded ? 2 : 1,
      overflow: TextOverflow.ellipsis,
      style: expanded
          ? Theme.of(context).textTheme.headlineMedium
          : Theme.of(context).textTheme.titleSmall,
    );
    final chart = _SpectrumChart(
      key: ValueKey((path, rate)),
      path: path,
      rate: rate,
      positions: positions,
      initial: position,
      expanded: expanded,
    );
    if (!expanded) {
      return Material(
        color: palette.surfaceContainer,
        child: InkWell(
          onTap: onToggle,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
            child: Row(
              children: [
                SizedBox.square(dimension: 42, child: art),
                const SizedBox(width: 12),
                Expanded(child: name),
                const SizedBox(width: 8),
                SizedBox(width: 80, height: 30, child: chart),
                const Icon(Icons.open_in_full, size: 16),
              ],
            ),
          ),
        ),
      );
    }
    return LayoutBuilder(
      builder: (ctx, bounds) {
        final wide =
            bounds.maxWidth > 650 && bounds.maxWidth > bounds.maxHeight;
        final side = math.max(
          0.0,
          math.min(
            430.0,
            math.min(
              bounds.maxWidth * (wide ? .38 : .76),
              bounds.maxHeight * (wide ? .8 : .54),
            ),
          ),
        );
        final sleeve = SizedBox.square(dimension: side, child: art);
        final details = Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            name,
            const SizedBox(height: 12),
            SizedBox(
              height: wide ? 84 : 48,
              width: double.infinity,
              child: chart,
            ),
          ],
        );
        return Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: wide
                ? Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      sleeve,
                      const SizedBox(width: 30),
                      Flexible(
                        child: ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 480),
                          child: details,
                        ),
                      ),
                    ],
                  )
                : Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [sleeve, const SizedBox(height: 16), details],
                  ),
          ),
        );
      },
    );
  }
}

class _SpectrumChart extends StatefulWidget {
  final String? path;
  final int? rate;
  final Stream<Duration> positions;
  final Duration initial;
  final bool expanded;
  const _SpectrumChart({
    super.key,
    required this.path,
    required this.rate,
    required this.positions,
    required this.initial,
    required this.expanded,
  });
  @override
  State<_SpectrumChart> createState() => _SpectrumChartState();
}

class _SpectrumChartState extends State<_SpectrumChart> {
  final blocks = <int, Future<Float32List?>>{};
  Float32List? lastData;
  int lastSecond = -1;
  int failures = 0;
  bool unavailable = false;

  Future<Float32List?> load(int second) async {
    final data = await compute(readSpectrum, (
      path: widget.path!,
      rate: widget.rate!,
      second: second,
    ));
    if (mounted) {
      if (data == null) {
        failures++;
        if (failures >= 3 && lastData == null) setState(() => unavailable = true);
      } else {
        failures = 0;
        lastData = data;
        lastSecond = second;
        if (unavailable) setState(() => unavailable = false);
      }
    }
    return data;
  }

  @override
  Widget build(BuildContext context) {
    final rate = widget.rate;
    final palette = Theme.of(context).colorScheme;
    if (unavailable ||
        widget.path == null ||
        rate == null ||
        rate < 1000 ||
        rate > 384000) {
      return Center(
        child: widget.expanded
            ? const Text('無法取得頻譜')
            : const Icon(Icons.graphic_eq, size: 16, semanticLabel: '無法取得頻譜'),
      );
    }
    return StreamBuilder<Duration>(
      stream: widget.positions,
      initialData: widget.initial,
      builder: (context, snapshot) {
        final ms = math.max(
          0,
          (snapshot.data ?? widget.initial).inMilliseconds,
        );
        final second = ms ~/ 1000;
        blocks.removeWhere((key, _) => key < second - 1 || key > second + 2);
        final future = blocks.putIfAbsent(second, () => load(second));
        blocks.putIfAbsent(second + 1, () => load(second + 1));
        return FutureBuilder<Float32List?>(
          key: ValueKey(second),
          future: future,
          builder: (context, block) => Semantics(
            label: '音訊頻譜，頻段 20 Hz 到 20 kHz',
            child: CustomPaint(
              painter: SpectrumPainter(
                block.data ?? lastData,
                block.data != null
                    ? (ms % 1000) * framesPerSecond ~/ 1000
                    : lastSecond < second
                    ? framesPerSecond - 1
                    : 0,
                palette.primary,
                palette.outlineVariant,
                widget.expanded ? spectrumBands : 26,
                widget.expanded,
              ),
            ),
          ),
        );
      },
    );
  }
}

class SpectrumPainter extends CustomPainter {
  final Float32List? data;
  final int frame, count;
  final Color color, baseline;
  final bool showScale;
  SpectrumPainter(
    this.data,
    this.frame,
    this.color,
    this.baseline,
    this.count,
    this.showScale,
  );

  @override
  void paint(Canvas canvas, Size size) {
    final bottom = size.height - (showScale ? 18 : 1);
    canvas.drawLine(
      Offset(0, bottom),
      Offset(size.width, bottom),
      Paint()..color = baseline,
    );
    final bands = data;
    if (bands == null) {
      if (showScale) _drawScale(canvas, size, bottom);
      return;
    }
    final step = size.width / count;
    final paint = Paint()..color = color;
    for (int i = 0; i < count; i++) {
      var peak = 0.0;
      for (
        var b = i * spectrumBands ~/ count;
        b < (i + 1) * spectrumBands ~/ count;
        b++
      ) {
        peak = math.max(peak, bands[frame * spectrumBands + b]);
      }
      canvas.drawRect(
        Rect.fromLTRB(i * step, bottom * (1 - peak), (i + .75) * step, bottom),
        paint,
      );
    }
    if (showScale) _drawScale(canvas, size, bottom);
  }

  void _drawScale(Canvas canvas, Size size, double top) {
    const frequencies = [20, 250, 1000, 4000, 10000, 20000];
    const labels = ['20 Hz', '250 Hz', '1 kHz', '4 kHz', '10 kHz', '20 kHz'];
    final span = math.log((400 + 20000) / 420);
    for (var i = 0; i < frequencies.length; i++) {
      final ratio = math.log((400 + frequencies[i]) / 420) / span;
      final painter = TextPainter(
        text: TextSpan(
          text: labels[i],
          style: TextStyle(color: baseline, fontSize: 9),
        ),
        textDirection: TextDirection.ltr,
      )..layout();
      final x = (size.width * ratio - painter.width / 2).clamp(
        0.0,
        math.max(0.0, size.width - painter.width),
      ).toDouble();
      painter.paint(canvas, Offset(x, top + 3));
    }
  }

  @override
  bool shouldRepaint(SpectrumPainter old) =>
      old.data != data ||
      old.frame != frame ||
      old.count != count ||
      old.showScale != showScale ||
      old.color != color ||
      old.baseline != baseline;
}
