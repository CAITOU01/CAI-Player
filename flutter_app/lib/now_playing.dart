import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';

class NowPlaying extends StatelessWidget {
  final String title;
  final bool expanded;
  final Uint8List? cover;
  final Float32List? samples;
  final Duration length;
  final Stream<Duration> positions;
  final Duration position;
  final VoidCallback onToggle;

  const NowPlaying({
    super.key,
    required this.title,
    required this.expanded,
    required this.cover,
    required this.samples,
    required this.length,
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
    final chart = StreamBuilder<Duration>(
      stream: positions,
      initialData: position,
      builder: (ctx, snapshot) => CustomPaint(
        painter: EnvelopePainter(
          samples,
          snapshot.data ?? position,
          length,
          palette.primary,
          palette.outlineVariant,
          expanded ? 64 : 26,
        ),
      ),
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

class EnvelopePainter extends CustomPainter {
  final Float32List? samples;
  final Duration position, duration;
  final Color color, baseline;
  final int count;
  EnvelopePainter(
    this.samples,
    this.position,
    this.duration,
    this.color,
    this.baseline,
    this.count,
  );

  @override
  void paint(Canvas canvas, Size size) {
    final mid = size.height / 2;
    canvas.drawLine(
      Offset(0, mid),
      Offset(size.width, mid),
      Paint()..color = baseline,
    );
    final data = samples;
    if (data == null || data.isEmpty || duration.inMilliseconds <= 0) return;
    final last =
        (position.inMilliseconds / duration.inMilliseconds * data.length)
            .floor()
            .clamp(0, data.length - 1);
    final step = size.width / count;
    final paint = Paint()..color = color;
    for (int i = 0; i < count; i++) {
      final index = last - count + i + 1;
      if (index < 0) continue;
      final h = data[index].abs().clamp(0.0, 1.0) * mid;
      canvas.drawRect(
        Rect.fromLTRB(i * step, mid - h, (i + .75) * step, mid + h),
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(EnvelopePainter old) =>
      old.samples != samples ||
      old.position != position ||
      old.duration != duration ||
      old.color != color ||
      old.count != count ||
      old.baseline != baseline;
}
