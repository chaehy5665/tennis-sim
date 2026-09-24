using System.Security.Cryptography;
using TennisSim.Core;

namespace TennisSim.Cli;

// Reads recorded states only. No engine, RNG, interpolation fed back to Core, or record mutation.
public static class Diagnostics
{
    public sealed class Metric
    {
        public string Unit { get; set; } = "";
        public string Definition { get; set; } = "";
        public string Quality { get; set; } = "OBSERVED";
        public List<double> Values { get; } = new();
        public Dictionary<string, int> Excluded { get; } = new();
        public int Samples => Values.Count;
        public int Eligible => Samples + Excluded.Values.Sum();
        public double? ValidRatio => Eligible == 0 ? null : (double)Samples / Eligible;
        public string Status => Samples == 0 ? "NOT_MEASURABLE" : "NOT_APPLICABLE"; // descriptive distribution, no target population
        public double? Mean => Samples == 0 ? null : Values.Average();
        public double? P50 => Quantile(.5);
        public double? P90 => Quantile(.9);
        public double? P99 => Quantile(.99);
        public double? Max => Samples == 0 ? null : Values.Max();
        public double? Min => Samples == 0 ? null : Values.Min();
        double? Quantile(double p) => Samples == 0 ? null : Values.Order().ElementAt(Math.Max(0, (int)Math.Ceiling(p * Samples) - 1));
        public void Skip(string reason) => Excluded[reason] = Excluded.GetValueOrDefault(reason) + 1;
    }
    public sealed class CheckResult
    {
        public int Samples { get; set; }
        public int Failures { get; set; }
        public string Status => Samples == 0 ? "NOT_MEASURABLE" : Failures == 0 ? "PASS" : "FAIL";
    }
    public sealed class Report
    {
        public string SchemaVersion { get; set; } = "audit-1";
        public string TargetPopulation { get; set; } = "UNSPECIFIED";
        public string ReplayFile { get; set; } = "";
        public string ReplayHash { get; set; } = "";
        public string SourceId { get; set; } = "UNKNOWN: use producing run manifest";
        public string EngineVersion { get; set; } = "";
        public string ConfigHash { get; set; } = "";
        public uint Seed { get; set; }
        public Dictionary<string, Metric> Metrics { get; } = new();
        public Dictionary<string, CheckResult> Checks { get; } = new();
        public List<object> Issues { get; } = new();
        public List<object> Contacts { get; } = new();
        public List<object> Crossings { get; } = new();
        public List<object> Landings { get; } = new();
        public object? Match { get; set; }
        public string[] Limitations { get; set; } = {
            "Recorded contact time is tick-quantized (<= tickSeconds), not physical racket impact time.",
            "Player feet and horizontal reach cylinder, not capsule centres. Serve uses scripted height 2.65m, not rally reach rules.",
            "Sub-tick player event states are interpolated by Core; movement metrics use frames, exclude resets and stopped intervals.",
            "Frame movement distance is a lower bound; acceleration is interval mean delta-velocity/dt, not instantaneous peak.",
            "Apex is sampled lower bound; net crossings use collision-split linear interpolation, not exact collision observations.",
            "Net uses engine piecewise-linear height, finite width 5.029m each side and physical radius .0335m; no mesh dynamics.",
            "No ace/winner/unforced-error or empirical performance targets inferred. Missing collision/instantaneous acceleration cannot be recovered."
        };
    }
    public static MatchRecord Parse(string json)
    {
        try { return ParseObservations(json); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        { throw new ArgumentException("Invalid diagnostic observation structure: " + ex.Message, ex); }
    }
    private static MatchRecord ParseObservations(string json)
    {
        // ReplayJson DTO defaults are useful for inputs, but missing observations must not become zeros.
        using var document = System.Text.Json.JsonDocument.Parse(json);
        void Require(System.Text.Json.JsonElement value, params string[] names)
        {
            foreach (string name in names)
                if (!value.TryGetProperty(name, out _)) throw new ArgumentException("Missing diagnostic observation: " + name);
        }
        void State(System.Text.Json.JsonElement state)
        {
            Require(state, "time", "point", "phase", "ball", "players", "score");
            var ball = state.GetProperty("ball"); Require(ball, "position", "velocity", "bounces");
            foreach (string field in new[] { "position", "velocity" }) Require(ball.GetProperty(field), "x", "y", "z");
            foreach (var player in state.GetProperty("players").EnumerateArray())
            {
                Require(player, "id", "end", "position", "velocity");
                foreach (string field in new[] { "position", "velocity" }) Require(player.GetProperty(field), "x", "y", "z");
            }
            Require(state.GetProperty("score"), "points");
        }
        var root = document.RootElement;
        Require(root, "schemaVersion", "engineVersion", "input", "frames", "events", "stats", "finalScore", "status");
        var input = root.GetProperty("input"); Require(input, "seed", "config", "players");
        Require(input.GetProperty("config"), "tickSeconds", "gravity", "drag", "reach", "minContactHeight", "maxContactHeight");
        foreach (var p in input.GetProperty("players").EnumerateArray()) Require(p, "id", "maxSpeed", "acceleration");
        foreach (var frame in root.GetProperty("frames").EnumerateArray()) State(frame);
        foreach (var e in root.GetProperty("events").EnumerateArray())
        {
            Require(e, "sequence", "time", "point", "kind", "actionId", "shotKind", "playerId", "reason", "state", "before");
            State(e.GetProperty("state"));
            if (e.GetProperty("before").ValueKind != System.Text.Json.JsonValueKind.Null) State(e.GetProperty("before"));
        }
        Require(root.GetProperty("finalScore"), "games", "winner", "complete", "pointsPlayed", "display");
        var stats = root.GetProperty("stats"); Require(stats, "players", "rallyLengths", "endReasons");
        foreach (var p in stats.GetProperty("players").EnumerateArray()) Require(p, "playerId", "serveAttempts", "servesIn", "serveLets", "doubleFaults");
        var record = ReplayJson.Deserialize<MatchRecord>(json);
        if (record.SchemaVersion != "1.0") throw new ArgumentException("Unsupported replay schema");
        return record;
    }
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string Hash(string value) => Hash(System.Text.Encoding.UTF8.GetBytes(value));
    public static Report Analyze(MatchRecord r, string file, string hash, string sourceId)
    {
        if (r.EngineVersion is not ("tennissim-mvp-1" or "tennissim-mvp-2" or "tennissim-mvp-3" or "tennissim-mvp-4" or "tennissim-mvp-5" or "tennissim-mvp-6")) throw new ArgumentException("Unsupported diagnostic engine contract");
        r.Input.Config.Validate();
        foreach (var p in r.Input.Players) p.Validate();
        if (r.Frames.Where((f, i) => !double.IsFinite(f.Time) || f.Time < 0 || (i > 0 && f.Time < r.Frames[i - 1].Time)).Any()) throw new ArgumentException("Invalid frame time order");
        if (r.Events.Where((e, i) => e.Sequence != i || !double.IsFinite(e.Time) || e.State.Time != e.Time || (i > 0 && e.Time < r.Events[i - 1].Time)).Any()) throw new ArgumentException("Invalid event order/time");
        if (r.Events.Any(e => e.Kind is "BallHit" or "BallBounced" or "NetTouched" && e.Before == null)) throw new ArgumentException("Missing collision/contact Before");
        if (r.Frames.Concat(r.Events.Select(e => e.State)).Concat(r.Events.Where(e => e.Before != null).Select(e => e.Before!)).Any(s => !s.Ball.Position.IsFinite || !s.Ball.Velocity.IsFinite || s.Players.Length != 2 || s.Players.Any(p => !p.Position.IsFinite || !p.Velocity.IsFinite))) throw new ArgumentException("Invalid recorded state");
        var report = new Report { ReplayFile = file, ReplayHash = hash, SourceId = sourceId, Seed = r.Input.Seed, EngineVersion = r.EngineVersion, ConfigHash = Hash(ReplayJson.Serialize(r.Input.Config)) };
        var c = r.Input.Config;
        Metric M(string name, string unit, string definition, string quality = "OBSERVED")
        {
            if (!report.Metrics.TryGetValue(name, out var m)) report.Metrics[name] = m = new Metric { Unit = unit, Definition = definition, Quality = quality };
            return m;
        }
        void Check(string code, MatchEvent e, double value, double allowed, string unit, string basis, string quality = "OBSERVED")
        {
            if (!report.Checks.TryGetValue(code, out var check)) report.Checks[code] = check = new CheckResult();
            check.Samples++;
            if (value <= allowed) return;
            check.Failures++;
            report.Issues.Add(new { issueCode = code, severity = "ERROR", classification = "CORRECTNESS_BUG", engine = r.EngineVersion, sourceId, report.ConfigHash, seed = r.Input.Seed, pointId = e.Point, eventSequence = e.Sequence, simulationTime = e.Time, observedValue = value, allowedValueOrRange = allowed, unit, measurementQuality = quality, toleranceBasis = basis, ball = e.State.Ball, players = e.State.Players, replayFile = file, replayHash = hash, reproductionCommand = "diagnose --input " + file + " --out NEW_SIDECAR.json", seekLocation = new { point = e.Point, sequence = e.Sequence, time = e.Time } });
        }
        foreach (var code in new[] { "CONTACT_REACH", "CONTACT_HEIGHT", "BOUNCE_HEIGHT", "BOUNCE_SIGN", "BOUNCE_ENERGY", "PLAYER_SPEED", "PLAYER_ACCELERATION", "PLAYER_DISPLACEMENT", "BOUNCE_DUPLICATE", "GROUND_PENETRATION", "LINE_FOOTPRINT" }) report.Checks[code] = new();
        var hits = r.Events.Where(e => e.Kind == "BallHit").ToArray();
        foreach (string kind in new[] { "Serve", "Return", "Groundstroke" })
        {
            M(kind + ".launchSpeed", "m/s", "Length of BallHit.State.Ball.Velocity (all attempts, including faults/lets)");
            M(kind + ".flightToFirstBounce", "s", "First same-action bounce time minus hit; excludes missing first bounce");
            M(kind + ".sampledApex", "m", "Maximum recorded centre Y from hit through first bounce or next hit/end", "ESTIMATED_LOWER_BOUND");
        }
        for (int i = 0; i < hits.Length; i++)
        {
            var h = hits[i]; var b = h.State.Ball; var p = h.State.Players.Single(p => p.Id == h.PlayerId);
            M(h.ShotKind + ".launchSpeed", "m/s", "").Values.Add(b.Velocity.Length);
            double distance = Vec3.GroundDistance(p.Position, b.Position);
            report.Contacts.Add(new { pointId = h.Point, eventSequence = h.Sequence, simulationTime = h.Time, h.PlayerId, h.ShotKind, position = b.Position, outgoingVelocity = b.Velocity, feet = p.Position, horizontalDistance = distance, reachMargin = h.ShotKind == "Serve" ? (double?)null : c.Reach - distance, quality = "OBSERVED_TICK_CONTACT" });
            if (h.ShotKind != "Serve")
            {
                M("contact.reachMargin", "m", "Reach minus horizontal ball-centre to feet distance; non-serves").Values.Add(c.Reach - distance);
                M("contact.heightMargin", "m", "Minimum margin to rally min/max centre height; non-serves").Values.Add(Math.Min(b.Position.Y - c.MinContactHeight, c.MaxContactHeight - b.Position.Y));
                Check("CONTACT_REACH", h, distance, c.Reach + 1e-8, "m", "same tick contact, 1e-8 numerical allowance, no speed*dt enlargement");
                Check("CONTACT_HEIGHT", h, Math.Max(c.MinContactHeight - b.Position.Y, b.Position.Y - c.MaxContactHeight), 1e-8, "m", "same tick bounds + 1e-8m");
            }
            var bounce = r.Events.FirstOrDefault(e => e.Sequence > h.Sequence && e.ActionId == h.ActionId && e.Kind == "BallBounced" && e.State.Ball.Bounces == 1);
            var flight = M(h.ShotKind + ".flightToFirstBounce", "s", "");
            if (bounce == null) flight.Skip("No first bounce recorded"); else
            {
                flight.Values.Add(bounce.Time - h.Time);
                report.Landings.Add(new { h.Point, h.ActionId, h.ShotKind, h.PlayerId, h.IntendedTarget, actual = bounce.State.Ball.Position, bounce.Reason, bounce.Time });
                if (h.IntendedTarget is Vec3 target)
                {
                    M(h.ShotKind + ".targetX", "m", "Intended X for paired first landings").Values.Add(target.X);
                    M(h.ShotKind + ".landingX", "m", "Actual first landing X, includes out/fault/let").Values.Add(bounce.State.Ball.Position.X);
                    M(h.ShotKind + ".targetZ", "m", "Intended Z for paired first landings").Values.Add(target.Z);
                    M(h.ShotKind + ".landingZ", "m", "Actual first landing Z").Values.Add(bounce.State.Ball.Position.Z);
                    M(h.ShotKind + ".landingError", "m", "Horizontal distance intended vs actual first landing; not selection rate").Values.Add(Vec3.GroundDistance(target, bounce.State.Ball.Position));
                }
            }
            double end = bounce?.Time ?? (i + 1 < hits.Length && hits[i + 1].Point == h.Point ? hits[i + 1].Time : r.Events.FirstOrDefault(e => e.Kind == "PointEnded" && e.Point == h.Point)?.Time ?? h.Time);
            double apex = r.Frames.Where(f => f.Point == h.Point && f.Time >= h.Time && f.Time <= end).Select(f => f.Ball.Position.Y).Concat(r.Events.Where(e => e.Point == h.Point && e.Time >= h.Time && e.Time <= end && e.Sequence >= h.Sequence).Select(e => e.State.Ball.Position.Y)).Append(b.Position.Y).Max();
            M(h.ShotKind + ".sampledApex", "m", "").Values.Add(apex);
        }
        var hitByAction = hits.ToDictionary(h => h.ActionId);
        var bounceKeys = new HashSet<(int action, double time)>();
        foreach (var e in r.Events.Where(e => e.Kind == "BallBounced"))
        {
            var before = e.Before!.Ball; var after = e.State.Ball;
            Check("BOUNCE_DUPLICATE", e, bounceKeys.Add((e.ActionId, e.Time)) ? 0 : 1, 0, "events", "same action and exact event time; no tolerance merging distinct impacts");
            if (hitByAction.TryGetValue(e.ActionId, out var origin))
            {
                M(origin.ShotKind + ".bounceBeforeSpeed", "m/s", "Collision Before velocity magnitude, grouped by initiating shot").Values.Add(before.Velocity.Length);
                M(origin.ShotKind + ".bounceAfterSpeed", "m/s", "Collision State velocity magnitude, grouped by initiating shot").Values.Add(after.Velocity.Length);
                if (after.Bounces == 1 && e.Reason is "In" or "ServiceIn" or "Let")
                {
                    int end = origin.State.Players.Single(p => p.Id == origin.PlayerId).End;
                    bool deuce = origin.State.Score.Points.Sum() % 2 == 0;
                    double x = origin.ShotKind == "Serve" ? after.Position.X * (deuce ? end : -end) : after.Position.X;
                    double z = -end * after.Position.Z;
                    double dx = Math.Max(0, Math.Max((origin.ShotKind == "Serve" ? 0 : -Court.HalfWidth) - x, x - Court.HalfWidth));
                    double dz = Math.Max(0, Math.Max(-z, z - (origin.ShotKind == "Serve" ? Court.ServiceLine : Court.HalfLength)));
                    Check("LINE_FOOTPRINT", e, Math.Sqrt(dx * dx + dz * dz), Court.BallRadius + 1e-8, "m", "independent nearest point to rectangle; finite circular footprint assumption; 1e-8m coordinate roundoff");
                }
            }
            M("bounce.beforeSpeed", "m/s", "Collision Before velocity magnitude").Values.Add(before.Velocity.Length);
            M("bounce.afterSpeed", "m/s", "Collision State velocity magnitude").Values.Add(after.Velocity.Length);
            Check("BOUNCE_HEIGHT", e, Math.Abs(after.Position.Y - Court.BallRadius), 1e-8, "m", "physical radius .0335m, 40 bisections and 1e-8m roundoff");
            Check("BOUNCE_SIGN", e, Math.Max(before.Velocity.Y, -after.Velocity.Y), 1e-8, "m/s", "incoming nonpositive, outgoing nonnegative");
            Check("BOUNCE_ENERGY", e, .5 * (after.Velocity.Length * after.Velocity.Length - before.Velocity.Length * before.Velocity.Length), 1e-7, "J/kg", "passive bounce at same height; floating roundoff only");
        }
        foreach (var e in r.Events.Where(e => e.State.Phase == "Rally"))
            Check("GROUND_PENETRATION", e, Court.BallRadius - e.State.Ball.Position.Y, 1e-8, "m", "recorded centre below physical radius; 40 collision bisections, 1e-8m roundoff");
        // Frames only: sub-tick event player states are interpolated, not independent movement observations.
        var frames = r.Frames.OrderBy(f => f.Time).ToArray();
        for (int i = 1; i < frames.Length; i++)
        {
            var a = frames[i - 1]; var b = frames[i]; double dt = b.Time - a.Time;
            bool boundary = r.Events.Any(e => e.Time >= a.Time && e.Time <= b.Time && e.Kind is "PlayersRepositioned" or "PointEnded" or "ServeFault" or "ServeLet");
            for (int p = 0; p < 2; p++)
            {
                var profile = r.Input.Players[p]; string prefix = profile.Id + ".";
                var distance = M(prefix + "movementDistance", "m", "Sum values for in-play sampled chord distance; resets excluded", "ESTIMATED_LOWER_BOUND");
                var speed = M(prefix + "speed", "m/s", "Recorded frame velocity magnitude in eligible rally intervals");
                var acceleration = M(prefix + "acceleration", "m/s2", "Magnitude of delta recorded velocity / dt; interval mean", "ESTIMATED_INTERVAL_MEAN");
                var turn = M(prefix + "directionChange", "degrees", "Angle between nonzero frame velocity vectors", "ESTIMATED");
                string? reason = dt <= 0 ? "dt<=0" : boundary || a.Point != b.Point ? "reset/point/serve boundary" : a.Phase != "Rally" || b.Phase != "Rally" ? "not continuously in play" : null;
                if (reason != null) { foreach (var m in new[] { distance, speed, acceleration, turn }) m.Skip(reason); continue; }
                var x = a.Players[p]; var y = b.Players[p]; double d = Vec3.GroundDistance(x.Position, y.Position), v = y.Velocity.GroundLength, acc = (y.Velocity - x.Velocity).GroundLength / dt;
                distance.Values.Add(d); speed.Values.Add(v); acceleration.Values.Add(acc);
                if (x.Velocity.GroundLength < 1e-8 || v < 1e-8) turn.Skip("zero speed; direction undefined");
                else turn.Values.Add(Math.Acos(Math.Clamp((x.Velocity.X * y.Velocity.X + x.Velocity.Z * y.Velocity.Z) / (x.Velocity.GroundLength * v), -1, 1)) * 180 / Math.PI);
                var location = new MatchEvent { Sequence = r.Events.LastOrDefault(e => e.Time <= b.Time)?.Sequence ?? -1, Point = b.Point, Time = b.Time, State = b };
                Check("PLAYER_SPEED", location, v, profile.MaxSpeed + 1e-8, "m/s", "profile hard maximum; energy-dependent soft cap not instantaneous");
                Check("PLAYER_ACCELERATION", location, acc, profile.Acceleration + 1e-7, "m/s2", "frame interval mean; double timestamp subtraction", "ESTIMATED_INTERVAL_MEAN");
                Check("PLAYER_DISPLACEMENT", location, d, profile.MaxSpeed * dt + 1e-8, "m", "hard speed*elapsed time + 1e-8m; reset intervals excluded", "ESTIMATED_LOWER_BOUND");
            }
        }
        // Merge with the viewer's same-time precedence and split ALL impulse boundaries.
        var entries = r.Frames.Select((s, i) => (time: s.Time, order: (long)i, state: s, ev: (MatchEvent?)null))
            .Concat(r.Events.Select(e => (time: e.Time, order: r.Frames.Count + e.Sequence, state: e.State, ev: (MatchEvent?)e)))
            .OrderBy(x => x.time).ThenBy(x => x.order).GroupBy(x => x.time).ToArray();
        var clearance = M("net.clearance", "m", "Interpolated centre Y minus physical radius minus piecewise net height, inside net span", "ESTIMATED");
        for (int i = 1; i < entries.Length; i++)
        {
            var left = entries[i - 1].Last().state; var group = entries[i].ToArray(); var right = group.FirstOrDefault(x => x.ev?.Before != null).ev?.Before ?? group[0].state;
            if (left.Phase != "Rally" || left.Point != right.Point || group.Any(x => x.ev?.Kind == "PlayersRepositioned")) continue;
            var a = left.Ball.Position; var b = right.Ball.Position; double dt = right.Time - left.Time;
            if (dt <= 0 || a.Z * b.Z > 0 || Math.Abs(b.Z - a.Z) < 1e-10 || Math.Abs(a.Z) < 1e-10) continue;
            double t = -a.Z / (b.Z - a.Z); var pos = Vec3.Lerp(a, b, t);
            if (Math.Abs(pos.X) > 5.029 + Court.BallRadius) { clearance.Skip("outside net span: around-post flight"); continue; }
            double value = pos.Y - Court.BallRadius - Court.NetHeight(pos.X);
            // Linear-interpolation error bound a_max*dt^2/8; profile Lipschitz slope includes X error.
            double vmax = Math.Max(left.Ball.Velocity.Length, right.Ball.Velocity.Length);
            double error = (c.Gravity + c.Drag * vmax) * dt * dt / 8 * (1 + .156 / 5.029) + 1e-8;
            clearance.Values.Add(value);
            report.Crossings.Add(new { pointId = left.Point, simulationTime = left.Time + t * dt, position = pos, clearance = value, measurementQuality = "ESTIMATED", errorBoundMetres = error, status = "NOT_APPLICABLE", reason = "negative clearance may be recorded net contact; no collision claim from interpolation" });
        }
        M("instantaneousAcceleration", "m/s2", "NOT_MEASURABLE: sparse replay does not preserve per-tick acceleration").Skip("requires per-tick observation");
        M("physicalContactTime", "s", "NOT_MEASURABLE: racket collision model absent; recorded contact is tick decision").Skip("racket model absent");
        var duration = M("point.inPlayDuration", "s", "Sum Serve BallHit to fault/let/PointEnded intervals per completed point; excludes .6s preparations, includes failed/let flight");
        foreach (var point in r.Events.GroupBy(e => e.Point))
        {
            double total = 0; double? start = null;
            foreach (var e in point) { if (e.Kind == "BallHit" && e.ShotKind == "Serve") start = e.Time; if (e.Kind is "ServeFault" or "ServeLet" or "PointEnded" && start.HasValue) { total += e.Time - start.Value; start = null; } }
            if (point.Any(e => e.Kind == "PointEnded")) duration.Values.Add(total); else duration.Skip("incomplete point");
        }
        var rally = M("point.rallyLength", "hits", "Successful rally hits including legal serve; failed serves and lets excluded (Core stats)");
        rally.Values.AddRange(r.Stats.RallyLengths.Select(x => (double)x));
        report.Match = new { r.Status, r.FinalScore, r.Stats.EndReasons, serves = r.Stats.Players.Select(p => new { p.PlayerId, attempts = p.ServeAttempts, legal = p.ServesIn, lets = p.ServeLets, faults = r.Events.Count(e => e.Kind == "ServeFault" && e.PlayerId == p.PlayerId), p.DoubleFaults, legalFractionAllAttempts = p.ServeAttempts == 0 ? (double?)null : (double)p.ServesIn / p.ServeAttempts, legalFractionExcludingLets = p.ServeAttempts == p.ServeLets ? (double?)null : (double)p.ServesIn / (p.ServeAttempts - p.ServeLets) }) };
        return report;
    }
}
