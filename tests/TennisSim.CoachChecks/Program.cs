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
Test("Rook is the backhander preset: the baseline player with forehand and backhand swapped", () =>
{
    var i = CoachMatchup.Input(9); var b = PlayerProfile.Preset("baseline", "B");
    Check(i.Seed == 9 && i.Players[1].Name == "Rook" && i.Players[1].BackhandPower == b.ForehandPower && i.Players[1].ForehandControl == b.BackhandControl);
});
Test("Using the backhander preset keeps coached sessions byte-identical to the hand-built Rook", () =>
{
    // The Rook CoachMatchup built by hand before the player archetypes existed.
    MatchInput Legacy(uint seed)
    {
        var rook = PlayerProfile.Preset("baseline", "B"); rook.Name = "Rook";
        (rook.ForehandPower, rook.BackhandPower) = (rook.BackhandPower, rook.ForehandPower);
        (rook.ForehandControl, rook.BackhandControl) = (rook.BackhandControl, rook.ForehandControl);
        return new MatchInput { Seed = seed, Players = new[] { PlayerProfile.Preset("baseline", "A"), rook } };
    }
    string Coached(MatchInput input)
    {
        var s = new CoachSession(input); s.Start(new Tactic { Target = TargetStyle.TargetBackhand }); int n = 0;
        while (s.Phase != CoachPhase.Finished)
        {
            if (s.Phase == CoachPhase.Changeover) s.Resume(n++ % 2 == 0 ? new Tactic { Aggression = Aggression.Aggressive } : null);
            else s.AdvanceToNextStop();
        }
        return TennisSim.Cli.ReplayJson.Serialize(s.Engine.Record);
    }
    Check(TennisSim.Cli.ReplayJson.Serialize(CoachMatchup.Input(3).Players[1]) == TennisSim.Cli.ReplayJson.Serialize(Legacy(3).Players[1]), "same profile, field for field");
    foreach (uint seed in new uint[] { 3, 7, 42 })
        Check(Coached(CoachMatchup.Input(seed)) == Coached(Legacy(seed)), "coached record bytes differ at seed " + seed);
});
Test("Opponent-coach style reasons read as sentences with their numbers", () =>
{
    Check(CoachText.Reason(new CoachReason { Kind = CoachReasonKind.HoldStyle }, "Ember") == "Ember 선수가 방금 공격성을 바꿔, 한 구간 균형으로 지켜봅니다.");
    var tried = new CoachReason { Kind = CoachReasonKind.TryStyle, From = Aggression.Balanced, To = Aggression.Aggressive, A = .38, B = 13 };
    Check(CoachText.Reason(tried, "Ember") == "균형으로 38%(13포인트)에 그쳐 공격을 한 구간 시험합니다.", CoachText.Reason(tried, "Ember"));
    var kept = new CoachReason { Kind = CoachReasonKind.MeasuredStyle, From = Aggression.Balanced, To = Aggression.Safe, A = .55, B = .38 };
    Check(CoachText.Reason(kept, "Ember") == "시험해 보니 안전이 55%로 균형 38%보다 나아 안전으로 갑니다.", CoachText.Reason(kept, "Ember"));
    var slugger = new CoachReason { Kind = CoachReasonKind.SelfScouting, To = Aggression.Safe, A = .935, B = .65 };
    Check(CoachText.Reason(slugger, "Ember") == "우리 선수는 파워(0.94)에 비해 컨트롤(0.65)이 낮아 안전하게 칩니다.", CoachText.Reason(slugger, "Ember"));
    var touch = new CoachReason { Kind = CoachReasonKind.SelfScouting, To = Aggression.Aggressive, A = .59, B = .92 };
    Check(CoachText.Reason(touch, "Ember") == "우리 선수는 컨트롤(0.92)에 비해 파워(0.59)가 약해 먼저 공격합니다.", CoachText.Reason(touch, "Ember"));
    var light = new CoachReason { Kind = CoachReasonKind.OpponentScouting, To = Aggression.Aggressive, A = .45, B = .88 };
    Check(CoachText.Reason(light, "Moss") == "Moss의 공이 가벼워(파워 0.45) 공격합니다.", CoachText.Reason(light, "Moss"));
    var heavy = new CoachReason { Kind = CoachReasonKind.OpponentScouting, To = Aggression.Safe, A = .935, B = .65 };
    Check(CoachText.Reason(heavy, "Blaze") == "Blaze의 공이 무겁지만(파워 0.94) 컨트롤(0.65)이 낮아 안전하게 버팁니다.", CoachText.Reason(heavy, "Blaze"));
    foreach (CoachReasonKind kind in Enum.GetValues(typeof(CoachReasonKind)))
        Check(CoachText.Reason(new CoachReason { Kind = kind }, "Ember") != kind.ToString(), "every reason has a sentence: " + kind);
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
    Check(serve.Any(r => r.Values.Length == 2 && r.Values.All(x => x == "—")), "an unused serve course shows the full-width dash");
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
        Check(aims[2].Values[0] == me.OtherAim.Shots.ToString() && aims.All(r => r.Muted == (int.Parse(r.Values[0]) < CoachViews.MinShots || v.Direction.SampleTag)), "row muted below 12 shots or in a tagged panel");
        Check(aims[2].Note == "빈 곳 · 강타 · 깊게" && aims[0].Note == null && aims[1].Note == null, "only 그 외 carries a second line");
        int shots = me.BackhandAim.Shots + me.ForehandAim.Shots + me.OtherAim.Shots;
        Check(v.Direction.Sample == "이번 구간 " + shots + "구" && v.Direction.SampleTag == (shots < CoachViews.MinShots));
        int n = opp.Forehands + opp.Backhands;
        Check(n == 0 || v.Direction.Split[^1].LeftText == "백핸드 " + CoachText.Percent((double)opp.Backhands / n) + " · " + opp.Backhands + "/" + n, v.Direction.Split[^1].LeftText);
        Check(v.Serve.Rows.Select(r => r.Label).SequenceEqual(new[] { "와이드", "바디", "T" }) && v.Serve.Sample == "내 서브 " + me.ServePoints + "포인트");
        Check(v.Serve.Columns.SequenceEqual(new[] { "첫 서브", "득점" }) && v.Serve.Compact, "serve course columns");
        Check(v.Serve.Rows[0].Values.SequenceEqual(me.WideServe.Points == 0 ? new[] { "—", "—" } : new[] { me.WideServe.FirstServesIn + "/" + me.WideServe.Points, me.WideServe.Won + "/" + me.WideServe.Points }), "serve ratios as k/n");
        Check(v.Serve.Rows.All(r => !r.Values.Any(x => x.Contains("%"))), "no percent in serve course");
        Check(v.Compare.SampleTag == (now.Points < CoachViews.MinPoints) && v.Compare.Rows.Select(r => r.Label).SequenceEqual(new[] { "득점", "서브 득점", "첫 서브 성공", "위너", "에러 포핸드/백핸드", "체력(구간 최저)" }));
        string rally = CoachText.Fixed(now.MeanRallyLength, 1) + "구";
        var note = v.Compare.Notes[0].Text;
        Check(i == 0 ? note == "평균 랠리 · 이번 " + rally : note.StartsWith("평균 랠리 · 직전 ") && note.EndsWith(" → 이번 " + rally), note);
        Check(v.Compare.Rows[5].Values[^1] == CoachText.Fixed(now.Players[1].EnergyMin, 2), "lowest energy, two decimals");
        Check(v.Compare.Notes[1].Text == "가장 지쳤을 때 최고 속도 · " + s.Input.Players[0].Name + " " + CoachViews.SpeedLoss(s.Input.Players[0], now.Players[0].EnergyMin) + " · " + s.Input.Players[1].Name + " " + CoachViews.SpeedLoss(s.Input.Players[1], now.Players[1].EnergyMin), v.Compare.Notes[1].Text);
        Check(v.Compare.Notes.All(x => x.Muted), "context lines under the comparison are muted");
        // One muting rule: a tagged panel mutes every value; an untagged panel mutes only rows below their sample.
        foreach (var panel in new[] { v.Direction, v.Serve, v.Compare })
            if (panel.SampleTag) Check(panel.Rows.All(r => r.Muted) && panel.Split.All(x => x.Muted) && panel.Notes.All(x => x.Muted), panel.Title + " tagged: all values of its sample muted");
        if (!v.Compare.SampleTag) Check(v.Compare.Rows.All(r => !r.Muted), "comparison values stay in line colour above the sample");
    }
});
Test("Change summary names only the changed axes, or what is kept", () =>
{
    var current = new Tactic { Aggression = Aggression.Safe };
    Check(CoachViews.ChangeSummary(current, current.Copy()).SequenceEqual(new[] { "변경 없음: 양쪽 · 안전 · 혼합 유지" }));
    Check(CoachViews.ChangeSummary(current, new Tactic { Aggression = Aggression.Aggressive }).SequenceEqual(new[] { "공격성 안전 → 공격" }));
    Check(CoachViews.ChangeSummary(current, new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Safe, Serve = ServeDirection.Wide }).SequenceEqual(new[] { "공격 방향 양쪽 → 백핸드 공략", "서브 코스 혼합 → 와이드" }), "one changed axis per line");
    var all = CoachViews.ChangeSummary(current, new Tactic { Target = TargetStyle.TargetBackhand, Aggression = Aggression.Aggressive, Serve = ServeDirection.T });
    Check(all.Length == 3 && all.All(x => !x.Contains("다음 포인트부터")), "at most three lines, fits the 3-line box");
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

// ---------- 2.5D broadcast view (design system broadcast.md, docs/BROADCAST_ASSETS.md) ----------
// A real engine record: the CLI replay named by TENNISSIM_BROADCAST_REPLAY (e.g. `match --seed 42 --out ...`), else
// a coached session played here. Both are plain Core records; the broadcast layer only reads them.
MatchRecord BroadcastRecord(out string source)
{
    var path = Environment.GetEnvironmentVariable("TENNISSIM_BROADCAST_REPLAY");
    if (!string.IsNullOrEmpty(path)) { source = path; return TennisSim.Cli.ReplayJson.Load(path); }
    source = "coached session seed 42"; return Play(42, 0).Engine.Record;
}
var cam = BroadcastCamera.Default();
bool InsideRect(ScreenRect r, ScreenPoint p) => p.InFront && r.Contains(p.X, p.Y);
bool InPolygon(IReadOnlyList<ScreenPoint> poly, double x, double y)
{
    bool inside = false;
    for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
    return inside;
}

Test("Broadcast: court corners, lines and net stand inside the title-safe area", () =>
{
    var safe = BroadcastSpec.SafeArea;
    foreach (var c in BroadcastCourt.SinglesCorners) Check(InsideRect(safe, cam.Project(c)), "corner " + c.X + "," + c.Z);
    foreach (var seg in BroadcastCourt.Lines()) Check(InsideRect(safe, cam.Project(seg.A)) && InsideRect(safe, cam.Project(seg.B)), "line end");
    foreach (var t in BroadcastCourt.NetTape()) Check(InsideRect(safe, cam.Project(t)), "net tape at x=" + t.X);
    foreach (double x in new[] { -BroadcastSpec.NetPostX, BroadcastSpec.NetPostX }) Check(InsideRect(safe, cam.Project(new Vec3(x, 0, 0))), "net post foot");
});
Test("Broadcast: the far baseline looks shorter and higher than the near one", () =>
{
    var c = BroadcastCourt.SinglesCorners.Select(cam.Project).ToArray();
    double near = c[1].X - c[0].X, far = c[2].X - c[3].X;
    Check(far < near && far > 0, $"near {near:0} px, far {far:0} px");
    Check(c[2].Y < c[1].Y && Math.Abs(c[0].Y - c[1].Y) < 1e-9 && Math.Abs(c[2].Y - c[3].Y) < 1e-9, "baselines stay level on screen");
    Check(Math.Abs(c[0].X + c[1].X - BroadcastSpec.ScreenWidth) < 1e-9, "the court is centred");
    Console.WriteLine($"  near baseline {near:0.0} px at y {c[0].Y:0.0}, far {far:0.0} px at y {c[3].Y:0.0}");
});
Test("Broadcast: the numbers in the design system match the camera", () =>
{
    // broadcast.md: near baseline y≈489, far baseline y≈240, a player 20.5 m behind the net: near foot y≈688, far head y≈143.
    Check(Math.Abs(cam.Project(new Vec3(0, 0, -Court.HalfLength)).Y - 489) < 1, "near baseline");
    Check(Math.Abs(cam.Project(new Vec3(0, 0, Court.HalfLength)).Y - 240) < 1, "far baseline");
    var nearFoot = cam.Player(new Vec3(0, 0, -BroadcastSpec.FramedDepth)); var farHead = cam.Player(new Vec3(0, 0, BroadcastSpec.FramedDepth));
    Check(Math.Abs(nearFoot.Foot.Y - 688) < 1 && Math.Abs(farHead.Head.Y - 143) < 1, "framed depth");
    Check(nearFoot.Foot.Y + nearFoot.ShadowRadiusY <= BroadcastSpec.ControlBar.Y - 4, "the deepest near player stays above the control bar");
    Check(farHead.Badge.Y >= BroadcastSpec.SafeArea.Y, "the deepest far player's badge stays below the safe-area top");
    double nearW = BroadcastSpec.LineWidth(cam.Project(new Vec3(0, 0, -Court.HalfLength)).Depth), farW = BroadcastSpec.LineWidth(cam.Project(new Vec3(0, 0, Court.HalfLength)).Depth);
    Check(Math.Abs(nearW - 1.95) < .1 && Math.Abs(farW - 1.16) < .1, $"line width near {nearW:0.00}, far {farW:0.00}");
});
Test("Broadcast: the ball keeps its recorded place, a fixed 11 px size and a shadow straight below", () =>
{
    foreach (var b in new[] { new Vec3(1.6, 1.25, 3.2), new Vec3(-3, .05, -11), new Vec3(2, 4.5, 10), new Vec3(0, 0, 0) })
    {
        var m = cam.Ball(b); var ground = cam.Project(new Vec3(b.X, 0, b.Z)); var exact = cam.Project(b);
        Check(m.Ball.X == exact.X && m.Ball.Y == exact.Y, "ball centre is the projected record");
        Check(m.Shadow.X == ground.X && m.Shadow.Y == ground.Y, "shadow is the ground point below the ball");
        Check(b.Y > 0 ? m.Shadow.Y > m.Ball.Y : m.Shadow.Y == m.Ball.Y, "shadow is under the ball on screen");
        Check(m.Diameter == BroadcastSpec.BallDiameter, "fixed diameter");
        Check(m.HeightGuide == (b.Y > BroadcastSpec.BallHeightGuideAbove), "height guide above 0.3 m");
    }
    double realFar = 2 * Court.BallRadius * cam.PixelsPerMetre(cam.Project(new Vec3(0, 1, Court.HalfLength)).Depth);
    Check(realFar < 3, $"a true-size ball at the far baseline is {realFar:0.0} px, which is why it is drawn at 11 px");
});
Test("Broadcast: HUD plates sit inside the safe area and off the court corridor", () =>
{
    var court = BroadcastCourt.SinglesCorners.Select(cam.Project).ToList();
    foreach (var plate in BroadcastSpec.HudPlates)
    {
        var safe = BroadcastSpec.SafeArea;
        Check(safe.Contains(plate.X, plate.Y) && safe.Contains(plate.Right, plate.Bottom), "plate inside safe area");
        for (double x = plate.X; x <= plate.Right; x += 4)
            for (double y = plate.Y; y <= plate.Bottom; y += 4)
                Check(!InPolygon(court, x, y), $"plate at {plate.X},{plate.Y} covers the court at {x},{y}");
    }
});
Test("Broadcast: recorded players stay in view and are never covered by a HUD plate; the ball is counted", () =>
{
    var rec = BroadcastRecord(out var source); var view = BroadcastSpec.Viewport;
    int ballOut = 0, samples = 0; double deepest = 0;
    foreach (var f in rec.Frames)
    {
        foreach (var pl in f.Players)
        {
            var m = cam.Player(pl.Position); deepest = Math.Max(deepest, Math.Abs(pl.Position.Z));
            Check(InsideRect(view, m.Foot) && view.Contains(m.Badge.X, m.Badge.Y) && view.Contains(m.Badge.Right, m.Badge.Bottom), $"player {pl.Id} at {pl.Position.X:0.0},{pl.Position.Z:0.0} ({source})");
            var body = new ScreenRect(m.Foot.X - m.ShadowRadiusX, m.Badge.Y, 2 * m.ShadowRadiusX, m.Foot.Y + m.ShadowRadiusY - m.Badge.Y);
            foreach (var plate in BroadcastSpec.HudPlates) Check(!plate.Overlaps(body), $"a HUD plate at {plate.X},{plate.Y} covers player {pl.Id} at {pl.Position.X:0.0},{pl.Position.Z:0.0} ({source})");
        }
        samples++; if (!InsideRect(view, cam.Ball(f.Ball.Position).Ball)) ballOut++;
    }
    Console.WriteLine($"  {source}: {samples} frames, deepest player {deepest:0.0} m, ball outside the viewport in {ballOut}");
    Check(deepest <= BroadcastSpec.FramedDepth, $"a player stood {deepest:0.0} m back, beyond the framed {BroadcastSpec.FramedDepth} m");
    Check(ballOut * 100 <= samples, "ball leaves the view in at most 1% of frames");
});

MotionTrack Motion(MatchRecord rec) => BroadcastMotion.Classify(rec.Events);
int PlayerIndex(MatchEvent e) => Array.FindIndex(e.State.Players, p => p.Id == e.PlayerId);
Test("Motion: every player's spans are ordered and leave no gaps", () =>
{
    var rec = BroadcastRecord(out _); var track = Motion(rec);
    for (int p = 0; p < 2; p++)
    {
        var spans = track.Spans[p];
        Check(spans.Count > 0 && spans[0].Start == rec.Events[0].Time);
        for (int i = 0; i < spans.Count; i++)
        {
            Check(spans[i].Start <= spans[i].End, "start before end");
            if (i > 0) Check(spans[i].Start == spans[i - 1].End, $"gap at {spans[i].Start}");
        }
    }
});
Test("Motion: the contact frame is the BallHit time, and there is no strike without a BallHit", () =>
{
    foreach (var rec in new[] { BroadcastRecord(out _), Play(3, 0).Engine.Record })
    {
        var track = Motion(rec); var hits = rec.Events.Where(e => e.Kind == "BallHit").ToList();
        foreach (var h in hits)
        {
            var span = track.SpanAt(PlayerIndex(h), h.Time);
            Check(span.State == MotionState.Strike && span.ContactTime == h.Time && span.ActionId == h.ActionId && span.Stroke == h.Stroke, $"hit at {h.Time}");
        }
        Check(track.Spans.Sum(s => s.Count(x => x.State == MotionState.Strike)) == hits.Count, "one strike per BallHit");
    }
});
Test("Motion: PlayersRepositioned is a cut, never a walk", () =>
{
    var rec = BroadcastRecord(out _); var track = Motion(rec);
    var resets = rec.Events.Where(e => e.Kind == "PlayersRepositioned").Select(e => e.Time).ToList();
    Check(track.Cuts.SequenceEqual(resets), "a cut at every reset");
    foreach (var t in resets)
        for (int p = 0; p < 2; p++)
        {
            Check(track.Spans[p].Any(s => s.State == MotionState.Reposition && s.Start == t && s.End == t), "zero-length reposition");
            Check(!track.Spans[p].Any(s => s.Start < t && s.End > t), "no motion runs across a cut");
        }
    Check(track.Spans.All(ps => ps.Where(s => s.State == MotionState.Reposition).All(s => s.Start == s.End)));
});
Test("Motion: an unreachable contact shows no strike; a missed one only reaches", () =>
{
    var rec = BroadcastRecord(out _); var track = Motion(rec); int unreachable = 0, lunges = 0;
    foreach (var cp in rec.Events.Where(e => e.Kind == "ContactPrepared" && e.Reason == "UnreachableContact"))
    {
        unreachable++; int p = PlayerIndex(cp);
        Check(!track.Spans[p].Any(s => s.State == MotionState.Strike && s.ActionId == cp.ActionId), "no strike for action " + cp.ActionId);
        if (track.Spans[p].Any(s => s.State == MotionState.Miss && s.ActionId == cp.ActionId)) lunges++;
    }
    Check(rec.Events.Where(e => e.Kind == "BallHit").All(h => !track.Spans[PlayerIndex(h)].Any(s => s.State == MotionState.Miss && s.ActionId == h.ActionId)), "a ball that was struck never shows a miss first");
    Console.WriteLine($"  unreachable contacts {unreachable}, of which reached-for before the point ended {lunges}");
});
Test("Motion: the serve winds up from the reset, and the prepared stroke mostly matches the one played", () =>
{
    var rec = BroadcastRecord(out _); var track = Motion(rec);
    foreach (var h in rec.Events.Where(e => e.Kind == "BallHit" && e.Stroke == "Serve"))
    {
        int p = PlayerIndex(h); var spans = track.Spans[p]; int i = spans.FindIndex(s => s.State == MotionState.Strike && s.ContactTime == h.Time);
        Check(i > 0 && spans[i - 1].State == MotionState.Serve && track.Cuts.Contains(spans[i - 1].Start), "serve span from a reset at " + h.Time);
    }
    var strikes = track.Spans.SelectMany(s => s).Where(s => s.State == MotionState.Strike && s.Stroke != "Serve").ToList();
    var planned = strikes.Where(s => s.PlannedStroke != "").ToList();
    int same = planned.Count(s => s.PlannedStroke == s.Stroke);
    Console.WriteLine($"  prepared stroke matches the played one in {same}/{planned.Count} of {strikes.Count} rally strikes");
    Check(planned.Count == strikes.Count && same * 100 >= planned.Count * 95, "engine v6 announces the stroke for every rally shot");
});
Test("Motion: a live renderer sees the same states as the finished replay", () =>
{
    var rec = BroadcastRecord(out _); var full = Motion(rec); var ev = rec.Events;
    for (int k = 0; k < ev.Count; k += 37)
    {
        double cut = ev[k].Time;
        var live = BroadcastMotion.Classify(ev.Where(e => e.Time <= cut).ToList(), cut);
        foreach (double back in new[] { 0, .05, .2, .5, 1.5 })
        {
            double t = cut - back; if (t < ev[0].Time) continue;
            for (int p = 0; p < 2; p++) Check(live.StateAt(p, t) == full.StateAt(p, t), $"player {p} at {t:0.000} (live up to {cut:0.000}): {live.StateAt(p, t)} vs {full.StateAt(p, t)}");
        }
    }
});
Test("Motion: the broadcast layer leaves the record untouched", () =>
{
    var rec = BroadcastRecord(out _); string before = TennisSim.Cli.ReplayJson.Serialize(rec);
    Motion(rec); foreach (var f in rec.Frames) { cam.Ball(f.Ball.Position); foreach (var pl in f.Players) cam.Player(pl.Position); }
    Check(TennisSim.Cli.ReplayJson.Serialize(rec) == before);
});
Test("Serve course: opponent return position line in the design system's wording", () =>
{
    SegmentStats Seg(int points, double total) => new SegmentStats { Points = points, Players = new[] { new SegmentPlayerStats { ServePoints = points, ReceiverShiftPoints = points, ReceiverShiftTotal = total }, new SegmentPlayerStats() } };
    Check(CoachViews.ReturnPosition(Seg(7, 5.6), null).SequenceEqual(new[] { "상대 리턴 위치 · 이번 와이드 쪽 0.8 m", "내 서브 7포인트" }));
    Check(CoachViews.ReturnPosition(Seg(7, 5.6), Seg(6, .3)).SequenceEqual(new[] { "상대 리턴 위치 · 이번 와이드 쪽 0.8 m", "직전 가운데 · 내 서브 7포인트" }));
    Check(CoachViews.ReturnPosition(Seg(5, -1.5), Seg(0, 0)).SequenceEqual(new[] { "상대 리턴 위치 · 이번 T 쪽 0.3 m", "직전 — · 내 서브 5포인트" }));
    Check(CoachViews.ReturnPosition(Seg(0, 0), Seg(6, 3)).SequenceEqual(new[] { "상대 리턴 위치 · 이번 —", "직전 와이드 쪽 0.5 m · 내 서브 0포인트" }), "no serve of mine this segment");
    Check(CoachViews.ServePanel(Seg(5, 1)).Notes[0].Muted && !CoachViews.ServePanel(Seg(6, 1)).Notes[0].Muted, "line 1 muted below 6 serve points");
    Check(CoachViews.ServePanel(Seg(6, 1)).Notes[1].Muted, "line 2 is context, always muted");
    // In a real coached match the line follows the recorded receiver positions.
    var views = new List<ChangeoverView>(); var s = Play(3, 0, views);
    for (int i = 0; i < views.Count; i++)
    {
        var seg = s.Segments[i]; var now = SegmentStats.Compute(s.Engine.Record, seg.FromPoint, seg.ToPoint);
        var prev = i > 0 ? SegmentStats.Compute(s.Engine.Record, s.Segments[i - 1].FromPoint, s.Segments[i - 1].ToPoint) : null;
        Check(views[i].Serve.Notes.Select(x => x.Text).SequenceEqual(CoachViews.ReturnPosition(now, prev)) && views[i].Serve.Notes[0].Text.StartsWith("상대 리턴 위치 · 이번 "), views[i].Serve.Notes[0].Text);
        Check(views[i].Compare.Notes[0].Muted, "average rally stays muted");
    }
    Console.WriteLine("  sample: " + string.Join(" / ", views[^1].Serve.Notes.Select(x => x.Text)));
});
Test("Serve course rows carry the match so far per course beside this segment", () =>
{
    var views = new List<ChangeoverView>(); var s = Play(3, 0, views);
    for (int i = 0; i < views.Count; i++)
    {
        var seg = s.Segments[i]; var match = SegmentStats.Compute(s.Engine.Record, 1, seg.ToPoint).Players[0];
        var p = views[i].Serve;
        if (i == 0)
        {
            // The first changeover's match so far is this segment: only the "이번 구간" group.
            Check(p.Groups.SequenceEqual(new[] { "이번 구간" }) && p.MatchColumns.Length == 0 && p.Rows.All(r => r.MatchValues == null), "first changeover: one group");
            continue;
        }
        Check(p.Groups.SequenceEqual(new[] { "이번 구간", "경기 누적" }) && p.MatchColumns.SequenceEqual(new[] { "첫 서브", "득점" }));
        var courses = new[] { match.WideServe, match.BodyServe, match.TServe };
        for (int k = 0; k < 3; k++)
        {
            var c = courses[k]; var row = p.Rows[k];
            string Ratio(int a, int n) => n == 0 ? "—" : a + "/" + n;
            Check(row.MatchValues.SequenceEqual(new[] { Ratio(c.FirstServesIn, c.Points), Ratio(c.Won, c.Points) }), row.Label + " match values");
            // The match group follows only its own sample, even when the segment panel is tagged (design system v39).
            Check(row.MatchMuted == (c.Points > 0 && c.Points < CoachViews.MinPoints), row.Label + " match muted");
        }
        // The match column covers this segment: its serve points are at least the segment's.
        int segPoints = s.Engine.Record == null ? 0 : SegmentStats.Compute(s.Engine.Record, seg.FromPoint, seg.ToPoint).Players[0].ServePoints;
        Check(match.WideServe.Points + match.BodyServe.Points + match.TServe.Points >= segPoints);
    }
    Check(CoachViews.ServePanel(SegmentStats.Compute(s.Engine.Record, 1, 10)).MatchColumns.Length == 0, "no match columns without match stats");
});
Test("A tagged serve panel mutes the segment group but not a match group with its own sample", () =>
{
    SegmentStats Serves(int wide, int t) => new SegmentStats { Points = wide + t, Players = new[] { new SegmentPlayerStats
    {
        ServePoints = wide + t, WideServe = new ServeCourseStats { Points = wide, FirstServesIn = wide, Won = wide / 2 }, TServe = new ServeCourseStats { Points = t, FirstServesIn = t, Won = t }
    }, new SegmentPlayerStats() } };
    var p = CoachViews.ServePanel(Serves(3, 1), Serves(4, 2), Serves(10, 3));
    Check(p.SampleTag && p.Rows.All(r => r.Muted), "segment of 4 serve points: tagged, segment values muted");
    Check(!p.Rows[0].MatchMuted && p.Rows[0].MatchValues[0] == "10/10", "wide: 10 match points, not muted by the segment tag");
    Check(p.Rows[2].MatchMuted, "T: 3 match points, muted by its own sample");
    Check(!p.Rows[1].MatchMuted && p.Rows[1].MatchValues[0] == "—", "body: unused, a dash, not muted");
});
Test("Chip descriptions: one source for pre-match and changeover, serve lean named for wide and T", () =>
{
    var s = Play(3, 0); var views = new List<ChangeoverView>(); Play(3, 0, views);
    var pre = CoachViews.PreMatch(s.Input).Groups.SelectMany(g => g.Options).ToList();
    var at = views[0].Groups.SelectMany(g => g.Options).ToList();
    Check(pre.Select(o => o.Label + o.Description).SequenceEqual(at.Select(o => o.Label + o.Description)) && at.All(o => o.Description.Length > 0), "same sentences on both screens");
    Check(at.Single(o => o.Label == "와이드").Description == "바깥쪽 집중. 반복하면 상대가 와이드 쪽으로 옮겨 서고 리턴이 빨라집니다.");
    Check(at.Single(o => o.Label == "T").Description == "가운데 집중. 반복하면 상대가 T 쪽으로 옮겨 서고 리턴이 빨라집니다.");
    Check(at.Single(o => o.Label == "바디").Description == "몸쪽 집중. 반복하면 리턴이 빨라집니다.", "body has no lean");
    Check(CoachViews.ChoiceHint == "칩에 마우스를 올리면 설명이 나옵니다.");
});
Test("Top speed at the lowest energy follows Movement.SpeedLimit as a whole percent", () =>
{
    var p = new PlayerProfile { MaxSpeed = 6.2 };
    Check(CoachViews.SpeedLoss(p, 1) == "변화 없음" && CoachViews.SpeedLoss(p, .98) == "변화 없음", "under half a percent reads as no change");
    Check(CoachViews.SpeedLoss(p, .85) == "−3%", CoachViews.SpeedLoss(p, .85));
    Check(CoachViews.SpeedLoss(p, .15) == "−17%", CoachViews.SpeedLoss(p, .15));
    Check(CoachViews.SpeedLoss(p, -1) == "—", "no frames");
});
Console.WriteLine($"COACH_CHECKS passed={passed} failed={failed}");
return failed == 0 ? 0 : 1;

static class Extensions
{
    public static List<CoachReason> Reasons(this CoachSegment s) => s.OpponentChangeAtEnd.Reasons;
}
