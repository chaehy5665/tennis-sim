using System;
using System.Linq;
using System.Collections.Generic;
using TennisSim.Core.Bounce;

namespace TennisSim.Core
{
    public enum ChangeoverStop { Changeover, Finished, TickBudget }
    public sealed class MatchEngine
    {
        private readonly MatchInput input;
        private readonly SimConfig config;
        private readonly SurfaceEnvironment? surface;
        private readonly SeedRandom rng;
        private readonly Scoring score;
        private readonly PlayerState[] players = new PlayerState[2];
        private readonly Tactic[] tactics;
        private readonly List<TacticInstruction> instructions = new List<TacticInstruction>();
        private readonly int stopAfterPoints;
        // Pattern reading: each player's recent serve directions and rally target sides. A receiver who has seen the
        // same choice repeatedly reacts faster. Only the last PatternWindow choices of each kind count.
        private const int PatternWindow = 10, PatternMinimum = 4;
        private const double PatternFloor = .4, PatternReadBonus = .6;
        private readonly List<string>[] servePatterns = { new List<string>(), new List<string>() };
        private readonly List<string>[] rallyPatterns = { new List<string>(), new List<string>() };
        private double receiverReaction;
        private bool receiverComfortable;
        private ServeRules serve = new ServeRules();
        private BallState ball = new BallState();
        private string phase = "BetweenPoints";
        private long tick, pointStartTick;
        private double prepareUntil, lastHitTime;
        private int currentPoint;
        private int hitter, actionCounter, activeAction, receiverAction, pointShots, serveLaunches;
        private bool serveFlight, receiverPlanned;
        private Vec3 receiverTarget;
        private PlayerState[]? movementStart;
        public MatchRecord Record { get; }
        public bool Finished => Record.Status != "Running";
        public long Tick => tick;
        public FrameState State => Snapshot(tick * config.TickSeconds);

        public MatchEngine(MatchInput supplied, int stopAfterPoints = 0)
        {
            if (supplied == null || supplied.Config == null || supplied.Players == null || supplied.Players.Length != 2 || supplied.Tactics == null || supplied.Tactics.Length != 2 || supplied.Instructions == null) throw new ArgumentException("Two players and tactics are required");
            supplied.Config.Validate();
            foreach (var p in supplied.Players) { if (p == null) throw new ArgumentException("Missing player"); p.Validate(); }
            foreach (var t in supplied.Tactics) { if (t == null) throw new ArgumentException("Missing tactic"); t.Validate(); }
            if (supplied.Players[0].Id == supplied.Players[1].Id) throw new ArgumentException("Player IDs must be distinct");
            if (stopAfterPoints < 0) throw new ArgumentException("Negative point count");
            surface = supplied.Surface == null ? null : supplied.Surface.Copy();
            surface?.Validate();
            input = new MatchInput { Seed = supplied.Seed, Config = supplied.Config.Copy(), Players = new[] { supplied.Players[0].Copy(), supplied.Players[1].Copy() }, Tactics = new[] { supplied.Tactics[0].Copy(), supplied.Tactics[1].Copy() }, Surface = surface };
            config = input.Config; this.stopAfterPoints = stopAfterPoints; rng = new SeedRandom(input.Seed);
            score = new Scoring(config.FirstServer, config.InitialEndA);
            tactics = new[] { input.Tactics[0].Copy(), input.Tactics[1].Copy() };
            for (int i = 0; i < 2; i++) players[i] = new PlayerState { Id = input.Players[i].Id, End = i == 0 ? score.EndA : -score.EndA };
            Record = new MatchRecord { Input = input, Stats = new MatchStats { Players = new[] { new PlayerStats { PlayerId = players[0].Id }, new PlayerStats { PlayerId = players[1].Id } } } };
            foreach (var instruction in supplied.Instructions) Schedule(instruction.Player, instruction.Value, instruction.RequestedTick);
        }
        private void Schedule(int player, Tactic value, long requestedTick)
        {
            if (value == null || player < 0 || player > 1 || requestedTick < tick) throw new ArgumentException("Invalid tactic instruction");
            value.Validate();
            var instruction = new TacticInstruction { Player = player, Value = value.Copy(), RequestedTick = requestedTick };
            instructions.Add(instruction);
            input.Instructions.Add(new TacticInstruction { Player = player, Value = value.Copy(), RequestedTick = requestedTick });
            Record.InstructionHistory.Add(instruction);
        }
        public void QueueTactics(int player, Tactic value)
        {
            if (Finished) throw new InvalidOperationException("Match has ended");
            Schedule(player, value, tick);
        }
        public void AdvanceTicks(int count)
        {
            if (count < 0) throw new ArgumentException("Negative tick count");
            for (int i = 0; i < count && !Finished; i++)
            {
                try { Step(); }
                catch (SimulationLimitExceeded ex)
                {
                    Record.Status = "SimulationLimitExceeded"; Record.Diagnostic = "tick=" + tick + ", point=" + (score.PointsPlayed + 1) + ", phase=" + phase + ": " + ex.Message;
                    Emit("SimulationLimitExceeded", tick * config.TickSeconds, reason: Record.Diagnostic); Finish();
                }
                tick++;
            }
        }
        public MatchRecord Run() { while (!Finished) AdvanceTicks(1024); return Record; }
        // Advances until the players change ends (after odd games, every six tiebreak points) and returns true, paused
        // before the next point starts; tactics queued now apply from that point. Returns false when the match ends,
        // including an end change on the final point.
        public bool AdvanceToChangeover() => AdvanceUntilChangeover(long.MaxValue) == ChangeoverStop.Changeover;
        // Same as AdvanceToChangeover, but stops after at most maxTicks so a renderer can play a match frame by frame.
        public ChangeoverStop AdvanceUntilChangeover(long maxTicks)
        {
            if (maxTicks < 0) throw new ArgumentException("Negative tick count");
            int seen = Record.Events.Count;
            for (long n = 0; n < maxTicks && !Finished; n++)
            {
                AdvanceTicks(1);
                for (int i = seen; i < Record.Events.Count; i++)
                    if (Record.Events[i].Kind == "EndsChanged" && !score.Complete) return ChangeoverStop.Changeover;
                seen = Record.Events.Count;
            }
            return Finished ? ChangeoverStop.Finished : ChangeoverStop.TickBudget;
        }
        private void Step()
        {
            double time = tick * config.TickSeconds;
            movementStart = null;
            if (phase == "ServeRetry") PrepareServe(time);
            if (phase == "BetweenPoints")
            {
                if (score.Complete || (stopAfterPoints > 0 && score.PointsPlayed >= stopAfterPoints))
                { Record.Status = score.Complete ? "Completed" : "PointBatchComplete"; phase = "Complete"; Finish(); return; }
                if (score.PointsPlayed >= config.MaxPoints) throw new SimulationLimitExceeded("Maximum point count");
                BeginPoint(time);
            }
            if (tick - pointStartTick >= config.MaxPointTicks) throw new SimulationLimitExceeded("Maximum point duration");
            if (phase == "ServePreparation" && time + 1e-9 >= prepareUntil) Hit(score.Server, time, true);
            if (phase == "Rally") StepBall(time);
            if (tick % config.SnapshotEveryTicks == 0) Record.Frames.Add(Snapshot((tick + 1) * config.TickSeconds));
        }
        private void BeginPoint(double time)
        {
            currentPoint = score.PointsPlayed + 1;
            pointStartTick = tick; pointShots = 0; serveLaunches = 0; serve = new ServeRules();
            // Stable request ordering, including multiple instructions received on the same tick.
            var pending = new List<TacticInstruction>();
            foreach (var instruction in instructions) if (instruction.AppliedPoint < 0 && instruction.RequestedTick <= tick) pending.Add(instruction);
            pending.Sort((a, b) => { int order = a.RequestedTick.CompareTo(b.RequestedTick); return order != 0 ? order : instructions.IndexOf(a).CompareTo(instructions.IndexOf(b)); });
            foreach (var instruction in pending)
            {
                tactics[instruction.Player] = instruction.Value.Copy(); instruction.AppliedPoint = score.PointsPlayed + 1; instruction.AppliedTick = tick;
                Emit("TacticsApplied", time, instruction.Player);
            }
            for (int i = 0; i < 2; i++) players[i].Energy = Math.Min(1, players[i].Energy + .035 * (.5 + input.Players[i].Stamina));
            PrepareServe(time);
            Emit("PointStarted", time, score.Server);
        }
        private void PrepareServe(double time)
        {
            phase = "ServePreparation";
            for (int i = 0; i < 2; i++)
            {
                players[i].End = i == 0 ? score.EndA : -score.EndA;
                double x = score.DeuceSide ? -players[i].End : players[i].End;
                players[i].Position = new Vec3(x * (i == score.Server ? 1.1 : 1.5), 0, players[i].End * (i == score.Server ? 12.15 : 12.7));
                players[i].Velocity = new Vec3(); players[i].Facing = new Vec3(0, 0, -players[i].End);
            }
            ball = new BallState { Position = players[score.Server].Position + new Vec3(0, 2.65, 0) };
            prepareUntil = time + .6; receiverPlanned = false;
            Emit("PlayersRepositioned", time, reason: "BetweenServePreparation");
        }
        private void Hit(int player, double time, bool isServe)
        {
            if (isServe && ++serveLaunches > config.MaxServeAttempts) throw new SimulationLimitExceeded("Maximum serve attempts including lets");
            var p = input.Players[player]; var self = players[player];
            double preparation = isServe ? 1 : Math.Max(.1, 1 - .32 * self.Velocity.GroundLength / p.MaxSpeed - .20 * Vec3.GroundDistance(self.Position, ball.Position) / config.Reach);
            var choice = ShotPolicy.Choose(p, self, input.Players[1 - player], players[1 - player], ball.Position, tactics[player], isServe, serve.Attempt, score.DeuceSide, preparation, config, rng);
            activeAction = isServe ? ++actionCounter : receiverAction;
            if (isServe) Emit("ServeStarted", time, player, action: activeAction);
            var plan = Emit("ShotPlanned", time, player, action: activeAction);
            plan.IntendedTarget = choice.Selected.Target; plan.Candidates = choice.Candidates; plan.Reason = choice.Selected.Name;
            plan.PreparationQuality = preparation;
            var before = Snapshot(time);
            double pressure = isServe ? 0 : ShotPolicy.Pressure(ball.Velocity.Length);
            ball = new BallState { Position = ball.Position, Velocity = ShotPolicy.Execute(choice, self, tactics[player], isServe, serve.Attempt, pressure, pointShots, rng) };
            receiverReaction = ReadPattern(player, isServe, choice.Selected);
            self.Energy = Math.Max(.15, self.Energy - .002 / (.4 + p.Stamina));
            phase = "Rally";
            var hit = Emit("BallHit", time, player, action: activeAction);
            hit.Before = before; hit.IntendedTarget = choice.Selected.Target; hit.PreparationQuality = preparation; hit.Reason = choice.Selected.Name;
            hit.ShotKind = isServe ? "Serve" : pointShots == 1 ? "Return" : "Groundstroke";
            hit.Stroke = isServe ? "Serve" : choice.Forehand ? "Forehand" : "Backhand";
            var stats = Record.Stats.Players[player]; stats.Shots++; stats.ShotSpeeds.Add(ball.Velocity.Length); stats.IntendedTargets.Add(choice.Selected.Target);
            if (isServe) stats.ServeAttempts++;
            else
            {
                if (choice.Forehand) stats.Forehands++; else stats.Backhands++;
                stats.BackhandTargetSelection.Denominator++;
                if (choice.Selected.Name == "Backhand") stats.BackhandTargetSelection.Numerator++;
                MatchStats.Increment(stats.Choices, choice.Selected.Name); pointShots++;
            }
            foreach (var candidate in choice.Candidates) if (!candidate.Feasible) MatchStats.Increment(stats.CandidateRejections, candidate.Rejection);
            hitter = player; lastHitTime = time; serveFlight = isServe; receiverPlanned = false; receiverComfortable = false; receiverAction = ++actionCounter; phase = "Rally";
        }
        private void StepBall(double time)
        {
            int receiver = 1 - hitter; var rp = input.Players[receiver];
            if (!receiverPlanned && time - lastHitTime + 1e-9 >= receiverReaction)
            {
                // Plan a comfortable contact first; if it cannot be reached, plan the earliest reachable one (rushed).
                receiverTarget = Movement.PredictContact(rp, players[receiver], ball, config, time - lastHitTime, out double arrival, out bool reachable, surface, receiverReaction, comfortableOnly: true);
                receiverComfortable = reachable;
                if (!reachable) receiverTarget = Movement.PredictContact(rp, players[receiver], ball, config, time - lastHitTime, out arrival, out reachable, surface, receiverReaction);
                receiverPlanned = true;
                var action = Emit("ContactPrepared", time, receiver, reason: reachable ? "PredictedReachable" : "UnreachableContact", action: receiverAction);
                action.IntendedTarget = receiverTarget; action.PredictedContactTime = time + arrival;
            }
            movementStart = new[] { players[0].Copy(), players[1].Copy() };
            for (int i = 0; i < 2; i++)
            {
                Vec3 target = i == receiver ? (receiverPlanned ? receiverTarget : players[i].Position) : new Vec3(0, 0, players[i].End * 11.9);
                Movement.Step(input.Players[i], players[i], target, config.TickSeconds);
            }
            bool stopped = false;
            var advanced = BallPhysics.Advance(ball, config.TickSeconds, config, collision =>
            {
                double at = time + collision.Offset;
                ball = collision.Before; var before = Snapshot(at);
                ball = collision.After;
                var ev = Emit(collision.Kind, at, hitter, action: activeAction); ev.Before = before;
                if (collision.Bounce != null) ev.Bounce = collision.Bounce;
                if (collision.Kind == "BallBounced")
                {
                    if (ball.Bounces == 1)
                    {
                        Record.Stats.Players[hitter].FirstLandings.Add(ball.Position);
                        if (serveFlight)
                        {
                            var decision = serve.Resolve(Court.ServiceIn(ball.Position, players[hitter].End, score.DeuceSide), ball.NetTouched);
                            if (decision != ServeDecision.In)
                            {
                                ev.Reason = decision.ToString();
                                // Reposition only at the next fixed tick, after preserving the collision state.
                                if (decision == ServeDecision.DoubleFault)
                                { Record.Stats.Players[hitter].DoubleFaults++; Emit("ServeFault", at, hitter, "DoubleFault", activeAction); EndPoint(receiver, "DoubleFault", at); }
                                else
                                {
                                    if (decision == ServeDecision.Let) Record.Stats.Players[hitter].ServeLets++;
                                    Emit(decision == ServeDecision.Let ? "ServeLet" : "ServeFault", at, hitter, decision.ToString(), activeAction);
                                    phase = "ServeRetry";
                                }
                                stopped = true; return false;
                            }
                            Record.Stats.Players[hitter].ServesIn++; pointShots = 1; serveFlight = false; ev.Reason = "ServiceIn";
                        }
                        else if (!Court.SinglesIn(ball.Position, players[receiver].End))
                        { EndPoint(receiver, ball.NetTouched && ball.Position.Z * players[hitter].End > 0 ? "Net" : "Out", at); stopped = true; return false; }
                        else ev.Reason = "In";
                    }
                    else { EndPoint(hitter, "UnreturnedBall", at); stopped = true; return false; }
                }
                return true;
            }, surface);
            ball = advanced;
            if (!ball.Position.IsFinite || !ball.Velocity.IsFinite) throw new SimulationLimitExceeded("Non-finite ball state");
            if (stopped)
            {
                // Players only move for the elapsed fraction when a point/serve ends mid-tick.
                double at = Record.Events[Record.Events.Count - 1].Time;
                double fraction = Math.Max(0, Math.Min(1, (at - time) / config.TickSeconds));
                for (int i = 0; i < 2; i++) { players[i].Position = Vec3.Lerp(movementStart[i].Position, players[i].Position, fraction); players[i].Velocity = Vec3.Lerp(movementStart[i].Velocity, players[i].Velocity, fraction); players[i].Energy = movementStart[i].Energy + (players[i].Energy - movementStart[i].Energy) * fraction; }
                movementStart = null;
                return;
            }
            double endTime = (tick + 1) * config.TickSeconds;
            if (!serveFlight && Movement.CanContact(rp, players[receiver], ball, endTime - lastHitTime, config, receiverReaction, receiverComfortable)) Hit(receiver, endTime, false);
        }
        // Records the hitter's choice and returns the receiver's reaction time for this ball. The share of the same
        // choice in the hitter's recent history, above PatternFloor, shortens reaction by up to PatternReadBonus.
        private double ReadPattern(int hitter, bool isServe, Candidate selected)
        {
            int receiver = 1 - hitter; var rp = input.Players[receiver]; var r = players[receiver];
            string label = selected.Name;
            if (!isServe)
            {
                double side = selected.Target.X - r.Position.X;
                label = Math.Abs(side) < 1 ? "Centre" : Court.IsForehand(r.Position, selected.Target, r.End, rp.LeftHanded) ? "ForehandSide" : "BackhandSide";
            }
            var history = isServe ? servePatterns[hitter] : rallyPatterns[hitter];
            double read = 0;
            if (history.Count >= PatternMinimum)
            {
                double share = (double)history.Count(h => h == label) / history.Count;
                read = Math.Max(0, Math.Min(1, (share - PatternFloor) / (1 - PatternFloor)));
            }
            history.Add(label); if (history.Count > PatternWindow) history.RemoveAt(0);
            return rp.ReactionSeconds * (1 - PatternReadBonus * read);
        }
        private void EndPoint(int winner, string reason, double time)
        {
            Emit("PointEnded", time, winner, reason, activeAction);
            Record.Stats.Players[winner].PointsWon++; Record.Stats.RallyLengths.Add(pointShots); MatchStats.Increment(Record.Stats.EndReasons, reason);
            bool endsChanged = score.Award(winner);
            Emit("ScoreChanged", time, winner);
            if (endsChanged) Emit("EndsChanged", time, reason: "NextPointEndsAssigned");
            phase = "BetweenPoints";
        }
        private FrameState Snapshot(double time)
        {
            var copy = new[] { players[0].Copy(), players[1].Copy() };
            if (movementStart != null)
            {
                double t = Math.Max(0, Math.Min(1, (time - tick * config.TickSeconds) / config.TickSeconds));
                for (int i = 0; i < 2; i++) { copy[i].Position = Vec3.Lerp(movementStart[i].Position, copy[i].Position, t); copy[i].Velocity = Vec3.Lerp(movementStart[i].Velocity, copy[i].Velocity, t); copy[i].Energy = movementStart[i].Energy + (copy[i].Energy - movementStart[i].Energy) * t; }
            }
            return new FrameState { Tick = tick, Time = time, Phase = phase, Point = currentPoint, ServeAttempt = serve.Attempt, Score = score.Snapshot(), Players = copy, Ball = ball.Copy(), Tactics = new[] { tactics[0].Copy(), tactics[1].Copy() } };
        }
        private MatchEvent Emit(string kind, double time, int player = -1, string reason = "", int action = 0)
        {
            var ev = new MatchEvent { Sequence = Record.Events.Count, Time = time, Kind = kind, Point = currentPoint, PlayerId = player < 0 ? "" : players[player].Id, Reason = reason, ActionId = action, State = Snapshot(time) };
            Record.Events.Add(ev); return ev;
        }
        private void Finish()
        { Record.FinalScore = score.Snapshot(); Record.FinalRandomState = rng.State; Record.Frames.Add(Snapshot(tick * config.TickSeconds)); }
    }
}
