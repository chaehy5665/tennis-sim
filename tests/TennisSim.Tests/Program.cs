using TennisSim.Core;
using TennisSim.Cli;
using TennisSim.Tests;

int passed = 0, failed = 0;
void Test(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double a, double b, double eps = 1e-7) => Check(Math.Abs(a - b) <= eps, $"{a} != {b}");
void Game(Scoring s, int winner) { for (int i = 0; i < 4; i++) s.Award(winner); }
Scoring TieBreak()
{
    var s = new Scoring(); for (int i = 0; i < 12; i++) Game(s, i % 2); return s;
}
MatchRecord Run(uint seed, int chunk = 4096, MatchInput? input = null)
{
    input ??= new MatchInput(); input.Seed = seed;
    var e = new MatchEngine(input); while (!e.Finished) e.AdvanceTicks(chunk); return e.Record;
}
var config = new SimConfig();

Test("Deuce, advantage, deuce, game", () =>
{
    var s = new Scoring(); for (int i = 0; i < 3; i++) { s.Award(0); s.Award(1); }
    Check(s.Snapshot().Display == "0-0 Deuce"); s.Award(0); Check(s.Snapshot().Display.Contains("Advantage A"));
    s.Award(1); Check(s.Snapshot().Display == "0-0 Deuce"); s.Award(1); s.Award(1); Check(s.Snapshot().Games[1] == 1);
});
Test("6-5 does not finish; 7-5 does", () =>
{
    var s = new Scoring(); for (int i = 0; i < 10; i++) Game(s, i % 2); Game(s, 0); Check(!s.Complete); Game(s, 0); Check(s.Complete && s.Winner == 0);
});
Test("6-6 enters tiebreak; seven requires two point lead", () =>
{
    var s = TieBreak(); Check(s.TieBreak && !s.Complete);
    for (int i = 0; i < 6; i++) { s.Award(0); s.Award(1); }
    s.Award(0); Check(!s.Complete); s.Award(1); s.Award(0); Check(!s.Complete); s.Award(0);
    Check(s.Complete && s.Snapshot().Games.SequenceEqual(new[] { 7, 6 }));
});
Test("Tiebreak server 1-2-2 sequence and six-point ends", () =>
{
    var s = TieBreak(); int end = s.EndA; int[] expected = { 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0 };
    for (int i = 0; i < expected.Length; i++) { Check(s.Server == expected[i], "Server at " + i); s.Award(i % 2); if (i == 5) Check(s.EndA == -end); }
    Check(s.EndA == end);
});
Test("Normal server, deuce side and odd-game end changes", () =>
{
    var s = new Scoring(); Check(s.DeuceSide); s.Award(0); Check(!s.DeuceSide); s.Award(0); Check(s.DeuceSide); s.Award(0); s.Award(0);
    Check(s.Server == 1 && s.EndA == 1 && s.DeuceSide); Game(s, 1); Check(s.Server == 0 && s.EndA == 1); Game(s, 0); Check(s.EndA == -1);
});
Test("First fault, second let, still second serve, double fault", () =>
{
    var s = new ServeRules(); Check(s.Resolve(false, false) == ServeDecision.Fault); Check(s.Attempt == 2);
    Check(s.Resolve(true, true) == ServeDecision.Let); Check(s.Attempt == 2); Check(s.Resolve(false, true) == ServeDecision.DoubleFault);
    var first = new ServeRules(); Check(first.Resolve(true, true) == ServeDecision.Let && first.Attempt == 1); Check(first.Resolve(true, false) == ServeDecision.In);
});
Test("Service diagonal changes with both court end and deuce side", () =>
{
    foreach (int end in new[] { -1, 1 }) foreach (bool deuce in new[] { false, true })
    {
        int side = deuce ? end : -end; Check(Court.ServiceIn(new Vec3(side * 2, 0, -end * 4), end, deuce));
        Check(!Court.ServiceIn(new Vec3(-side * 2, 0, -end * 4), end, deuce));
        Check(!Court.ServiceIn(new Vec3(side * 2, 0, end * 4), end, deuce));
    }
});
Test("Line footprint inside, tangent and outside singles/service boundaries", () =>
{
    Check(Court.SinglesIn(new Vec3(4.114, 0, 11.885), 1));
    // A circular footprint misses a corner when both offsets equal its radius.
    Check(!Court.SinglesIn(new Vec3(Court.HalfWidth + Court.BallRadius, 0, Court.HalfLength + Court.BallRadius), 1));
    Check(!Court.SinglesIn(new Vec3(Court.HalfWidth + Court.BallRadius + 1e-6, 0, 3), 1));
    Check(!Court.SinglesIn(new Vec3(0, 0, Court.HalfLength + Court.BallRadius + 1e-6), 1));
    Check(!Court.ServiceIn(new Vec3(-Court.BallRadius, 0, -6.4 - Court.BallRadius), 1, true));
    Check(Court.ServiceIn(new Vec3(1, 0, -6.4 - Court.BallRadius), 1, true));
    Check(!Court.ServiceIn(new Vec3(-Court.BallRadius - 1e-6, 0, -4), 1, true));
    Check(!Court.ServiceIn(new Vec3(2, 0, -6.4 - Court.BallRadius - 1e-6), 1, true));
});
Test("Hand and end orientation mirror correctly", () =>
{
    var body = new Vec3(); var right = new Vec3(1, 1, 0);
    Check(Court.IsForehand(body, right, -1, false)); Check(!Court.IsForehand(body, right, 1, false));
    Check(!Court.IsForehand(body, right, -1, true)); Check(Court.IsForehand(body, right, 1, true));
    Near(Court.BackhandX(body, -1, false), -Court.BackhandX(body, 1, false));
});
Test("Target solver is consistent with drag/gravity trajectory", () =>
{
    foreach (double drag in new[] { 0.0, .08, .8 })
    {
        var c = new SimConfig { Drag = drag }; var from = new Vec3(2, 1.3, -12); var target = new Vec3(-3, Court.BallRadius, 9);
        var b = new BallState { Position = from, Velocity = BallPhysics.Launch(from, target, 1.3, c) };
        var at = BallPhysics.At(b, 1.3, c); Near((at.Position - target).Length, 0);
        var split = BallPhysics.At(BallPhysics.At(b, .7, c), .6, c); Near((split.Position - at.Position).Length, 0);
    }
});
Test("High speed net crossing cannot tunnel and preserves collision states", () =>
{
    var b = new BallState { Position = new Vec3(0, .7, -1), Velocity = new Vec3(0, 0, 500) };
    var hits = new List<Collision>(); BallPhysics.Advance(b, config.TickSeconds, config, e => { hits.Add(e); return true; });
    Check(hits.Count == 1 && hits[0].Kind == "NetTouched"); Check(hits[0].Offset > 0 && hits[0].Offset < config.TickSeconds);
    Near(hits[0].Before.Position.Z, 0); Near((hits[0].After.Position - hits[0].Before.Position).Length, 0);
    Check(hits[0].Before.Velocity.Z > 0 && hits[0].After.Velocity.Z < 0);
});
Test("Net tape can continue over; air outside sidelines is not a collision", () =>
{
    var b = new BallState { Position = new Vec3(0, .92, -.1), Velocity = new Vec3(0, 0, 30) };
    var result = BallPhysics.Advance(b, .01, config); Check(result.NetTouched && result.Velocity.Z > 0);
    int collisions = 0;
    result = BallPhysics.Advance(new BallState { Position = new Vec3(6, 2, -.1), Velocity = new Vec3(0, 1, 40) }, .01, config, _ => { collisions++; return true; });
    Check(collisions == 0 && result.Position.Z > 0);
});
Test("High speed ground impact is interpolated; bounce impulse is consistent", () =>
{
    Collision? impact = null;
    var b = new BallState { Position = new Vec3(0, 1, 5), Velocity = new Vec3(3, -500, 2) };
    BallPhysics.Advance(b, config.TickSeconds, config, e => { impact = e; return false; });
    Check(impact != null && impact.Kind == "BallBounced"); Near(impact!.After.Position.Y, Court.BallRadius);
    Check(impact.Offset < config.TickSeconds); Near(impact.After.Velocity.Y, -impact.Before.Velocity.Y * config.Restitution);
    Near(impact.After.Velocity.X, impact.Before.Velocity.X * config.GroundFriction); Check(impact.After.Bounces == 1);
});
Test("Physics stops exactly at second bounce", () =>
{
    var b = new BallState { Position = new Vec3(0, 1, 5), Velocity = new Vec3(0, -2, 3) }; double duration = 0;
    while (b.Bounces < 2 && duration < 4) { b = BallPhysics.Advance(b, config.TickSeconds, config, e => e.After.Bounces < 2); duration += config.TickSeconds; }
    Check(b.Bounces == 2); Near(b.Position.Y, Court.BallRadius);
});
Test("Movement obeys acceleration and speed without court clamping", () =>
{
    var p = PlayerProfile.Preset("baseline", "A"); var s = new PlayerState { End = -1 };
    for (int i = 0; i < 1200; i++)
    {
        var before = s.Copy(); Movement.Step(p, s, new Vec3(15, 0, -20), config.TickSeconds);
        Check(s.Velocity.Length <= p.MaxSpeed + 1e-8); Check((s.Velocity - before.Velocity).Length <= p.Acceleration * config.TickSeconds + 1e-8);
        Near((s.Position - before.Position - (s.Velocity + before.Velocity) * (config.TickSeconds / 2)).Length, 0);
    }
    Check(s.Position.X > Court.HalfWidth && s.Position.Z < -Court.HalfLength);
});
Test("Contact requires preparation, bounce, incoming direction, height and reach", () =>
{
    var p = PlayerProfile.Preset("baseline", "A"); var s = new PlayerState { End = 1, Position = new Vec3(5, 0, 14) };
    var b = new BallState { Position = new Vec3(5.5, 1, 14), Velocity = new Vec3(0, 2, 10), Bounces = 1 };
    Check(Movement.CanContact(p, s, b, 1, config), "Outside-court legal contact");
    Check(!Movement.CanContact(p, s, b, .1, config));
    b.Bounces = 2; Check(!Movement.CanContact(p, s, b, 1, config)); b.Bounces = 0; Check(!Movement.CanContact(p, s, b, 1, config)); b.Bounces = 1;
    b.Position = new Vec3(8, 1, 14); Check(!Movement.CanContact(p, s, b, 1, config)); b.Position = new Vec3(5.5, 3, 14); Check(!Movement.CanContact(p, s, b, 1, config));
    b.Position = new Vec3(5.5, 1, 14); b.Velocity = new Vec3(0, 0, -10); Check(!Movement.CanContact(p, s, b, 1, config));
});
Test("Predictor marks impossible wide contact unreachable", () =>
{
    var p = PlayerProfile.Preset("baseline", "A"); var s = new PlayerState { End = 1, Position = new Vec3(-20, 0, 12) };
    var b = new BallState { Position = new Vec3(4, .8, 11), Velocity = new Vec3(0, 1, 15), Bounces = 1 };
    Movement.PredictContact(p, s, b, config, .3, out _, out bool reachable); Check(!reachable);
});

ShotChoice Choose(Tactic tactic, SeedRandom rng, double preparation = 1, bool serve = false, bool left = false, int end = -1)
{
    var p = PlayerProfile.Preset("baseline", "A"); var op = PlayerProfile.Preset("baseline", "B"); op.LeftHanded = left;
    var self = new PlayerState { End = end, Position = new Vec3(0, 0, end * 11) };
    var other = new PlayerState { End = -end, Position = new Vec3(0, 0, -end * 12) };
    return ShotPolicy.Choose(p, self, op, other, new Vec3(.3, serve ? 2.65 : 1.2, end * 11), tactic, serve, 1, true, preparation, config, rng);
}
Test("Neutral paired policy: TargetBackhand raises selection for both hands/ends", () =>
{
    foreach (bool left in new[] { false, true }) foreach (int end in new[] { -1, 1 })
    {
        int baseline = 0, targeted = 0; var a = new SeedRandom(234); var b = new SeedRandom(234);
        for (int i = 0; i < 2000; i++)
        {
            if (Choose(new Tactic(), a, left: left, end: end).Selected.Name == "Backhand") baseline++;
            if (Choose(new Tactic { Target = TargetStyle.TargetBackhand }, b, left: left, end: end).Selected.Name == "Backhand") targeted++;
        }
        Check(targeted > baseline + 200, $"{targeted}/{2000} vs {baseline}/{2000}");
    }
});
Test("TargetBackhand moves weight between sides without adding displacement", () =>
{
    var a = new SeedRandom(77); var b = new SeedRandom(77); int sidesBalanced = 0, sidesTargeted = 0, forehandTargeted = 0;
    for (int i = 0; i < 4000; i++)
    {
        string x = Choose(new Tactic(), a).Selected.Name, y = Choose(new Tactic { Target = TargetStyle.TargetBackhand }, b).Selected.Name;
        if (x is "Backhand" or "Forehand") sidesBalanced++;
        if (y is "Backhand" or "Forehand") sidesTargeted++;
        if (y == "Forehand") forehandTargeted++;
    }
    Check(Math.Abs(sidesTargeted - sidesBalanced) < 200, $"side shots {sidesTargeted} vs {sidesBalanced}");
    Check(forehandTargeted * 5 < sidesTargeted, $"forehand-side share {forehandTargeted}/{sidesTargeted}");
});
Test("Pressure grows with incoming pace; aggression is riskier under pressure", () =>
{
    Check(ShotPolicy.Pressure(15) == 0 && ShotPolicy.Pressure(25) == 1 && ShotPolicy.Pressure(17.75) > .4 && ShotPolicy.Pressure(17.75) < .6);
    var choice = Choose(new Tactic(), new SeedRandom(9)); var self = new PlayerState { Energy = 1 };
    double Spread(Aggression aggression, double pressure, int shots)
    {
        var rng = new SeedRandom(31); double total = 0;
        for (int i = 0; i < 400; i++) total += (ShotPolicy.Execute(choice, self, new Tactic { Aggression = aggression }, false, 1, pressure, shots, rng) - choice.Selected.LaunchVelocity).Length;
        return total;
    }
    Check(Spread(Aggression.Aggressive, 0, 0) < Spread(Aggression.Balanced, 0, 0), "aggression punishes an easy ball");
    Check(Spread(Aggression.Aggressive, 1, 0) > Spread(Aggression.Balanced, 1, 0), "aggression is punished by a hard ball");
    Check(Spread(Aggression.Safe, 1, 0) < Spread(Aggression.Balanced, 1, 0), "safe absorbs pace");
    Check(Spread(Aggression.Balanced, 0, 20) > Spread(Aggression.Balanced, 0, 0), "long rallies raise error");
});
Test("Rushed receivers wait for comfortable height only when they can", () =>
{
    var rising = new BallState { Position = new Vec3(0, .5, 11), Velocity = new Vec3(0, 3, 10), Bounces = 1 };
    var high = new BallState { Position = new Vec3(0, 1.0, 11), Velocity = new Vec3(0, 1, 10), Bounces = 1 };
    var apex = new BallState { Position = new Vec3(0, .6, 11), Velocity = new Vec3(0, -.1, 10), Bounces = 1 };
    Check(!Movement.Comfortable(rising) && Movement.Comfortable(high) && Movement.Comfortable(apex));
    var p = PlayerProfile.Preset("baseline", "A"); var s = new PlayerState { End = 1, Position = new Vec3(0, 0, 11) };
    Check(Movement.CanContact(p, s, rising, 1, config) && !Movement.CanContact(p, s, rising, 1, config, comfortableOnly: true));
});
Test("Receivers line up beside the ball on the side it arrives on", () =>
{
    var right = PlayerProfile.Preset("baseline", "A"); var left = right.Copy(); left.LeftHanded = true;
    foreach (var end in new[] { -1, 1 })
        foreach (var p in new[] { right, left })
            foreach (var dx in new[] { -2.0, 2.0 })
            {
                var s = new PlayerState { End = end, Position = new Vec3(0, 0, end * 11) };
                var contact = new Vec3(dx, 0, end * 10);
                bool forehand = Court.IsForehand(s.Position, contact, end, p.LeftHanded);
                var stance = Movement.Stance(p, s, contact);
                Near(Vec3.GroundDistance(stance, contact), Movement.StrokeOffset); Near(stance.Z, contact.Z);
                Check(Court.IsForehand(stance, contact, end, p.LeftHanded) == forehand, "stance keeps the stroke side");
                Check(Math.Abs(stance.X) < Math.Abs(contact.X), "stance is on the near side of the ball");
            }
});
Test("A rally shot aimed at a side is played on that side", () =>
{
    var input = new MatchInput(); input.Players[1] = PlayerProfile.Preset("baseline", "B");
    int aimed = 0, followed = 0;
    foreach (uint seed in new uint[] { 1, 2, 3 })
    {
        var hits = Run(seed, input: input).Events.Where(e => e.Kind == "BallHit").ToList();
        for (int i = 1; i < hits.Count; i++)
        {
            var a = hits[i - 1]; var b = hits[i];
            if (a.Point != b.Point || a.ShotKind == "Serve" || a.Reason is not ("Backhand" or "Forehand")) continue;
            aimed++; if (b.Stroke == a.Reason) followed++;
        }
    }
    // Before tennissim-mvp-5 receivers stood on the ball and followed the aimed side about 69% of the time.
    Check(aimed > 100 && followed >= .95 * aimed, $"followed {followed}/{aimed}");
});
Test("Aggressive changes feasible attack frequency; Safe changes speed constraints", () =>
{
    int safe = 0, aggressive = 0; double sv = 0, av = 0;
    var a = new SeedRandom(124); var b = new SeedRandom(124);
    for (int i = 0; i < 1000; i++)
    {
        var s = Choose(new Tactic { Aggression = Aggression.Safe }, a); var g = Choose(new Tactic { Aggression = Aggression.Aggressive }, b);
        if (s.Selected.Name == "Attack") safe++; if (g.Selected.Name == "Attack") aggressive++;
        sv += s.Selected.LaunchVelocity.Length; av += g.Selected.LaunchVelocity.Length;
    }
    Check(aggressive > safe + 200); Check(av > sv);
});
Test("Insufficient preparation rejects attack without forcing it", () =>
{
    var choice = Choose(new Tactic { Aggression = Aggression.Aggressive }, new SeedRandom(5), .3);
    var attack = choice.Candidates.Single(c => c.Name == "Attack"); Check(!attack.Feasible && attack.Rejection == "InsufficientPreparationTime"); Check(choice.Selected.Name != "Attack");
});
Test("Serve direction weighted policy changes selected course", () =>
{
    foreach (var direction in new[] { ServeDirection.Wide, ServeDirection.Body, ServeDirection.T })
    {
        var random = new SeedRandom(345); int selected = 0;
        for (int i = 0; i < 400; i++) if (Choose(new Tactic { Serve = direction }, random, serve: true).Selected.Name == direction.ToString()) selected++;
        Check(selected > 320);
    }
});
Test("Explicit RNG sequence/state, zero seed is non-degenerate", () =>
{
    // The seed is mixed (lowbias32 of 1 = 1753845952) before xorshift32; Legacy keeps the unmixed stream.
    var rng = new SeedRandom(1); Check(rng.NextUInt() == 145099912); Check(rng.NextUInt() == 4024068723); Check(rng.State == 4024068723);
    var legacy = SeedRandom.Legacy(1); Check(legacy.NextUInt() == 270369); Check(legacy.NextUInt() == 67634689);
    Check(new SeedRandom(0).NextUInt() != 0);
});
Test("Small seeds do not bias the first draw", () =>
{
    // Unmixed xorshift32 returned about seed * 2^-19 first, so seeds below 1000 all drew under .002.
    int low = 0; double sum = 0;
    for (uint seed = 1; seed <= 1000; seed++) { double first = new SeedRandom(seed).Next(); sum += first; if (first < .05) low++; }
    Check(low < 100, "first draws below .05: " + low); Check(Math.Abs(sum / 1000 - .5) < .05, "mean first draw " + sum / 1000);
});
Test("Invalid/nonfinite configuration and identity rejected", () =>
{
    foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1.0 })
    {
        bool rejected = false; try { new MatchEngine(new MatchInput { Config = new SimConfig { TickSeconds = bad } }); } catch (ArgumentException) { rejected = true; } Check(rejected);
    }
    var input = new MatchInput(); input.Players[1].Id = "A"; bool duplicate = false;
    try { new MatchEngine(input); } catch (ArgumentException) { duplicate = true; } Check(duplicate);
});

MatchRecord? sample = null;
Test("Complete fixed-seed set", () =>
{
    sample = Run(42); Check(sample.Status == "Completed", sample.Diagnostic); Check(sample.FinalScore.Complete);
    Check(sample.Events.Any(e => e.ShotKind == "Return")); Check(sample.Events.Any(e => e.Stroke == "Forehand") && sample.Events.Any(e => e.Stroke == "Backhand"));
});
Test("Identical input and different tick chunk sizes produce identical whole records", () =>
{
    Check(sample != null); string first = ReplayJson.Serialize(sample); Check(first == ReplayJson.Serialize(Run(42, 1))); Check(first == ReplayJson.Serialize(Run(42, 137)));
});
Test("Saved JSON/load preserves complete events, states, input and score", () =>
{
    string json = ReplayJson.Serialize(sample); var loaded = ReplayJson.Deserialize<MatchRecord>(json); Check(json == ReplayJson.Serialize(loaded));
    Check(loaded.FinalScore.Display == sample!.FinalScore.Display && loaded.Events.Count == sample.Events.Count);
});
Test("Actual file save/load and readonly summary fields survive JSON", () =>
{
    string path = Path.Combine("artifacts", "tests", "roundtrip.json");
    ReplayJson.Save(path, sample); var loaded = ReplayJson.Load(path);
    Check(ReplayJson.Serialize(loaded) == ReplayJson.Serialize(sample));
    using var doc = System.Text.Json.JsonDocument.Parse(ReplayJson.Serialize(new { policy = "safe", count = 1000, ratio = new Ratio { Numerator = 3, Denominator = 7 } }));
    Check(doc.RootElement.GetProperty("policy").GetString() == "safe"); Check(doc.RootElement.GetProperty("count").GetInt32() == 1000);
    Near(doc.RootElement.GetProperty("ratio").GetProperty("value").GetDouble(), 3.0 / 7);
});
Test("Event timestamps, sequence, collision and contact state invariants", () =>
{
    Check(sample != null); var r = sample!; double last = -1; int index = 0;
    var actions = r.Events.Where(e => e.Kind == "ContactPrepared").ToDictionary(e => e.ActionId);
    foreach (var e in r.Events)
    {
        Check(e.Sequence == index++ && e.Time + 1e-9 >= last, "Event order"); last = e.Time; Near(e.State.Time, e.Time);
        Check(e.State.Ball.Position.IsFinite && e.State.Ball.Velocity.IsFinite);
        foreach (var s in e.State.Players) { Check(s.Position.IsFinite && s.Velocity.IsFinite); Check(s.Energy >= .15 && s.Energy <= 1); Check(s.Velocity.Length <= r.Input.Players.Single(p => p.Id == s.Id).MaxSpeed + 1e-8); }
        if (e.Kind is "BallBounced" or "NetTouched" or "BallHit")
        {
            Check(e.Before != null); Near((e.State.Ball.Position - e.Before!.Ball.Position).Length, 0);
            if (e.Kind == "BallBounced") Near(e.State.Ball.Position.Y, Court.BallRadius);
        }
        if (e.Kind == "BallHit" && e.ShotKind != "Serve")
        {
            var s = e.State.Players.Single(p => p.Id == e.PlayerId); var p = r.Input.Players.Single(p => p.Id == e.PlayerId);
            Check(actions.TryGetValue(e.ActionId, out var action) && action.Time < e.Time, "Preparation precedes contact");
            Check(e.Before!.Ball.Bounces == 1 && Vec3.GroundDistance(s.Position, e.State.Ball.Position) <= config.Reach + 1e-8);
            Check(Court.IsForehand(s.Position, e.State.Ball.Position, s.End, p.LeftHanded) == (e.Stroke == "Forehand"));
        }
        if (e.Kind == "PointEnded") Check(r.Events.Any(s => s.Kind == "ScoreChanged" && s.Point == e.Point && s.Time == e.Time));
    }
    Check(r.Events.Where(e => e.Kind == "PointEnded" && e.Reason == "UnreturnedBall").All(e => e.State.Ball.Bounces == 2));
    Check(r.Stats.Players.Sum(p => p.PointsWon) == r.FinalScore.PointsPlayed);
    Check(r.Stats.Players.Sum(p => p.Shots) == r.Events.Count(e => e.Kind == "BallHit"));
    Check(r.Stats.Players.Sum(p => p.FirstLandings.Count) == r.Events.Count(e => e.Kind == "BallBounced" && e.State.Ball.Bounces == 1));
});
Test("Live tactic request waits for point boundary and re-simulates from history", () =>
{
    var engine = new MatchEngine(new MatchInput { Seed = 87 }); engine.AdvanceTicks(100); Check(engine.State.Phase == "Rally");
    int point = engine.State.Point; long requested = engine.Tick;
    engine.QueueTactics(0, new Tactic { Target = TargetStyle.TargetBackhand }); Check(engine.State.Tactics[0].Target == TargetStyle.Balanced);
    var record = engine.Run(); var history = record.InstructionHistory.Single(); Check(history.RequestedTick == requested && history.AppliedPoint > point);
    var replay = Run(87, input: record.Input); Check(ReplayJson.Serialize(record) == ReplayJson.Serialize(replay));
});
Test("Changeover pause: stops after each end change, instructions apply next point and re-simulate", () =>
{
    var engine = new MatchEngine(new MatchInput { Seed = 42, Players = new[] { PlayerProfile.Preset("baseline", "A"), PlayerProfile.Preset("baseline", "B") } });
    int pauses = 0; var tactics = new[] { new Tactic { Target = TargetStyle.TargetBackhand }, new Tactic { Aggression = Aggression.Aggressive }, new Tactic { Serve = ServeDirection.Wide } };
    while (engine.AdvanceToChangeover())
    {
        var state = engine.State;
        Check(engine.Record.Events[^1].Kind == "EndsChanged", "paused right after the end change");
        int games = state.Score.Games[0] + state.Score.Games[1];
        Check(state.Score.TieBreak || games % 2 == 1, "end changes only after odd games outside a tiebreak: " + games);
        var tactic = tactics[pauses % tactics.Length]; engine.QueueTactics(0, tactic); pauses++;
        int nextPoint = state.Score.PointsPlayed + 1;
        engine.AdvanceTicks(1);
        Check(engine.Finished || (engine.State.Point == nextPoint && engine.State.Tactics[0].Aggression == tactic.Aggression && engine.State.Tactics[0].Target == tactic.Target), "applied from the next point");
    }
    var record = engine.Record; Check(record.Status == "Completed" && pauses > 0);
    Check(record.InstructionHistory.Count == pauses && record.InstructionHistory.All(i => i.AppliedPoint > 0));
    Check(pauses == record.Events.Count(e => e.Kind == "EndsChanged") - (record.Events.Last(e => e.Kind == "EndsChanged").Point == record.FinalScore.PointsPlayed ? 1 : 0));
    Check(ReplayJson.Serialize(record) == ReplayJson.Serialize(Run(42, 137, record.Input)), "coached replay re-simulates exactly");
});
Test("Segment stats over the whole match agree with match stats", () =>
{
    foreach (uint seed in new uint[] { 3, 42 })
    {
        var r = Run(seed); var s = SegmentStats.Compute(r, 1, r.FinalScore.PointsPlayed);
        Check(s.Points == r.FinalScore.PointsPlayed);
        for (int i = 0; i < 2; i++)
        {
            Check(s.Players[i].PointsWon == r.Stats.Players[i].PointsWon, "points won");
            Check(s.Players[i].DoubleFaults == r.Stats.Players[i].DoubleFaults, "double faults");
            Check(s.Players[i].Forehands == r.Stats.Players[i].Forehands && s.Players[i].Backhands == r.Stats.Players[i].Backhands, "strokes");
        }
        Check(s.Players.Sum(p => p.ServePoints) == s.Points && s.Players[0].ServePointsWon + s.Players[1].ServePoints - s.Players[1].ServePointsWon == s.Players[0].PointsWon);
        Check(Math.Abs(s.MeanRallyLength - r.Stats.RallyLengths.Average()) < 1e-12, "rally length matches MatchStats");
        int ended = s.Players.Sum(p => p.Winners + p.ForehandErrors + p.BackhandErrors + p.DoubleFaults);
        Check(ended == s.Points, $"every point has one cause: {ended} vs {s.Points}");
        var half = SegmentStats.Compute(r, 1, 10); var rest = SegmentStats.Compute(r, 11, r.FinalScore.PointsPlayed);
        Check(half.Points + rest.Points == s.Points && half.Players[1].Winners + rest.Players[1].Winners == s.Players[1].Winners);
    }
});
Test("Aim and serve-course segment stats: every shot ends once, segments add up to the match", () =>
{
    foreach (uint seed in new uint[] { 3, 42 })
    {
        var input = new MatchInput { Seed = seed, Tactics = new[] { new Tactic { Target = TargetStyle.TargetBackhand, Serve = ServeDirection.Wide }, new Tactic() } };
        var r = Run(seed, input: input); int n = r.FinalScore.PointsPlayed;
        var whole = SegmentStats.Compute(r, 1, n); var a = SegmentStats.Compute(r, 1, n / 2); var b = SegmentStats.Compute(r, n / 2 + 1, n);
        for (int i = 0; i < 2; i++)
        {
            var p = whole.Players[i]; string id = r.Stats.Players[i].PlayerId;
            Check(p.BackhandAim.Shots + p.ForehandAim.Shots + p.OtherAim.Shots == p.Forehands + p.Backhands, "every rally shot has one aim");
            foreach (var (aim, name) in new[] { (p.BackhandAim, "Backhand"), (p.ForehandAim, "Forehand"), (p.OtherAim, "") })
            {
                Check(aim.Shots == r.Events.Count(e => e.Kind == "BallHit" && e.ShotKind != "Serve" && e.PlayerId == id && (name == "" ? e.Reason != "Backhand" && e.Reason != "Forehand" : e.Reason == name)), "aimed shots from events");
                Check(aim.Shots == aim.Winners + aim.Errors + aim.ReplyForehands + aim.ReplyBackhands, "each aimed shot ends once");
                Check(aim.ReplyErrors <= aim.ReplyForehands + aim.ReplyBackhands);
            }
            Check(p.WideServe.Points + p.BodyServe.Points + p.TServe.Points == p.ServePoints, "serve courses cover serve points");
            Check(p.WideServe.Won + p.BodyServe.Won + p.TServe.Won == p.ServePointsWon, "serve courses cover serve points won");
            Check(p.WideServe.FirstServesIn + p.BodyServe.FirstServesIn + p.TServe.FirstServesIn == p.FirstServesIn, "serve courses cover first serves in");
            int Sum(Func<SegmentPlayerStats, int> f) => f(a.Players[i]) + f(b.Players[i]);
            Check(Sum(x => x.BackhandAim.Shots) == p.BackhandAim.Shots && Sum(x => x.ForehandAim.ReplyBackhands) == p.ForehandAim.ReplyBackhands, "aims add up");
            Check(Sum(x => x.BackhandAim.ReplyErrors) == p.BackhandAim.ReplyErrors && Sum(x => x.OtherAim.Winners) == p.OtherAim.Winners && Sum(x => x.WideServe.Won) == p.WideServe.Won && Sum(x => x.TServe.Points) == p.TServe.Points && Sum(x => x.BodyServe.FirstServesIn) == p.BodyServe.FirstServesIn, "courses add up");
        }
        // Engine v5: the receiver plays the aimed side; A targets the backhand, so B mostly replies on the backhand.
        var aimA = whole.Players[0].BackhandAim;
        Check(aimA.Shots > 20 && aimA.ReplyBackhands > 0.9 * (aimA.ReplyBackhands + aimA.ReplyForehands), $"aimed side followed: {aimA.ReplyBackhands}/{aimA.ReplyBackhands + aimA.ReplyForehands}");
        Check(whole.Players[0].WideServe.Points > whole.Players[0].BodyServe.Points + whole.Players[0].TServe.Points, "Wide tactic serves mostly wide");
    }
});
Test("Opponent coach: reads scouting and style, consumes no randomness, re-simulates exactly", () =>
{
    MatchRecord Coached(uint seed, PlayerProfile a, Tactic tacticA)
    {
        var input = new MatchInput { Seed = seed, Players = new[] { a, PlayerProfile.Preset("baseline", "B") }, Tactics = new[] { tacticA, new Tactic() } };
        var engine = new MatchEngine(input); int from = 1;
        while (engine.AdvanceToChangeover())
        {
            var state = engine.State; int to = state.Score.PointsPlayed; uint before = engine.Record.FinalRandomState;
            string snapshot = ReplayJson.Serialize(engine.Record);
            var decision = OpponentCoach.Decide(1, engine.Record, from, to, state.Tactics, input.Players);
            Check(ReplayJson.Serialize(engine.Record) == snapshot, "Decide must not mutate the record");
            if (decision != null) engine.QueueTactics(1, decision);
            from = to + 1;
        }
        return engine.Record;
    }
    var baseline = PlayerProfile.Preset("baseline", "A");
    var strong = PlayerProfile.Preset("baseline", "A");
    (strong.ForehandPower, strong.BackhandPower, strong.ForehandControl, strong.BackhandControl) = (strong.BackhandPower, strong.ForehandPower, strong.BackhandControl, strong.ForehandControl);
    var r = Coached(5, baseline, new Tactic { Aggression = Aggression.Safe });
    var first = r.InstructionHistory.First(i => i.Player == 1).Value;
    Check(first.Aggression == Aggression.Aggressive, "attacks a Safe opponent");
    Check(first.Target == TargetStyle.TargetBackhand, "scouting: baseline backhand is weaker");
    var s2 = Coached(5, strong, new Tactic { Aggression = Aggression.Aggressive });
    var firstStrong = s2.InstructionHistory.FirstOrDefault(i => i.Player == 1)?.Value ?? new Tactic();
    Check(firstStrong.Target == TargetStyle.Balanced && firstStrong.Aggression == Aggression.Balanced, "no backhand targeting against a strong backhand; balanced against aggression");
    Check(ReplayJson.Serialize(r) == ReplayJson.Serialize(Coached(5, baseline, new Tactic { Aggression = Aggression.Safe })), "deterministic");
    Check(ReplayJson.Serialize(r) == ReplayJson.Serialize(Run(5, 137, r.Input)), "AI-coached replay re-simulates from its recorded instructions");
});
Test("Segment energy is the recorded energy after the last point of the range", () =>
{
    var r = Run(42); var last = r.Events.Last(e => e.Kind == "PointEnded" && e.Point == 10);
    var s = SegmentStats.Compute(r, 1, 10);
    for (int i = 0; i < 2; i++) { Check(s.Players[i].EnergyAtEnd == last.State.Players[i].Energy); Check(s.Players[i].EnergyAtEnd >= .15 && s.Players[i].EnergyAtEnd <= 1); }
    Check(SegmentStats.Compute(r, 10000, 10001).Players.All(p => p.EnergyAtEnd == -1), "empty range has no energy");
});
Test("Coaching commands parse settings and reject unknown ones", () =>
{
    Check(Coach.TryParse("t=backhand a=safe s=t", new Tactic(), out var t, out _) && t.Target == TargetStyle.TargetBackhand && t.Aggression == Aggression.Safe && t.Serve == ServeDirection.T);
    Check(Coach.TryParse("a=aggressive", t, out var u, out _) && u.Target == TargetStyle.TargetBackhand && u.Aggression == Aggression.Aggressive && t.Aggression == Aggression.Safe);
    Check(!Coach.TryParse("a=reckless", t, out _, out _) && !Coach.TryParse("a=7", t, out _, out _) && !Coach.TryParse("aggressive", t, out _, out _) && !Coach.TryParse("x=1", t, out _, out _));
});
Test("Limits terminate diagnostically and never award a fabricated point", () =>
{
    var r = Run(1, input: new MatchInput { Config = new SimConfig { MaxPointTicks = 1 } });
    Check(r.Status == "SimulationLimitExceeded" && !r.FinalScore.Complete && r.FinalScore.PointsPlayed == 0);
    Check(!r.Events.Any(e => e.Kind == "PointEnded") && r.Diagnostic.Length > 0);
});
Test("Several seeds and mirrored starting end finish", () =>
{
    foreach (uint seed in new uint[] { 1, 7, 99, 2026 })
    {
        var r = Run(seed, input: new MatchInput { Config = new SimConfig { InitialEndA = seed % 2 == 0 ? -1 : 1 } });
        Check(r.Status == "Completed", $"seed={seed}: {r.Diagnostic}");
    }
});
Test("Low-control independent points exercise real faults, lets and double faults", () =>
{
    int faults = 0, doubles = 0, lets = 0;
    for (uint seed = 1; seed <= 150; seed++)
    {
        var input = new MatchInput { Seed = seed }; input.Players[0].ServeControl = 0;
        input.Tactics[0] = new Tactic { Serve = ServeDirection.T, Aggression = Aggression.Aggressive };
        var r = new MatchEngine(input, 1).Run(); Check(r.Status == "PointBatchComplete", r.Diagnostic);
        faults += r.Events.Count(e => e.Kind == "ServeFault"); doubles += r.Stats.Players[0].DoubleFaults; lets += r.Stats.Players[0].ServeLets;
    }
    Console.WriteLine($"  serve stress: faults={faults} doubleFaults={doubles} lets={lets}"); Check(faults > 0 && doubles > 0 && lets > 0);
});
Test("Independent audit correctness scenarios", () =>
{
    var results = AuditScenarios.Run();
    foreach (var result in results.Where(r => r.Category == "CORRECTNESS_BUG")) Check(result.Status == "PASS", result.Name + ": " + result.Observed);
});
Test("Diagnostics preserve replay and random state; empty samples are not PASS", () =>
{
    string before = ReplayJson.Serialize(sample);
    var report = Diagnostics.Analyze(sample!, "sample.json", Diagnostics.Hash(before), "test-source");
    Check(ReplayJson.Serialize(sample) == before, "Observation mutated record");
    Check(report.Issues.Count == 0);
    Check(report.Metrics["Serve.launchSpeed"].Samples == sample!.Events.Count(e => e.ShotKind == "Serve"));
    var empty = Diagnostics.Analyze(new MatchRecord(), "empty.json", "none", "test-source");
    Check(empty.Checks.Values.All(c => c.Status == "NOT_MEASURABLE"));
    Check(empty.Metrics["Serve.launchSpeed"].Mean == null);
});
Test("Diagnostics catch actual out-of-reach state and exclude resets/dt zero", () =>
{
    var copy = ReplayJson.Deserialize<MatchRecord>(ReplayJson.Serialize(sample));
    var hit = copy.Events.First(e => e.Kind == "BallHit" && e.ShotKind != "Serve");
    hit.State.Players.Single(p => p.Id == hit.PlayerId).Position += new Vec3(100, 0, 0);
    var report = Diagnostics.Analyze(copy, "injected.json", "test", "test-source");
    Check(report.Checks["CONTACT_REACH"].Failures == 1);
    // Same-time duplicated frame is legal; point reset itself must not count as fast movement.
    copy = ReplayJson.Deserialize<MatchRecord>(ReplayJson.Serialize(sample));
    copy.Frames.Insert(1, copy.Frames[0]);
    report = Diagnostics.Analyze(copy, "duplicate.json", "test", "test-source");
    Check(report.Checks["PLAYER_DISPLACEMENT"].Failures == 0);
    Check(report.Metrics["A.movementDistance"].Excluded["dt<=0"] == 1);
});
Test("Diagnostic parser rejects missing velocity instead of observing zero", () =>
{
    string json = ReplayJson.Serialize(sample);
    Check(ReplayJson.Serialize(Diagnostics.Parse(json)) == json);
    var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
    node["events"]![4]!["state"]!["ball"]!.AsObject().Remove("velocity");
    bool rejected = false;
    try { Diagnostics.Parse(node.ToJsonString()); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Missing outgoing velocity must not default to an observed zero");
});
BounceTests.Run(Test);
CalibrationTests.Run(Test);
Console.WriteLine($"TEST_RESULT passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;
