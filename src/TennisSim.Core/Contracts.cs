using System;
using System.Collections.Generic;
using TennisSim.Core.Bounce;

namespace TennisSim.Core
{
    public sealed class TacticInstruction
    {
        public int Player { get; set; }
        public long RequestedTick { get; set; }
        public int AppliedPoint { get; set; } = -1;
        public long AppliedTick { get; set; } = -1;
        public Tactic Value { get; set; } = new Tactic();
    }
    public sealed class MatchInput
    {
        public uint Seed { get; set; } = 42;
        public SimConfig Config { get; set; } = new SimConfig();
        public PlayerProfile[] Players { get; set; } = { PlayerProfile.Preset("baseline", "A"), PlayerProfile.Preset("defender", "B") };
        public Tactic[] Tactics { get; set; } = { new Tactic(), new Tactic() };
        public List<TacticInstruction> Instructions { get; set; } = new List<TacticInstruction>();
        // Null keeps the legacy multiplicative bounce. A surface environment selects the explicit
        // impulse model and carries the profile identity into the replay.
        public SurfaceEnvironment? Surface { get; set; }
    }
    public sealed class FrameState
    {
        public long Tick { get; set; }
        public double Time { get; set; }
        public string Phase { get; set; } = "";
        public int Point { get; set; }
        public int ServeAttempt { get; set; }
        public ScoreState Score { get; set; } = new ScoreState();
        public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();
        public BallState Ball { get; set; } = new BallState();
        public Tactic[] Tactics { get; set; } = Array.Empty<Tactic>();
    }
    public sealed class MatchEvent
    {
        public long Sequence { get; set; }
        public double Time { get; set; }
        public string Kind { get; set; } = "";
        public int Point { get; set; }
        public string PlayerId { get; set; } = "";
        public int ActionId { get; set; }
        public string ShotKind { get; set; } = "";
        public string Stroke { get; set; } = "";
        public string Reason { get; set; } = "";
        public Vec3? IntendedTarget { get; set; }
        public double? PredictedContactTime { get; set; }
        public double? PreparationQuality { get; set; }
        public List<Candidate>? Candidates { get; set; }
        public FrameState? Before { get; set; }
        public FrameState State { get; set; } = new FrameState();
        // Impulse-model diagnostics for BallBounced collisions. Null under the legacy bounce.
        public BounceResult? Bounce { get; set; }
    }
    public sealed class Ratio
    {
        public int Numerator { get; set; }
        public int Denominator { get; set; }
        public double Value => Denominator == 0 ? 0 : (double)Numerator / Denominator;
    }
    public sealed class PlayerStats
    {
        public string PlayerId { get; set; } = "";
        public int PointsWon { get; set; }
        public int ServeAttempts { get; set; }
        public int ServesIn { get; set; }
        public int ServeLets { get; set; }
        public int DoubleFaults { get; set; }
        public int Shots { get; set; }
        public int Forehands { get; set; }
        public int Backhands { get; set; }
        public Ratio BackhandTargetSelection { get; set; } = new Ratio();
        public List<Vec3> IntendedTargets { get; set; } = new List<Vec3>();
        public List<Vec3> FirstLandings { get; set; } = new List<Vec3>();
        public List<double> ShotSpeeds { get; set; } = new List<double>();
        public Dictionary<string, int> CandidateRejections { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> Choices { get; set; } = new Dictionary<string, int>();
    }
    public sealed class MatchStats
    {
        public PlayerStats[] Players { get; set; } = Array.Empty<PlayerStats>();
        // Successful rally hits including the legal serve; failed serves and lets excluded.
        public List<int> RallyLengths { get; set; } = new List<int>();
        public Dictionary<string, int> EndReasons { get; set; } = new Dictionary<string, int>();
        internal static void Increment(Dictionary<string, int> values, string key) { values.TryGetValue(key, out int n); values[key] = n + 1; }
    }
    public sealed class MatchRecord
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string EngineVersion { get; set; } = "tennissim-mvp-3";
        public string Rules { get; set; } = "Singles; one set; advantage games; 6 games by 2; 6-6 seven-point tiebreak by 2; service lets";
        public bool RealismCalibrated { get; set; }
        public MatchInput Input { get; set; } = new MatchInput();
        public List<TacticInstruction> InstructionHistory { get; set; } = new List<TacticInstruction>();
        public List<MatchEvent> Events { get; set; } = new List<MatchEvent>();
        public List<FrameState> Frames { get; set; } = new List<FrameState>();
        public ScoreState FinalScore { get; set; } = new ScoreState();
        public MatchStats Stats { get; set; } = new MatchStats();
        public string Status { get; set; } = "Running";
        public string Diagnostic { get; set; } = "";
        public uint FinalRandomState { get; set; }
    }
}
