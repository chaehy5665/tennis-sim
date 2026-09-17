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
        Check(targeted > baseline + 400, $"{targeted}/{2000} vs {baseline}/{2000}");
    }
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
    var rng = new SeedRandom(1); Check(rng.NextUInt() == 270369); Check(rng.NextUInt() == 67634689); Check(rng.State == 67634689);
    Check(new SeedRandom(0).NextUInt() != 0);
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
