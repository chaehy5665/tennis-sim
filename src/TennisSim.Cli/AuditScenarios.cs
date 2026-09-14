using TennisSim.Core;

namespace TennisSim.Cli;

// Fixed inputs exercise production Core; expected values are analytic or independent geometry.
public static class AuditScenarios
{
    public sealed record Result(string Name, string Status, string Category, double? Observed, string Expected, string Unit, string Basis);
    public static List<Result> Run()
    {
        var results = new List<Result>();
        void Add(string name, bool pass, double observed, string expected, string unit, string basis, string category = "CORRECTNESS_BUG") => results.Add(new(name, pass ? "PASS" : "FAIL", category, observed, expected, unit, basis));
        var c = new SimConfig { Drag = 0 };
        Add("court.length", Math.Abs(Court.HalfLength * 2 - 23.77) < 1e-12, Court.HalfLength * 2, "23.77", "m", "R1 Rule 1; outside line edges");
        Add("court.width", Math.Abs(Court.HalfWidth * 2 - 8.23) < 1e-12, Court.HalfWidth * 2, "8.23", "m", "R1 singles");
        Add("net.centre", Math.Abs(Court.NetHeight(0) - .914) < 1e-12, Court.NetHeight(0), ".914", "m", "R1");
        Add("net.support", Math.Abs(Court.NetHeight(5.029) - 1.07) < 1e-12, Court.NetHeight(5.029), "1.07", "m", "R1 singles support X=4.115+.914; linear profile is approximation");
        int wrong = 0;
        foreach (int end in new[] { -1, 1 }) foreach (bool deuce in new[] { false, true })
        {
            int side = deuce ? end : -end;
            if (!Court.ServiceIn(new Vec3(side * 2, 0, -end * 4), end, deuce)) wrong++;
            if (Court.ServiceIn(new Vec3(-side * 2, 0, -end * 4), end, deuce)) wrong++;
        }
        Add("service.diagonal", wrong == 0, wrong, "0/8 wrong", "cases", "R1 Rule 17, end/deuce sign combinations");
        // At an outside corner, the nearest legal point is the corner: distance is sqrt(2)*.8R > R.
        // Existing separate-axis expansion admits a square footprint larger than the declared ball radius.
        double offset = .8 * Court.BallRadius; int cornerErrors = 0;
        foreach (int end in new[] { -1, 1 }) foreach (int side in new[] { -1, 1 })
        {
            if (Court.SinglesIn(new Vec3(side * (Court.HalfWidth + offset), 0, end * (Court.HalfLength + offset)), end)) cornerErrors++;
            bool deuce = side == end;
            if (Court.ServiceIn(new Vec3(side * (Court.HalfWidth + offset), 0, -end * (Court.ServiceLine + offset)), end, deuce)) cornerErrors++;
        }
        Add("line.circularFootprintCorner", cornerErrors == 0, cornerErrors, "0/8 false IN; nearest distance=.037901 > radius=.0335", "cases", "Independent Euclidean corner distance; R1 line contact plus existing finite-radius design assumption");
        int boundaryErrors = 0;
        foreach (int side in new[] { -1, 1 })
        {
            if (!Court.SinglesIn(new Vec3(side * (Court.HalfWidth + Court.BallRadius), 0, 5), 1)) boundaryErrors++;
            if (Court.SinglesIn(new Vec3(side * (Court.HalfWidth + Court.BallRadius + 1e-6), 0, 5), 1)) boundaryErrors++;
        }
        Add("line.edgeTangency", boundaryErrors == 0, boundaryErrors, "0/4 wrong", "cases", "Line inclusive; 1 micrometre outside rejects; no rendering radius");
        var initial = new BallState { Position = new Vec3(1, 5, 4), Velocity = new Vec3(3, 2, -1) };
        var at = BallPhysics.At(initial, .4, c);
        double error = (at.Position - new Vec3(2.2, 5 + .8 - 9.81 * .16 / 2, 3.6)).Length;
        Add("flight.analyticParabola", error < 1e-12, error, "<1e-12", "m", "x=x0+v0*t; y=y0+vy*t-g*t^2/2; controlled drag=0 only");
        double maxEnergyGain = 0; var passive = initial.Copy();
        for (int i = 0; i < 40; i++)
        {
            double Energy(BallState b) => .5 * b.Velocity.Length * b.Velocity.Length + 9.81 * b.Position.Y;
            var next = BallPhysics.At(passive, .008, new SimConfig()); maxEnergyGain = Math.Max(maxEnergyGain, Energy(next) - Energy(passive)); passive = next;
        }
        Add("flight.passiveEnergy", maxEnergyGain < 1e-10, maxEnergyGain, "<=1e-10", "J/kg", "positive drag, no hit or collision; gravitational potential included");
        foreach (double dt in new[] { 1.0 / 60, 1.0 / 120, 1.0 / 240 })
        {
            var b = new BallState { Position = new Vec3(0, 2.54 + Court.BallRadius, 5) }; double time = 0; Collision? hit = null; double contactTime = 0;
            while (hit == null && time < 2) { b = BallPhysics.Advance(b, dt, c, e => { hit = e; contactTime = time + e.Offset; return false; }); time += dt; }
            double expectedTime = Math.Sqrt(2 * 2.54 / 9.81);
            Add("drop.contactTime.dt" + dt, hit != null && Math.Abs(contactTime - expectedTime) < 1e-9, contactTime, expectedTime.ToString("R"), "s", "Analytic free fall; bisection 40 iterations; tolerance 1ns");
            if (hit != null)
            {
                Add("bounce.signAndHeight.dt" + dt, hit.Before.Velocity.Y < 0 && hit.After.Velocity.Y > 0 && Math.Abs(hit.After.Position.Y - Court.BallRadius) < 1e-10 && hit.After.Bounces == 1, hit.After.Position.Y, ".0335, downward->upward, one bounce", "m", "Exact event, physical centre");
                double bottomApex = hit.After.Velocity.Y * hit.After.Velocity.Y / (2 * 9.81);
                Add("drop.type2Reference.dt" + dt, bottomApex >= 1.35 && bottomApex <= 1.47, bottomApex, "1.35..1.47", "m bottom", "R1/R2 limited no-drag rigid-surface scenario; BALL_SURFACE_COMBINED_APPROXIMATION; NOT certification", "PARAMETER_MISMATCH");
            }
        }
        // Default drag retained for separate reference observation, not silently removed from operational model.
        var drop = new BallState { Position = new Vec3(0, 2.54 + Court.BallRadius, 5) }; double peak = 0;
        for (int i = 0; i < 220; i++) { drop = BallPhysics.Advance(drop, 1.0 / 120, new SimConfig(), e => e.After.Bounces < 2); if (drop.Bounces == 1) peak = Math.Max(peak, drop.Position.Y - Court.BallRadius); if (drop.Bounces == 2) break; }
        Add("drop.defaultDragReference", peak >= 1.35 && peak <= 1.47, peak, "1.35..1.47 (sampled lower bound)", "m bottom", "R2 surface unspecified in model: BALL_SURFACE_COMBINED_APPROXIMATION; apex sampling uncertainty <=g*dt^2/8", "PARAMETER_MISMATCH");
        int netCount = 0; var netBall = new BallState { Position = new Vec3(0, .7, -1), Velocity = new Vec3(0, 0, 500) };
        BallPhysics.Advance(netBall, 1.0 / 120, c, e => { if (e.Kind == "NetTouched") netCount++; return true; });
        Add("net.highSpeedNoTunnel", netCount == 1, netCount, "1 collision", "events", "500m/s controlled input traverses plane within tick; not realistic shot target");
        var profile = PlayerProfile.Preset("baseline", "A"); var state = new PlayerState { End = -1 };
        double maxA = 0, maxV = 0;
        for (int i = 0; i < 720; i++)
        {
            Vec3 target = i < 240 ? new Vec3(5, 0, 0) : i < 480 ? state.Position : new Vec3(-5, 0, 0);
            var before = state.Copy(); Movement.Step(profile, state, target, c.TickSeconds);
            maxA = Math.Max(maxA, (state.Velocity - before.Velocity).Length / c.TickSeconds); maxV = Math.Max(maxV, state.Velocity.Length);
        }
        Add("movement.targetStopReverseAcceleration", maxA <= profile.Acceleration + 1e-10, maxA, "<=10+1e-10", "m/s2", "720 real Movement.Step calls: fixed target, stop at current feet, reverse");
        Add("movement.targetStopReverseSpeed", maxV <= profile.MaxSpeed + 1e-10, maxV, "<=6.2+1e-10", "m/s", "Hard configured speed; not empirical athlete bound");
        var near = new PlayerState { End = 1, Position = new Vec3(0, 0, 12) };
        var incoming = new BallState { Position = new Vec3(.5, 1, 12), Velocity = new Vec3(0, 1, 4), Bounces = 1 };
        Add("contact.reachableNow", Movement.CanContact(profile, near, incoming, 1, c), Vec3.GroundDistance(near.Position, incoming.Position), "<=.95", "m", "Actual horizontal cylinder plus bounce/direction/height/preparation");
        incoming.Position = new Vec3(.950001, 1, 12);
        Add("contact.outsideReach", !Movement.CanContact(profile, near, incoming, 1, c), Vec3.GroundDistance(near.Position, incoming.Position), ">.95 -> reject", "m", "Boundary +1 micrometre; no teleport");
        var far = new PlayerState { End = 1, Position = new Vec3(-20, 0, 12) };
        Movement.PredictContact(profile, far, incoming, c, 1, out _, out bool reachable);
        Add("contact.unreachablePrediction", !reachable, reachable ? 1 : 0, "0", "boolean", "20m cannot be covered before second bounce");
        var x = new PlayerState { End = -1, Position = new Vec3(1, 0, -4) }; var mirror = new PlayerState { End = 1, Position = new Vec3(-1, 0, 4) };
        for (int i = 0; i < 100; i++) { Movement.Step(profile, x, new Vec3(-3, 0, -12), c.TickSeconds); Movement.Step(profile, mirror, new Vec3(3, 0, 12), c.TickSeconds); }
        Add("movement.endMirror", (x.Position + mirror.Position).Length < 1e-12, (x.Position + mirror.Position).Length, "<1e-12", "m", "Deterministic inputs reflected across origin, no random label symmetry claim");
        foreach (int server in new[] { 0, 1 }) { var score = new Scoring(server); for (int i = 0; i < 4; i++) score.Award(0); Add("server.alternate" + server, score.Server == 1 - server, score.Server, (1 - server).ToString(), "index", "One completed game switches server, independent of winner"); }
        return results;
    }
}
