using System;
using System.Collections.Generic;
using System.Linq;
using TennisSim.Coach;
using TennisSim.Core;

int passed = 0, failed = 0;
void Test(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Check(bool ok, string message = "Assertion failed") { if (!ok) throw new Exception(message); }

MatchInput Input(uint seed) => CoachMatchup.Input(seed);
var script = new[] { new Tactic { Target = TargetStyle.TargetBackhand }, new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Aggressive }, null,
    new Tactic { Target = TargetStyle.TargetBackhand, Serve = ServeDirection.Wide }, null, null, null, null, null, null };

// Plays a whole set; frame > 0 advances in fixed frame steps as a renderer would, 0 jumps between stops.
CoachSession Play(uint seed, double frame, List<ChangeoverView> views = null)
{
    var session = new CoachSession(Input(seed));
    session.Start(new Tactic());
    int changeovers = 0;
    while (session.Phase != CoachPhase.Finished)
    {
        if (session.Phase == CoachPhase.Playing) { if (frame > 0) session.Advance(frame); else session.AdvanceToNextStop(); continue; }
        views?.Add(CoachViews.Changeover(session));
        session.Resume(script[Math.Min(changeovers++, script.Length - 1)]);
    }
    return session;
}

Test("Frame-by-frame play equals jumping between changeovers, and re-simulates exactly", () =>
{
    var jump = Play(3, 0); var frames = Play(3, 16.0 / 60);
    string a = TennisSim.Cli.ReplayJson.Serialize(jump.Engine.Record), b = TennisSim.Cli.ReplayJson.Serialize(frames.Engine.Record);
    Check(a == b, "frame stepping changed the match");
    var replay = new MatchEngine(jump.Engine.Record.Input).Run();
    Check(TennisSim.Cli.ReplayJson.Serialize(replay) == a, "coached session does not re-simulate");
    Check(jump.Engine.Record.Status == "Completed");
});
Test("Segments cover every point once and carry the tactics in force", () =>
{
    var s = Play(3, 0); var rec = s.Engine.Record;
    Check(s.Segments[0].FromPoint == 1 && s.Segments[^1].ToPoint == rec.FinalScore.PointsPlayed);
    for (int i = 1; i < s.Segments.Count; i++) Check(s.Segments[i].FromPoint == s.Segments[i - 1].ToPoint + 1 && s.Segments[i].FirstGame == s.Segments[i - 1].LastGame + 1);
    Check(s.Points.Count == rec.FinalScore.PointsPlayed && s.Points.Select(p => p.Point).SequenceEqual(Enumerable.Range(1, s.Points.Count)));
    foreach (var seg in s.Segments)
    {
        var first = rec.Events.First(e => e.Kind == "PointStarted" && e.Point == seg.FromPoint).State.Tactics;
        Check(CoachSession.Same(first[0], seg.TacticA) && CoachSession.Same(first[1], seg.TacticB), "tactics in force at segment " + seg.FromPoint);
    }
});
Test("Keeping the same tactic records no instruction; the opponent's changes are recorded with reasons", () =>
{
    var s = Play(3, 0); var rec = s.Engine.Record;
    Check(rec.InstructionHistory.Where(i => i.Player == 0).Count() == 3, "three real user changes in the script");
    var changes = s.Segments.Where(g => g.OpponentChangeAtEnd != null).ToList();
    Check(changes.Count == rec.InstructionHistory.Count(i => i.Player == 1) && changes.Count > 0);
    Check(changes.All(c => c.Reasons().Count > 0));
});
Test("Changeover view matches segment statistics and explains the opponent change", () =>
{
    var views = new List<ChangeoverView>(); var s = Play(3, 0, views);
    Check(views.Count == s.Segments.Count - 1);
    for (int i = 0; i < views.Count; i++)
    {
        var seg = s.Segments[i]; var stats = SegmentStats.Compute(s.Engine.Record, seg.FromPoint, seg.ToPoint);
        var points = views[i].Compare.Rows.Single(r => r.Label == "득점").Values;
        Check(points[points.Length / 2 - 1] == stats.Players[0].PointsWon.ToString() && points[points.Length - 1] == stats.Players[1].PointsWon.ToString(), "now columns");
        Check(views[i].Heading.Contains("포인트 " + seg.FromPoint + "–" + seg.ToPoint + ") · " + stats.Points + "포인트"));
        Check(views[i].OpponentChanged == (seg.OpponentChangeAtEnd != null));
        if (views[i].OpponentChanged) Check(views[i].OpponentText.Contains("→") && views[i].OpponentText.Contains("다음 포인트부터"), views[i].OpponentText);
    }
    var withChange = views.First(v => v.OpponentChanged);
    Console.WriteLine("  sample: " + withChange.OpponentKicker + " / " + withChange.OpponentText);
});
Test("Review view: segment points, game winners and landings agree with the record", () =>
{
    var s = Play(3, 0); var r = CoachViews.Review(s); var rec = s.Engine.Record;
    Check(r.Segments.Sum(g => g.Won) == rec.Stats.Players[0].PointsWon && r.Segments.Sum(g => g.Points) == rec.FinalScore.PointsPlayed);
    Check(r.GameWinners.Count == rec.FinalScore.Games.Sum() && r.GameWinners.Count(w => w == 0) == rec.FinalScore.Games[0]);
    int rally = rec.Events.Count(e => e.Kind == "BallHit" && e.PlayerId == "A" && e.ShotKind != "Serve");
    Check(r.Landings.Count <= rally && r.Landings.Count > rally * 0.9, $"landings {r.Landings.Count} of {rally} rally hits");
    Check(r.Landings.All(l => l.Z > 0), "rotated onto the opponent's half");
    Check(r.BackhandTargets == rec.Events.Count(e => e.Kind == "BallHit" && e.PlayerId == "A" && e.ShotKind != "Serve" && e.Reason == "Backhand" &&
        rec.Events.Any(b => b.Kind == "BallBounced" && b.ActionId == e.ActionId && b.State.Ball.Bounces == 1)));
    Check(r.Outs > 0 && r.Outs < r.Landings.Count);
    Check(r.Summary[0].A == rec.Stats.Players[0].PointsWon.ToString());
});
Test("Match view follows the engine state", () =>
{
    var session = new CoachSession(Input(3)); session.Start(new Tactic());
    for (int i = 0; i < 400 && session.Phase == CoachPhase.Playing; i++) session.Advance(1.0 / 60);
    var v = CoachViews.Match(session); var st = session.Engine.State;
    Check(v.PlayerX[0] == (float)st.Players[0].Position.X && v.BallZ == (float)st.Ball.Position.Z);
    Check(v.Point == st.Score.PointsPlayed + 1 && v.Feed.Count == Math.Min(5, session.Points.Count));
    Check(v.Feed.Count == 0 || v.Feed[0].Point == session.Points[^1].Point, "newest point first");
});
Test("Score text: 15/30/40, deuce, advantage and tiebreak", () =>
{
    string P(int a, int b, bool tb = false) => string.Join("-", CoachText.PointScore(new ScoreState { Points = new[] { a, b }, TieBreak = tb }));
    Check(P(0, 0) == "0-0" && P(1, 2) == "15-30" && P(3, 1) == "40-15" && P(3, 3) == "40-40" && P(4, 3) == "AD-40" && P(5, 6) == "40-AD" && P(6, 5, true) == "6-5");
});
Test("Tactic options round-trip and cover every tactic value", () =>
{
    foreach (var g in CoachViews.TacticGroups(true)) foreach (var o in g.Options)
    {
        var t = CoachViews.With(new Tactic(), o);
        Check(CoachViews.IsSelected(t, o), o.Key + "=" + o.Value);
        Check(g.Options.Count(x => CoachViews.IsSelected(t, x)) == 1, "exactly one selected in " + g.Key);
    }
    Check(CoachViews.TacticGroups(true).Sum(g => g.Options.Count) == 2 + Enum.GetValues(typeof(Aggression)).Length + Enum.GetValues(typeof(ServeDirection)).Length);
});
Test("Pre-match memo reads the opponent's profile", () =>
{
    var v = CoachViews.PreMatch(Input(1));
    Check(v.ScoutingMemo.Contains("강합니다") && v.Attributes[0].Count == 9 && v.Attributes[1][4].Value == "0.80");
    var weak = new MatchInput { Players = new[] { PlayerProfile.Preset("baseline", "A"), PlayerProfile.Preset("server", "B") } };
    Check(CoachViews.PreMatch(weak).ScoutingMemo.Contains("약점"));
});
Test("Session rejects calls out of phase", () =>
{
    var s = new CoachSession(Input(1));
    bool Throws(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }
    Check(Throws(() => s.Advance(1)) && Throws(() => s.Resume(null)));
    s.Start(new Tactic()); Check(Throws(() => s.Start(new Tactic())) && Throws(() => s.Resume(null)) && Throws(() => CoachViews.Review(s)));
});
Test("PlayMode scenario: a Safe start makes the opponent coach change at the first changeover", () =>
{
    // Mirrors CoachPlayModeTests so the Unity assertions are known to hold for actual Core.
    var s = new CoachSession(Input(3)); s.Start(new Tactic { Aggression = Aggression.Safe }); int banners = 0;
    s.AdvanceToNextStop();
    Check(s.Phase == CoachPhase.Changeover && CoachViews.Changeover(s).OpponentChanged, "banner at the first changeover");
    Check(s.OpponentChange.Tactic.Aggression == Aggression.Aggressive && s.OpponentChange.Reasons.Any(r => r.Kind == CoachReasonKind.CounterSafe));
    while (s.Phase != CoachPhase.Finished)
    {
        if (s.Phase == CoachPhase.Changeover) { if (CoachViews.Changeover(s).OpponentChanged) banners++; s.Resume(null); }
        else s.AdvanceToNextStop();
    }
    Check(banners > 0 && s.Engine.Record.Status == "Completed", "banners " + banners);
    Check(s.Engine.Record.InstructionHistory.All(i => i.Player == 1));
});
Test("Rook is the baseline preset with forehand and backhand swapped", () =>
{
    var i = CoachMatchup.Input(9); var b = PlayerProfile.Preset("baseline", "B");
    Check(i.Seed == 9 && i.Players[1].Name == "Rook" && i.Players[1].BackhandPower == b.ForehandPower && i.Players[1].ForehandControl == b.BackhandControl);
});
Test("Korean words stay whole: word joiners only around Hangul, never at spaces", () =>
{
    const char J = '\u2060';
    Check(CoachText.KeepAll("다음 체인지오버") == "다" + J + "음 체" + J + "인" + J + "지" + J + "오" + J + "버");
    Check(CoachText.KeepAll("Rook 코치가 23%로") == "Rook 코" + J + "치" + J + "가 23%" + J + "로");
    Check(CoachText.KeepAll("SEED 3 · 4×") == "SEED 3 · 4×", "no Hangul, unchanged");
    string once = CoachText.KeepAll("Ember의 백핸드 에러율이 23%로"), twice = CoachText.KeepAll(once);
    Check(once == twice, "idempotent");
    Check(once.Replace(J.ToString(), "") == "Ember의 백핸드 에러율이 23%로", "only joiners added");
    Check(once.Split(' ').Length == 4, "spaces untouched");
    Check(CoachText.KeepAll(CoachText.Short(new Tactic())).Split(' ').Length == 5, "short tactic text keeps break points between settings");
});
Test("Option names never repeat across axes; empty values use the full-width dash", () =>
{
    var names = CoachViews.TacticGroups(false).SelectMany(g => g.Options.Select(o => o.Label)).ToList();
    Check(names.Distinct().Count() == names.Count, string.Join(",", names));
    Check(CoachText.Tactic(new Tactic()) == "양쪽 · 균형 · 혼합" && CoachText.Short(new Tactic()) == "양쪽 · 균형 · 혼합");
    var s = Play(3, 0); var views = new List<ChangeoverView>(); Play(3, 0, views);
    var serve = views[0].Serve.Rows;
    Check(serve.Any(r => r.Values[0] == "—" && r.Values[1] == "—" && r.Values[2] == "—"), "an unused serve course shows the full-width dash");
    Check(CoachText.None == "\u2014");
    Check(views[0].Heading.Contains("–"), "ranges keep the short dash");
});
Test("Changeover evidence: panels follow the segment stats, previous columns appear after the first changeover", () =>
{
    var views = new List<ChangeoverView>(); var s = Play(3, 0, views); var rec = s.Engine.Record;
    for (int i = 0; i < views.Count; i++)
    {
        var v = views[i]; var seg = s.Segments[i]; var now = SegmentStats.Compute(rec, seg.FromPoint, seg.ToPoint);
        Check(v.HasPrevious == (i > 0) && v.Direction.Split.Count == (i > 0 ? 2 : 1) && v.Compare.Columns.Length == (i > 0 ? 4 : 2), "previous columns");
        Check(v.Direction.Split[^1].Label == "이번" && (i == 0 || v.Direction.Split[0].Label == "직전"));
        var me = now.Players[0]; var opp = now.Players[1];
        var aims = v.Direction.Rows;
        Check(aims.Select(r => r.Label).SequenceEqual(new[] { "백핸드 쪽", "포핸드 쪽", "그 외" }) && v.Direction.Columns.SequenceEqual(new[] { "타구", "상대 에러", "내 위너" }));
        Check(aims[0].Values.SequenceEqual(new[] { me.BackhandAim.Shots.ToString(), me.BackhandAim.ReplyErrors.ToString(), me.BackhandAim.Winners.ToString() }), "backhand-side counts");
        Check(aims[2].Values[0] == me.OtherAim.Shots.ToString() && aims.All(r => r.Muted == (int.Parse(r.Values[0]) < CoachViews.MinShots)), "row muted below 12 shots");
        int shots = me.BackhandAim.Shots + me.ForehandAim.Shots + me.OtherAim.Shots;
        Check(v.Direction.Sample == "이번 구간 " + shots + "구" && v.Direction.SampleTag == (shots < CoachViews.MinShots));
        int n = opp.Forehands + opp.Backhands;
        Check(n == 0 || v.Direction.Split[^1].LeftText == "백핸드 " + CoachText.Percent((double)opp.Backhands / n) + " · " + opp.Backhands + "/" + n, v.Direction.Split[^1].LeftText);
        Check(v.Serve.Rows.Select(r => r.Label).SequenceEqual(new[] { "와이드", "바디", "T" }) && v.Serve.Sample == "내 서브 " + me.ServePoints + "포인트");
        Check(v.Serve.Rows[0].Values[2] == (me.WideServe.Points == 0 ? "—" : me.WideServe.Won + "/" + me.WideServe.Points), "serve ratios as k/n");
        Check(v.Serve.Rows.All(r => !r.Values.Any(x => x.Contains("%"))), "no percent in serve course");
        Check(v.Compare.SampleTag == (now.Points < CoachViews.MinPoints) && v.Compare.Rows.Count == 6 && v.Compare.Rows.All(r => r.Label != "평균 랠리"));
        string rally = CoachText.Fixed(now.MeanRallyLength, 1) + "구";
        Check(i == 0 ? v.Compare.Note == "평균 랠리 · 이번 " + rally : v.Compare.Note.StartsWith("평균 랠리 · 직전 ") && v.Compare.Note.EndsWith(" → 이번 " + rally), v.Compare.Note);
    }
});
Test("Change summary names only the changed axes, or what is kept", () =>
{
    var current = new Tactic { Aggression = Aggression.Safe };
    Check(CoachViews.ChangeSummary(current, current.Copy()) == "변경 없음: 양쪽 · 안전 · 혼합 유지");
    Check(CoachViews.ChangeSummary(current, new Tactic { Aggression = Aggression.Aggressive }) == "공격성 안전 → 공격 · 다음 포인트부터");
    Check(CoachViews.ChangeSummary(current, new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Safe, Serve = ServeDirection.Wide }) == "공격 방향 양쪽 → 백핸드 공략, 서브 혼합 → 와이드 · 다음 포인트부터");
});
Test("Review: opponent-coach changes with reasons, landing filters by the tactic in force", () =>
{
    var s = Play(3, 0); var r = CoachViews.Review(s);
    var changes = s.Segments.Where(g => g.OpponentChangeAtEnd != null).ToList();
    Check(r.OpponentChanges.Count == changes.Count && changes.Count > 0);
    for (int i = 0; i < changes.Count; i++)
        Check(r.OpponentChanges[i].Kicker == "게임 " + changes[i].LastGame + " 뒤" && r.OpponentChanges[i].Change.Contains("→") && r.OpponentChanges[i].Reasons.Length > 0);
    Check(r.LandingFilters[0].Key == "all" && r.LandingFilters.Count == 3, "script uses both attack directions");
    var bh = CoachViews.Filter(r.Landings, "backhand"); var both = CoachViews.Filter(r.Landings, "balanced");
    Check(bh.Count + both.Count == r.Landings.Count && bh.Count > 0 && both.Count > 0);
    Check(bh.Count(l => l.BackhandTarget) * both.Count > both.Count(l => l.BackhandTarget) * bh.Count, "backhand targeting aims more at the backhand side");
    int k = r.Landings.Count(l => l.BackhandTarget);
    Check(CoachViews.LandingLegend(r.Landings) == "백핸드 쪽 " + k + "/" + r.Landings.Count + "구 · " + CoachText.Percent((double)k / r.Landings.Count));
    var fixedOpp = new CoachSession(Input(3), adaptiveOpponent: false); fixedOpp.Start(new Tactic());
    while (fixedOpp.Phase != CoachPhase.Finished) { if (fixedOpp.Phase == CoachPhase.Changeover) fixedOpp.Resume(null); else fixedOpp.AdvanceToNextStop(); }
    var rf = CoachViews.Review(fixedOpp);
    Check(rf.OpponentChanges.Count == 0 && rf.NoOpponentChange == "Rook 코치는 전술을 바꾸지 않았습니다." && rf.LandingFilters.Count == 2);
});
Console.WriteLine($"COACH_CHECKS passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;

static class Extensions
{
    public static List<CoachReason> Reasons(this CoachSegment s) => s.OpponentChangeAtEnd.Reasons;
}
