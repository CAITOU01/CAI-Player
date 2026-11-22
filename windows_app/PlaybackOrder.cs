using System;
using System.Collections.Generic;

namespace CaiPlayer
{
    internal enum RepeatMode { Off, One, All }

    internal sealed class PlaybackOrder
    {
        private readonly Random random = new Random();
        private readonly List<Track> history = new List<Track>();
        private readonly HashSet<Track> playedThisRound = new HashSet<Track>();
        private int position = -1;
        public bool Shuffle { get; private set; }
        public RepeatMode Repeat { get; set; }

        public void Reset(Track current)
        {
            history.Clear();
            playedThisRound.Clear();
            position = -1;
            if (Shuffle && current != null) Select(current);
        }

        public void SetShuffle(bool enabled, Track current)
        {
            Shuffle = enabled;
            Reset(current);
        }

        // An explicit song selection starts a new forward path from this position.
        public void Select(Track track)
        {
            if (!Shuffle || (position >= 0 && history[position] == track)) return;
            history.RemoveRange(position + 1, history.Count - position - 1);
            history.Add(track);
            playedThisRound.Add(track);
            position++;
        }

        public void Forget(Track track)
        {
            playedThisRound.Remove(track);
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i] != track) continue;
                history.RemoveAt(i);
                if (i <= position) position--;
            }
        }

        public Track Begin(List<Track> tracks, Track selected)
        {
            Reset(null);
            if (tracks.Count == 0) return null;
            var first = Shuffle ? tracks[random.Next(tracks.Count)] : selected ?? tracks[0];
            Select(first);
            return first;
        }

        public Track Next(List<Track> tracks, Track current, bool finished)
        {
            if (current == null || tracks.Count == 0) return null;
            if (finished && Repeat == RepeatMode.One) return current;
            if (!Shuffle)
            {
                int next = tracks.IndexOf(current) + 1;
                return next < tracks.Count ? tracks[next] : Repeat == RepeatMode.All ? tracks[0] : null;
            }
            if (position + 1 < history.Count) return history[++position];

            var candidates = tracks.FindAll(t => !playedThisRound.Contains(t));
            if (candidates.Count == 0)
            {
                if (Repeat != RepeatMode.All) return null;
                if (tracks.Count == 1) return current;
                playedThisRound.Clear();
                candidates = tracks.FindAll(t => t != current);
                // Keep the preceding round available to the Previous button.
                int discard = history.Count - tracks.Count;
                if (discard > 0)
                {
                    history.RemoveRange(0, discard);
                    position -= discard;
                }
            }
            var nextTrack = candidates[random.Next(candidates.Count)];
            Select(nextTrack);
            return nextTrack;
        }

        public Track Previous(List<Track> tracks, Track current)
        {
            if (Shuffle) return position > 0 ? history[--position] : null;
            int index = tracks.IndexOf(current);
            if (index > 0) return tracks[index - 1];
            return index == 0 && Repeat == RepeatMode.All ? tracks[tracks.Count - 1] : null;
        }
    }
}
