using System;
using System.Collections.Generic;
using System.Linq;

namespace TennisSim.Viewer
{
    public sealed class ReplayTimeline
    {
        public sealed class Key
        {
            public double Time;
            public ReplayState Before, After;
            public bool Reset;
        }
        public ReplayData Data { get; }
        public Key[] Keys { get; }
        public double Start => Keys[0].Time;
        public double Duration => Keys[Keys.Length - 1].Time;
        public int TotalPoints { get; }
        public ReplayTimeline(ReplayData data)
        {
            ReplayValidator.Validate(data); Data = data;
            // Frames at a tick boundary were emitted before events at the next tick.
            // Stable ordering: frame(s), then domain events in original sequence.
            var entries = data.Frames.Select((f, i) => new Entry { Time = f.Time, Order = i, State = f })
                .Concat(data.Events.Select(e => new Entry { Time = e.Time, Order = data.Frames.Length + e.Sequence, State = e.State, Event = e }))
                .OrderBy(x => x.Time).ThenBy(x => x.Order);
            var keys = new List<Key>();
            foreach (var group in entries.GroupBy(x => x.Time))
            {
                var list = group.ToArray();
                // Before of earliest boundary at this time preserves contact approach.
                var boundary = list.FirstOrDefault(x => x.Event != null && x.Event.Before != null);
                keys.Add(new Key { Time = group.Key, Before = boundary == null ? list[0].State : boundary.Event.Before, After = list[list.Length - 1].State, Reset = list.Any(x => x.Event != null && x.Event.Kind == "PlayersRepositioned") });
            }
            Keys = keys.ToArray(); TotalPoints = data.Events.Count(e => e.Kind == "PointStarted");
        }
        sealed class Entry { public double Time; public int Order; public ReplayState State; public ReplayEvent Event; }
        public double Clamp(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time)) throw new ArgumentOutOfRangeException(nameof(time));
            return Math.Max(Start, Math.Min(Duration, time));
        }
        public int LastEventIndex(double time)
        {
            int lo = 0, hi = Data.Events.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (Data.Events[mid].Time <= time) lo = mid + 1; else hi = mid; }
            return lo - 1;
        }
        // Forward interval is (from, to]. includeStart is used exactly once after restart.
        public ReplayEvent[] EventsBetween(double from, double to, bool includeStart = false)
        {
            if (to < from) return new ReplayEvent[0];
            int first = LastEventIndex(from) + 1;
            if (includeStart) while (first > 0 && Data.Events[first - 1].Time == from) first--;
            int last = LastEventIndex(to); var result = new ReplayEvent[Math.Max(0, last - first + 1)];
            Array.Copy(Data.Events, first, result, 0, result.Length); return result;
        }
    }
    public sealed class ReplayStateSampler
    {
        public ReplayTimeline Timeline { get; }
        public ReplayStateSampler(ReplayTimeline timeline) { Timeline = timeline; }
        public ReplayState Sample(double requestedTime)
        {
            double t = Timeline.Clamp(requestedTime); var keys = Timeline.Keys;
            int lo = 0, hi = keys.Length;
            while (lo < hi) { int mid = lo + (hi - lo) / 2; if (keys[mid].Time <= t) lo = mid + 1; else hi = mid; }
            int i = Math.Max(0, lo - 1); var a = keys[i].After;
            if (i == keys.Length - 1 || t == keys[i].Time) return Copy(a, t);
            var next = keys[i + 1];
            if (next.Reset || a.Point != next.Before.Point || a.Phase == "BetweenPoints" || a.Phase == "ServeRetry") return Copy(a, t);
            double fraction = (t - keys[i].Time) / (next.Time - keys[i].Time);
            var result = Copy(a, t); result.Ball = Position.Lerp(a.Ball, next.Before.Ball, fraction); result.A = Position.Lerp(a.A, next.Before.A, fraction); result.B = Position.Lerp(a.B, next.Before.B, fraction);
            return result;
        }
        static ReplayState Copy(ReplayState s, double time) => new ReplayState { Time = time, Point = s.Point, Phase = s.Phase, Score = s.Score, Ball = s.Ball, A = s.A, B = s.B };
    }
    public sealed class ReplayController
    {
        public ReplayTimeline Timeline { get; }
        public ReplayStateSampler Sampler { get; }
        public double Time { get; private set; }
        public double Speed { get; private set; } = 1;
        public bool Playing { get; private set; }
        public int SelectedEvent { get; private set; } = -1;
        public ReplayEvent[] CrossedEvents { get; private set; } = new ReplayEvent[0];
        bool includeStart;
        public ReplayController(ReplayTimeline timeline) { Timeline = timeline; Sampler = new ReplayStateSampler(timeline); Restart(); }
        public ReplayState State => Sampler.Sample(Time);
        public void SetPlaying(bool value) { Playing = value && Time < Timeline.Duration; }
        public void SetSpeed(double speed) { if (speed != .25 && speed != 1 && speed != 2) throw new ArgumentOutOfRangeException(nameof(speed)); Speed = speed; }
        public void Restart() { Time = Timeline.Start; Playing = false; includeStart = true; SelectedEvent = -1; CrossedEvents = new ReplayEvent[0]; }
        public void Seek(double time) { Time = Timeline.Clamp(time); Playing = false; includeStart = false; SelectedEvent = -1; CrossedEvents = new ReplayEvent[0]; }
        public void StepEvent(int direction)
        {
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException(nameof(direction));
            int index = SelectedEvent;
            if (index < 0) { index = Timeline.LastEventIndex(Time); if (direction < 0 && index >= 0 && Timeline.Data.Events[index].Time < Time) index++; }
            index = Math.Max(0, Math.Min(Timeline.Data.Events.Length - 1, index + direction));
            Seek(Timeline.Data.Events[index].Time); SelectedEvent = index;
        }
        public void Advance(double realSeconds)
        {
            if (double.IsNaN(realSeconds) || double.IsInfinity(realSeconds) || realSeconds < 0) throw new ArgumentOutOfRangeException(nameof(realSeconds));
            CrossedEvents = new ReplayEvent[0]; if (!Playing) return;
            double next = Timeline.Clamp(Time + realSeconds * Speed);
            CrossedEvents = Timeline.EventsBetween(Time, next, includeStart); includeStart = false; SelectedEvent = -1; Time = next;
            if (Time == Timeline.Duration) Playing = false;
        }
    }
}
