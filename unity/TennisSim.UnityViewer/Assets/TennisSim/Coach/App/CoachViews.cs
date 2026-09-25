using System;
using System.Collections.Generic;
using System.Linq;
using TennisSim.Core;

namespace TennisSim.Coach
{
    public sealed class AttributeRow { public string Label; public string Value; public float Fraction; }
    public sealed class TacticOption { public string Key; public string Value; public string Label; public string Description; }
    public sealed class TacticGroup { public string Key; public string Label; public List<TacticOption> Options = new List<TacticOption>(); }
    public sealed class StatRow { public string Label; public string A; public string B; public string MatchA; public string MatchB; }
    public sealed class FeedItem { public int Point; public int Winner; public string Text; }
    // BackhandTarget: the shot chose the opponent's backhand side. TacticBackhand: the hitter's tactic at that moment was
    // TargetBackhand (for the review filter "백핸드 공략일 때" / "양쪽일 때").
    public sealed class Landing { public float X; public float Z; public bool BackhandTarget; public bool TacticBackhand; public bool In; }
    public sealed class LandingFilter { public string Key; public string Label; }
    // Changeover evidence (design system changeover.md). A row's Values follow the panel's Columns; Muted marks a row
    // whose values are below their sample threshold. Note is an optional second line under the row label ("그 외").
    // A SplitRow is one line of the SplitBar: the opponent's backhand share. MatchValues (optional) follow
    // MatchColumns: the same measure over the match so far, beside this segment's Values.
    // Muting is one rule (changeover.md "표본 크기"): only values are muted, never labels, headers, titles or badges.
    // A panel below its threshold gets the SampleTag and every value from the tagged sample (this segment) muted; a row
    // or group below mutes just its values. Another sample's group (match so far) follows only its own threshold.
    public sealed class EvidenceRow { public string Label; public string Note; public string[] Values; public bool Muted; public string[] MatchValues; public bool MatchMuted; }
    public sealed class SplitRow { public string Label; public float Backhand; public string LeftText; public string RightText; public bool Muted; }
    // A body line under the table: a value that is not per player (average rally) or a signal (return position).
    public sealed class NoteLine { public string Text; public bool Muted; }
    public sealed class EvidencePanel
    {
        public string Title; public string Sample; public bool SampleTag;
        public List<NoteLine> Notes = new List<NoteLine>();
        public string[] Columns = new string[0];
        public string[] MatchColumns = new string[0];
        // Group header over the value columns: Groups[0] spans Columns, Groups[1] spans MatchColumns. Empty: no group row.
        public string[] Groups = new string[0];
        // Value cells at the compact width (72px) for the whole table.
        public bool Compact;
        public List<SplitRow> Split = new List<SplitRow>();
        public List<EvidenceRow> Rows = new List<EvidenceRow>();

        // The panel is below its threshold: every value from its sample, split line and note is muted. MatchMuted is
        // left alone: when the segment is small, the match so far is what the coach leans on (design system v39).
        public EvidencePanel MuteAll()
        {
            foreach (var r in Rows) r.Muted = true;
            foreach (var s in Split) s.Muted = true;
            foreach (var n in Notes) n.Muted = true;
            return this;
        }
    }
    public sealed class OpponentChangeRow { public string Kicker; public string Change; public string Reasons; }
    public sealed class SegmentRow { public string Games; public int FirstGame; public int LastGame; public string TacticA; public string TacticB; public int Won; public int Points; }

    public sealed class PreMatchView
    {
        public string[] Names;
        public List<AttributeRow>[] Attributes;
        public string ScoutingMemo;
        public string OpponentCoachNote;
        public List<TacticGroup> Groups;
    }

    public sealed class MatchView
    {
        public string[] Names;
        public int[] Games;
        public string[] PointText;
        public int Server;
        // Court positions in metres, engine frame: X across the court, Z along it, ball height Y.
        public float[] PlayerX, PlayerZ;
        public float BallX, BallY, BallZ;
        public bool BallVisible;
        public string[] Tactics;
        public List<FeedItem> Feed;
        public int Point;
        public int Game;
        public List<int> ChangeoverPoints;
    }

    public sealed class ChangeoverView
    {
        public string[] Names;
        public string Heading;
        public int[] Games;
        public bool OpponentChanged;
        // A banner is due: the opponent coach changed its tactic or noted the user's change (OpponentKicker/Text).
        public bool OpponentBanner;
        public string OpponentKicker;
        public string OpponentText;
        // One evidence panel per tactic axis: attack direction, serve course, and aggression as a segment comparison.
        // Compare has four columns (A previous, A now, B previous, B now) after the first changeover, else two.
        public EvidencePanel Direction, Serve, Compare;
        public bool HasPrevious;
        public Tactic CurrentA;
        public string CurrentAText;
        public string NextChangeover;
        public List<TacticGroup> Groups;
    }

    public sealed class ReviewView
    {
        public string[] Names;
        public int[] Games;
        public int Winner;
        public int Points;
        public List<SegmentRow> Segments;
        public List<int> GameWinners;
        public List<StatRow> Summary;
        // Body lines under the whole-match table (line-muted): average rally, top speed when most tired.
        public List<string> SummaryNotes;
        public List<Landing> Landings;
        public int BackhandTargets;
        public int Outs;
        // Each opponent-coach change with the games it followed and its reasons, in match order; NoOpponentChange when
        // there was none. LandingFilters lists "전체" and each attack-direction tactic actually used.
        public List<OpponentChangeRow> OpponentChanges;
        public string NoOpponentChange;
        public List<LandingFilter> LandingFilters;
    }

    public static class CoachViews
    {
        // The changeover's choice description box before any chip is hovered, focused or clicked (line-muted).
        public const string ChoiceHint = "칩에 마우스를 올리면 설명이 나옵니다.";

        // One source for the chip descriptions: the pre-match chips show them inline, the changeover in its box.
        public static List<TacticGroup> TacticGroups(bool withDescriptions)
        {
            TacticOption O(string key, string value, string label, string description) => new TacticOption { Key = key, Value = value, Label = label, Description = withDescriptions ? description : "" };
            return new List<TacticGroup>
            {
                new TacticGroup { Key = "target", Label = "공격 방향", Options = {
                    O("target", "balanced", "양쪽", "양쪽 사이드로 고르게 벌립니다."),
                    O("target", "backhand", "백핸드 공략", "상대 백핸드 쪽으로 몰아칩니다. 백핸드가 약한 상대에게 유리합니다.") } },
                new TacticGroup { Key = "aggression", Label = "공격성", Options = {
                    O("aggression", "safe", "안전", "실수가 적고 빠른 공을 잘 받아냅니다. 느린 공을 줘서 공격당하기 쉽습니다."),
                    O("aggression", "balanced", "균형", "무난한 기본값입니다."),
                    O("aggression", "aggressive", "공격", "쉬운 공을 강하게 응징합니다. 빠른 공에는 실수가 급증합니다.") } },
                new TacticGroup { Key = "serve", Label = "서브 코스", Options = {
                    O("serve", "mixed", "혼합", "코스를 섞어 읽히지 않습니다."),
                    O("serve", "wide", "와이드", "바깥쪽 집중. 반복하면 상대가 와이드 쪽으로 옮겨 서고 리턴이 빨라집니다."),
                    O("serve", "body", "바디", "몸쪽 집중. 반복하면 리턴이 빨라집니다."),
                    O("serve", "t", "T", "가운데 집중. 반복하면 상대가 T 쪽으로 옮겨 서고 리턴이 빨라집니다.") } }
            };
        }

        public static bool IsSelected(Tactic t, TacticOption o)
        {
            switch (o.Key)
            {
                case "target": return (t.Target == TargetStyle.TargetBackhand) == (o.Value == "backhand");
                case "aggression": return t.Aggression.ToString().Equals(o.Value, StringComparison.OrdinalIgnoreCase);
                default: return t.Serve.ToString().Equals(o.Value, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static Tactic With(Tactic t, TacticOption o)
        {
            var next = t.Copy();
            switch (o.Key)
            {
                case "target": next.Target = o.Value == "backhand" ? TargetStyle.TargetBackhand : TargetStyle.Balanced; break;
                case "aggression": next.Aggression = (Aggression)Enum.Parse(typeof(Aggression), o.Value, true); break;
                default: next.Serve = (ServeDirection)Enum.Parse(typeof(ServeDirection), o.Value, true); break;
            }
            return next;
        }

        static string[] Names(MatchInput input) => new[] { input.Players[0].Name, input.Players[1].Name };

        public static PreMatchView PreMatch(MatchInput input)
        {
            List<AttributeRow> Rows(PlayerProfile p)
            {
                AttributeRow Skill(string label, double v) => new AttributeRow { Label = label, Value = CoachText.Fixed(v, 2), Fraction = (float)v };
                return new List<AttributeRow>
                {
                    Skill("서브 파워", p.ServePower), Skill("서브 정확도", p.ServeControl),
                    Skill("포핸드 파워", p.ForehandPower), Skill("포핸드 정확도", p.ForehandControl),
                    Skill("백핸드 파워", p.BackhandPower), Skill("백핸드 정확도", p.BackhandControl),
                    new AttributeRow { Label = "최고 속도", Value = CoachText.Fixed(p.MaxSpeed, 1) + " m/s", Fraction = (float)Math.Min(1, p.MaxSpeed / 8) },
                    new AttributeRow { Label = "반응 시간", Value = CoachText.Fixed(p.ReactionSeconds, 2) + " s", Fraction = (float)Math.Max(0, (.4 - p.ReactionSeconds) / .4) },
                    Skill("체력", p.Stamina)
                };
            }
            // No particle right after a player name (design system v45 "말투와 표기"): its form depends on how the name is
            // read (Ember는, Rook은), so sentences say "Rook 선수는".
            var o = input.Players[1];
            double fh = o.ForehandPower + o.ForehandControl, bh = o.BackhandPower + o.BackhandControl;
            string detail = " (파워 " + CoachText.Fixed(o.BackhandPower, 2) + " 대 " + CoachText.Fixed(o.ForehandPower, 2) + ", 정확도 " + CoachText.Fixed(o.BackhandControl, 2) + " 대 " + CoachText.Fixed(o.ForehandControl, 2) + ")";
            string memo = bh - fh > .1 ? o.Name + " 선수는 백핸드가 포핸드보다 강합니다" + detail + ". 백핸드 공략이 통하지 않을 수 있습니다."
                : fh - bh > .1 ? o.Name + " 선수는 백핸드가 약점입니다" + detail + "."
                : o.Name + " 선수는 포핸드와 백핸드가 비슷합니다" + detail + ".";
            return new PreMatchView
            {
                Names = Names(input), Attributes = new[] { Rows(input.Players[0]), Rows(o) }, ScoutingMemo = memo,
                OpponentCoachNote = "상대 코치(AI)도 체인지오버마다 전술을 바꿉니다.", Groups = TacticGroups(true)
            };
        }

        public static MatchView Match(CoachSession session, int feedLength = 5)
        {
            var state = session.Engine.State;
            var names = Names(session.Input);
            var feed = new List<FeedItem>();
            for (int i = session.Points.Count - 1; i >= 0 && feed.Count < feedLength; i--)
            {
                var p = session.Points[i];
                feed.Add(new FeedItem { Point = p.Point, Winner = p.Winner, Text = names[p.Winner] + " 득점 · " + CoachText.Cause(p, names) });
            }
            return new MatchView
            {
                Names = names, Games = (int[])state.Score.Games.Clone(), PointText = CoachText.PointScore(state.Score), Server = state.Score.Server,
                PlayerX = state.Players.Select(p => (float)p.Position.X).ToArray(), PlayerZ = state.Players.Select(p => (float)p.Position.Z).ToArray(),
                BallX = (float)state.Ball.Position.X, BallY = (float)state.Ball.Position.Y, BallZ = (float)state.Ball.Position.Z,
                BallVisible = state.Phase == "Rally" || state.Phase == "ServePreparation",
                Tactics = state.Tactics.Select(CoachText.Tactic).ToArray(), Feed = feed,
                Point = state.Score.PointsPlayed + 1, Game = state.Score.Games[0] + state.Score.Games[1] + 1,
                ChangeoverPoints = session.Segments.Take(session.Segments.Count - 1).Select(s => s.ToPoint).ToList()
            };
        }

        // The review's whole-match table (design system changeover.md "리뷰 화면: 경기 전체 기록"): the changeover
        // comparison rows in the same order and names, plus double faults, with the range swapped to the whole match.
        // A whole match is always above the sample thresholds, so nothing is muted.
        static List<StatRow> Rows(SegmentStats s)
        {
            StatRow R(string label, Func<SegmentPlayerStats, string> f) => new StatRow { Label = label, A = f(s.Players[0]), B = f(s.Players[1]) };
            return new List<StatRow>
            {
                R("득점", p => p.PointsWon.ToString()),
                R("서브 득점", p => CoachText.Ratio(p.ServePointsWon, p.ServePoints)),
                R("첫 서브 성공", p => CoachText.Ratio(p.FirstServesIn, p.ServePoints)),
                R("더블 폴트", p => p.DoubleFaults.ToString()),
                R("위너", p => p.Winners.ToString()),
                R("타구 포핸드/백핸드", p => p.Forehands + "/" + p.Backhands),
                R("에러 포핸드/백핸드", p => p.ForehandErrors + "/" + p.BackhandErrors),
                // Energy at the end is back near 1 (recovery between points), so the row shows the match's lowest.
                R("체력(경기 최저)", p => p.EnergyMin < 0 ? CoachText.None : CoachText.Fixed(p.EnergyMin, 2))
            };
        }

        public static ChangeoverView Changeover(CoachSession session)
        {
            if (session.Phase != CoachPhase.Changeover) throw new InvalidOperationException("Not at a changeover");
            var seg = session.Current;
            var names = Names(session.Input);
            var s = SegmentStats.Compute(session.Engine.Record, seg.FromPoint, seg.ToPoint);
            var m = SegmentStats.Compute(session.Engine.Record, 1, seg.ToPoint);
            var state = session.Engine.State;
            string games = seg.FirstGame == seg.LastGame ? "게임 " + seg.FirstGame : "게임 " + seg.FirstGame + "–" + seg.LastGame;
            var view = new ChangeoverView
            {
                Names = names, Games = (int[])state.Score.Games.Clone(),
                Heading = "체인지오버 · " + games + " 구간 (포인트 " + seg.FromPoint + "–" + seg.ToPoint + ") · " + s.Points + "포인트",
                CurrentA = state.Tactics[0].Copy(), CurrentAText = CoachText.Tactic(state.Tactics[0]),
                NextChangeover = state.Score.TieBreak ? "다음 체인지오버: 6포인트 후" : "다음 체인지오버: 2게임 후",
                Groups = TacticGroups(true)
            };
            var change = session.OpponentChange;
            if (change != null)
            {
                view.OpponentChanged = view.OpponentBanner = true;
                view.OpponentKicker = names[1] + " 코치가 전술을 바꿨습니다";
                view.OpponentText = ChangeParts(state.Tactics[1], change.Tactic) + " (다음 포인트부터). " + ChangeReasons(change, names) + NoteText(change, names);
                // The direction sentence only when the attack direction really changes; a note alone never gets it.
                var effect = DirectionEffect(state.Tactics[1], change.Tactic, names[0]);
                if (effect != null) view.OpponentText += " " + effect;
            }
            else if (session.OpponentNote != null)
            {
                view.OpponentBanner = true;
                view.OpponentKicker = names[1] + " 코치가 지켜보고 있습니다";
                view.OpponentText = NoteText(session.OpponentNote, names).TrimStart();
            }
            int prevIndex = session.Segments.Count - 2;
            var prev = prevIndex >= 0 ? SegmentStats.Compute(session.Engine.Record, session.Segments[prevIndex].FromPoint, session.Segments[prevIndex].ToPoint) : null;
            view.HasPrevious = prev != null;
            view.Direction = DirectionPanel(s, prev);
            view.Serve = ServePanel(s, prev, m);
            view.Compare = ComparePanel(s, prev, session.Input);
            return view;
        }

        // Sample thresholds (design system changeover.md): point-based values under 6 points and shot-based values under
        // 12 shots (OpponentCoach's minimum strokes) are shown muted, with a "참고용" tag when the whole panel is under.
        public const int MinPoints = 6, MinShots = 12;

        public static string ChangeParts(Tactic before, Tactic after) => string.Join(", ", ChangeList(before, after));
        // What an opponent's attack-direction change does to my player, and where to see it (changeover.md "상대가 공격
        // 방향을 바꿀 때의 배너 문장"). No advice: no single answer fits every opponent. Null when the direction is kept.
        public static string DirectionEffect(Tactic before, Tactic after, string me)
        {
            if (before.Target == after.Target) return null;
            string more = after.Target == TargetStyle.TargetBackhand ? "늘어납니다" : "줄어듭니다";
            return "이제 " + me + " 선수가 백핸드로 받는 공이 " + more + ". 구간 비교 표의 타구 포핸드/백핸드에서 확인할 수 있습니다.";
        }
        static string NoteText(CoachDecision d, string[] names) => string.Concat(d.Notes.Select(r => " " + CoachText.Reason(r, names[0])));
        static string ChangeReasons(CoachDecision change, string[] names) => string.Join(" ", change.Reasons.Select(r => CoachText.Reason(r, names[0])));

        static string Count(int k, int n) => n == 0 ? CoachText.None : CoachText.Ratio(k, n);

        static SplitRow Split(string label, SegmentPlayerStats opponent)
        {
            int bh = opponent.Backhands, n = opponent.Forehands + opponent.Backhands;
            if (n == 0) return new SplitRow { Label = label, Backhand = 0, LeftText = "백핸드 " + CoachText.None, RightText = "포핸드 " + CoachText.None, Muted = true };
            double share = (double)bh / n;
            return new SplitRow { Label = label, Backhand = (float)share, LeftText = "백핸드 " + CoachText.Percent(share) + " · " + CoachText.Ratio(bh, n), RightText = "포핸드 " + CoachText.Percent(1 - share), Muted = n < MinShots };
        }

        public const string OtherAimNote = "빈 곳 · 강타 · 깊게";

        // Attack direction: where the opponent actually hit from (SplitBar, previous and now) and what my shots aimed at
        // each side produced this segment, as counts.
        public static EvidencePanel DirectionPanel(SegmentStats now, SegmentStats prev)
        {
            var me = now.Players[0];
            int shots = me.BackhandAim.Shots + me.ForehandAim.Shots + me.OtherAim.Shots;
            var panel = new EvidencePanel { Title = "공격 방향", Sample = "이번 구간 " + shots + "구", SampleTag = shots < MinShots, Columns = new[] { "타구", "상대 에러", "내 위너" } };
            if (prev != null) panel.Split.Add(Split("직전", prev.Players[1]));
            panel.Split.Add(Split("이번", now.Players[1]));
            EvidenceRow Aim(string label, AimStats a) => new EvidenceRow { Label = label, Values = new[] { a.Shots.ToString(), a.ReplyErrors.ToString(), a.Winners.ToString() }, Muted = a.Shots < MinShots };
            panel.Rows.Add(Aim("백핸드 쪽", me.BackhandAim));
            panel.Rows.Add(Aim("포핸드 쪽", me.ForehandAim));
            // "그 외" is usually over half the shots, so the row says what it holds: OpenCourt, Attack, SafeDeep.
            // Attack reads "강타" because "공격" is an aggression chip.
            var other = Aim("그 외", me.OtherAim); other.Note = OtherAimNote;
            panel.Rows.Add(other);
            return panel.SampleTag ? panel.MuteAll() : panel;
        }

        // Serve course: my serve points by the first serve's course. Two column groups, "이번 구간" and "경기 누적", each
        // "첫 서브" (first serves in / serves) and "득점" (points won / serves): the denominator is the serve count, so
        // there is no separate serve column. The match group is the expectation for switching courses, since one
        // segment is only a few serves per course; at the first changeover it equals the segment and is left out.
        // Ratios as k/n, never %; an unused course stays as "—".
        public static EvidencePanel ServePanel(SegmentStats now, SegmentStats prev = null, SegmentStats match = null)
        {
            var me = now.Players[0];
            bool withMatch = match != null && prev != null;
            var panel = new EvidencePanel
            {
                Title = "서브 코스", Sample = "내 서브 " + me.ServePoints + "포인트", SampleTag = me.ServePoints < MinPoints, Compact = true,
                Columns = new[] { "첫 서브", "득점" }, Groups = withMatch ? new[] { "이번 구간", "경기 누적" } : new[] { "이번 구간" }
            };
            if (withMatch) panel.MatchColumns = new[] { "첫 서브", "득점" };
            string[] Values(ServeCourseStats c) => new[] { Count(c.FirstServesIn, c.Points), Count(c.Won, c.Points) };
            bool Few(ServeCourseStats c) => c.Points > 0 && c.Points < MinPoints;
            EvidenceRow Course(string label, ServeCourseStats c, ServeCourseStats total) => new EvidenceRow
            {
                Label = label, Values = Values(c), Muted = Few(c),
                MatchValues = withMatch ? Values(total) : null, MatchMuted = withMatch && Few(total)
            };
            var all = match?.Players[0];
            panel.Rows.Add(Course(CoachText.Serve(ServeDirection.Wide), me.WideServe, all?.WideServe));
            panel.Rows.Add(Course(CoachText.Serve(ServeDirection.Body), me.BodyServe, all?.BodyServe));
            panel.Rows.Add(Course(CoachText.Serve(ServeDirection.T), me.TServe, all?.TServe));
            // Whether the opponent is reading my serve: where the receiver stood against it (changeover.md "2. 서브 코스
            // 패널"). Line 1 is a decision value, so line colour, muted below 6 of my serve points; line 2 is context.
            var lines = ReturnPosition(now, prev);
            panel.Notes.Add(new NoteLine { Text = lines[0], Muted = me.ReceiverShiftPoints < MinPoints });
            panel.Notes.Add(new NoteLine { Text = lines[1], Muted = true });
            return panel.SampleTag ? panel.MuteAll() : panel;
        }

        // Two lines: "상대 리턴 위치 · 이번 와이드 쪽 0.8 m" and "직전 가운데 · 내 서브 7포인트" ("내 서브 7포인트" alone at
        // the first changeover). The side is named like the chips, metres to one decimal, a mean under 0.1 m reads as
        // "가운데", and a segment without my serves reads "—". The point count is this segment's.
        public static string[] ReturnPosition(SegmentStats now, SegmentStats prev)
        {
            string Side(SegmentPlayerStats p)
            {
                if (p.ReceiverShiftPoints == 0) return CoachText.None;
                double m = p.MeanReceiverShift;
                if (Math.Abs(m) < .1) return "가운데";
                return (m > 0 ? CoachText.Serve(ServeDirection.Wide) : CoachText.Serve(ServeDirection.T)) + " 쪽 " + CoachText.Fixed(Math.Abs(m), 1) + " m";
            }
            var me = now.Players[0];
            string count = "내 서브 " + me.ReceiverShiftPoints + "포인트";
            return new[] { "상대 리턴 위치 · 이번 " + Side(me), prev == null ? count : "직전 " + Side(prev.Players[0]) + " · " + count };
        }

        // Aggression: this segment beside the previous one for both players (A previous, A now, B previous, B now), or
        // just the two "now" columns at the first changeover. Match totals belong to the review. With the match input,
        // a last line gives each player's top speed at their lowest energy against full energy.
        public static EvidencePanel ComparePanel(SegmentStats now, SegmentStats prev, MatchInput input = null)
        {
            var panel = new EvidencePanel { Title = "공격성 · 구간 비교", Sample = "이번 구간 " + now.Points + "포인트", SampleTag = now.Points < MinPoints };
            panel.Columns = prev != null ? new[] { "직전", "이번", "직전", "이번" } : new[] { "이번", "이번" };
            EvidenceRow R(string label, Func<SegmentStats, SegmentPlayerStats, string> f)
            {
                var values = prev != null
                    ? new[] { f(prev, prev.Players[0]), f(now, now.Players[0]), f(prev, prev.Players[1]), f(now, now.Players[1]) }
                    : new[] { f(now, now.Players[0]), f(now, now.Players[1]) };
                return new EvidenceRow { Label = label, Values = values };
            }
            panel.Rows.Add(R("득점", (x, p) => p.PointsWon.ToString()));
            panel.Rows.Add(R("서브 득점", (x, p) => Count(p.ServePointsWon, p.ServePoints)));
            panel.Rows.Add(R("첫 서브 성공", (x, p) => Count(p.FirstServesIn, p.ServePoints)));
            panel.Rows.Add(R("위너", (x, p) => p.Winners.ToString()));
            // Rally strokes each player hit: an opponent aiming at my backhand shows here, beside the errors below.
            panel.Rows.Add(R("타구 포핸드/백핸드", (x, p) => p.Forehands + "/" + p.Backhands));
            panel.Rows.Add(R("에러 포핸드/백핸드", (x, p) => p.ForehandErrors + "/" + p.BackhandErrors));
            // Energy at the end of a segment is back near 1 (recovery between points), so the row shows the lowest.
            panel.Rows.Add(R("체력(구간 최저)", (x, p) => p.EnergyMin < 0 ? CoachText.None : CoachText.Fixed(p.EnergyMin, 2)));
            // Average rally belongs to the segment, not to a player: one line under the table, not two equal cells.
            string Rally(SegmentStats x) => CoachText.Fixed(x.MeanRallyLength, 1) + "구";
            panel.Notes.Add(new NoteLine { Text = prev != null ? "평균 랠리 · 직전 " + Rally(prev) + " → 이번 " + Rally(now) : "평균 랠리 · 이번 " + Rally(now), Muted = true });
            if (input != null)
                panel.Notes.Add(new NoteLine { Text = "가장 지쳤을 때 최고 속도 · " + string.Join(" · ", Enumerable.Range(0, 2).Select(i => input.Players[i].Name + " " + SpeedLoss(input.Players[i], now.Players[i].EnergyMin))), Muted = true });
            return panel.SampleTag ? panel.MuteAll() : panel;
        }

        // Movement.SpeedLimit at the lowest energy against full energy, as a whole percent ("−3%"), "변화 없음" at 0.
        // Shot error also grows with fatigue, but it has no percent, so it is not shown.
        public static string SpeedLoss(PlayerProfile profile, double energyMin)
        {
            if (energyMin < 0) return CoachText.None;
            double ratio = Movement.SpeedLimit(profile, new PlayerState { Energy = energyMin }) / Movement.SpeedLimit(profile, new PlayerState { Energy = 1 }) - 1;
            int percent = (int)Math.Round(ratio * 100, MidpointRounding.AwayFromZero);
            return percent == 0 ? "변화 없음" : (percent < 0 ? "−" : "+") + Math.Abs(percent) + "%";
        }

        // The change summary under the chips: one changed axis per line ("공격성 안전 → 공격"), at most three, or one line
        // for what is kept. "다음 포인트부터" is the side panel's subtitle, not repeated here.
        public static string[] ChangeSummary(Tactic current, Tactic pending) =>
            CoachSession.Same(current, pending) ? new[] { "변경 없음: " + CoachText.Tactic(current) + " 유지" } : ChangeList(current, pending).ToArray();

        static List<string> ChangeList(Tactic before, Tactic after)
        {
            var parts = new List<string>();
            if (before.Target != after.Target) parts.Add("공격 방향 " + CoachText.Target(before.Target) + " → " + CoachText.Target(after.Target));
            if (before.Aggression != after.Aggression) parts.Add("공격성 " + CoachText.Aggression(before.Aggression) + " → " + CoachText.Aggression(after.Aggression));
            if (before.Serve != after.Serve) parts.Add("서브 코스 " + CoachText.Serve(before.Serve) + " → " + CoachText.Serve(after.Serve));
            return parts;
        }

        public static ReviewView Review(CoachSession session)
        {
            if (session.Phase != CoachPhase.Finished) throw new InvalidOperationException("Match not finished");
            var record = session.Engine.Record;
            var names = Names(session.Input);
            var whole = SegmentStats.Compute(record, 1, record.FinalScore.PointsPlayed);
            var segments = session.Segments.Select(seg =>
            {
                int won = session.Points.Count(p => p.Point >= seg.FromPoint && p.Point <= seg.ToPoint && p.Winner == 0);
                return new SegmentRow
                {
                    Games = seg.FirstGame == seg.LastGame ? "게임 " + seg.FirstGame : "게임 " + seg.FirstGame + "–" + seg.LastGame,
                    FirstGame = seg.FirstGame, LastGame = seg.LastGame,
                    TacticA = CoachText.Short(seg.TacticA), TacticB = CoachText.Short(seg.TacticB), Won = won, Points = seg.ToPoint - seg.FromPoint + 1
                };
            }).ToList();
            var gameWinners = new List<int>();
            int ga = 0, gb = 0;
            foreach (var p in session.Points)
            {
                if (p.GamesA > ga) gameWinners.Add(0); else if (p.GamesB > gb) gameWinners.Add(1);
                ga = p.GamesA; gb = p.GamesB;
            }
            var summary = Rows(whole);
            // Values shared by both players go under the table, in the changeover's order: average rally, then top speed.
            var summaryNotes = new List<string>
            {
                "평균 랠리 · " + CoachText.Fixed(whole.MeanRallyLength, 1) + "구",
                "가장 지쳤을 때 최고 속도 · " + string.Join(" · ", Enumerable.Range(0, 2).Select(i => session.Input.Players[i].Name + " " + SpeedLoss(session.Input.Players[i], whole.Players[i].EnergyMin)))
            };
            return new ReviewView
            {
                Names = names, Games = (int[])record.FinalScore.Games.Clone(), Winner = record.FinalScore.Winner, Points = record.FinalScore.PointsPlayed,
                Segments = segments, GameWinners = gameWinners, Summary = summary, SummaryNotes = summaryNotes, Landings = Landings(record, 0),
                OpponentChanges = session.Segments.Where(g => g.OpponentChangeAtEnd != null).Select(g => new OpponentChangeRow
                {
                    Kicker = "게임 " + g.LastGame + " 뒤", Change = ChangeParts(g.TacticB, g.OpponentChangeAtEnd.Tactic), Reasons = ChangeReasons(g.OpponentChangeAtEnd, names)
                }).ToList(),
                NoOpponentChange = names[1] + " 코치는 전술을 바꾸지 않았습니다."
            }.WithCounts();
        }

        static ReviewView WithCounts(this ReviewView v)
        {
            v.BackhandTargets = v.Landings.Count(l => l.BackhandTarget);
            v.Outs = v.Landings.Count(l => !l.In);
            // "전체" plus each attack-direction tactic that was actually in force for some shot.
            v.LandingFilters = new List<LandingFilter> { new LandingFilter { Key = "all", Label = "전체" } };
            if (v.Landings.Any(l => l.TacticBackhand)) v.LandingFilters.Add(new LandingFilter { Key = "backhand", Label = CoachText.Target(TargetStyle.TargetBackhand) + "일 때" });
            if (v.Landings.Any(l => !l.TacticBackhand)) v.LandingFilters.Add(new LandingFilter { Key = "balanced", Label = CoachText.Target(TargetStyle.Balanced) + "일 때" });
            return v;
        }

        public static List<Landing> Filter(List<Landing> landings, string key) =>
            key == "backhand" ? landings.Where(l => l.TacticBackhand).ToList() : key == "balanced" ? landings.Where(l => !l.TacticBackhand).ToList() : landings;

        // Heatmap legend count line: "백핸드 쪽 28/61구 · 46%".
        public static string LandingLegend(List<Landing> landings)
        {
            int bh = landings.Count(l => l.BackhandTarget), n = landings.Count;
            return n == 0 ? "백핸드 쪽 " + CoachText.None : "백핸드 쪽 " + CoachText.Ratio(bh, n) + "구 · " + CoachText.Percent((double)bh / n);
        }

        // First landings of one player's rally shots (serves excluded), rotated so the opponent's court is always
        // Z > 0: a half turn keeps left and right as the hitter sees them. In/out uses the engine's own rule.
        public static List<Landing> Landings(MatchRecord record, int player)
        {
            string id = record.Stats.Players[player].PlayerId;
            var result = new List<Landing>();
            MatchEvent hit = null;
            foreach (var e in record.Events)
            {
                if (e.Kind == "BallHit") hit = e;
                else if (e.Kind == "BallBounced" && e.State.Ball.Bounces == 1 && hit != null && hit.PlayerId == id && hit.ShotKind != "Serve")
                {
                    var p = e.State.Ball.Position;
                    int receiverEnd = -hit.State.Players[player].End;
                    bool flip = p.Z < 0;
                    result.Add(new Landing
                    {
                        X = (float)(flip ? -p.X : p.X), Z = (float)(flip ? -p.Z : p.Z), BackhandTarget = hit.Reason == "Backhand",
                        TacticBackhand = hit.State.Tactics[player].Target == TargetStyle.TargetBackhand, In = Court.SinglesIn(p, receiverEnd)
                    });
                    hit = null;
                }
            }
            return result;
        }
    }
}
