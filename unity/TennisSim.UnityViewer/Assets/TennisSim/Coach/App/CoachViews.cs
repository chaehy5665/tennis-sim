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
    public sealed class Landing { public float X; public float Z; public bool BackhandTarget; public bool In; }
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
        public List<StatRow> Rows;
        public string RallyNote;
        public bool OpponentChanged;
        public string OpponentKicker;
        public string OpponentText;
        public List<string> Observations;
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
        public List<Landing> Landings;
        public int BackhandTargets;
        public int Outs;
    }

    public static class CoachViews
    {
        public static List<TacticGroup> TacticGroups(bool withDescriptions)
        {
            TacticOption O(string key, string value, string label, string description) => new TacticOption { Key = key, Value = value, Label = label, Description = withDescriptions ? description : "" };
            return new List<TacticGroup>
            {
                new TacticGroup { Key = "target", Label = "공격 방향", Options = {
                    O("target", "balanced", "균형", "양쪽 사이드로 고르게 벌립니다."),
                    O("target", "backhand", "백핸드 공략", "상대 백핸드 쪽으로 몰아칩니다. 백핸드가 약한 상대에게 유리합니다.") } },
                new TacticGroup { Key = "aggression", Label = "공격성", Options = {
                    O("aggression", "safe", "안전", "실수가 적고 빠른 공을 잘 받아냅니다. 느린 공을 줘서 공격당하기 쉽습니다."),
                    O("aggression", "balanced", "균형", "무난한 기본값입니다."),
                    O("aggression", "aggressive", "공격", "쉬운 공을 강하게 응징합니다. 빠른 공에는 실수가 급증합니다.") } },
                new TacticGroup { Key = "serve", Label = "서브 코스", Options = {
                    O("serve", "mixed", "혼합", "코스를 섞어 읽히지 않습니다."),
                    O("serve", "wide", "와이드", "바깥쪽 집중. 반복하면 리턴이 빨라집니다."),
                    O("serve", "body", "바디", "몸쪽 집중. 반복하면 리턴이 빨라집니다."),
                    O("serve", "t", "T", "가운데 집중. 반복하면 리턴이 빨라집니다.") } }
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
            var o = input.Players[1];
            double fh = o.ForehandPower + o.ForehandControl, bh = o.BackhandPower + o.BackhandControl;
            string detail = " (파워 " + CoachText.Fixed(o.BackhandPower, 2) + " 대 " + CoachText.Fixed(o.ForehandPower, 2) + ", 정확도 " + CoachText.Fixed(o.BackhandControl, 2) + " 대 " + CoachText.Fixed(o.ForehandControl, 2) + ")";
            string memo = bh - fh > .1 ? o.Name + "는 백핸드가 포핸드보다 강합니다" + detail + ". 백핸드 공략이 통하지 않을 수 있습니다."
                : fh - bh > .1 ? o.Name + "는 백핸드가 약점입니다" + detail + "."
                : o.Name + "는 포핸드와 백핸드가 비슷합니다" + detail + ".";
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

        static List<StatRow> Rows(SegmentStats s, SegmentStats m)
        {
            StatRow R(string label, Func<SegmentPlayerStats, string> f) => new StatRow { Label = label, A = f(s.Players[0]), B = f(s.Players[1]), MatchA = m == null ? null : f(m.Players[0]), MatchB = m == null ? null : f(m.Players[1]) };
            var rows = new List<StatRow>
            {
                R("득점", p => p.PointsWon.ToString()),
                R("서브 포인트 획득", p => CoachText.Ratio(p.ServePointsWon, p.ServePoints)),
                R("첫 서브 성공", p => CoachText.Ratio(p.FirstServesIn, p.ServePoints)),
                R("더블 폴트", p => p.DoubleFaults.ToString()),
                R("위너", p => p.Winners.ToString()),
                R("에러 포핸드/백핸드", p => p.ForehandErrors + "/" + p.BackhandErrors),
                R("타수 포핸드/백핸드", p => p.Forehands + "/" + p.Backhands)
            };
            // Energy is a state at the end of the range, so the segment and the match show the same value.
            rows.Add(new StatRow { Label = "체력", A = Energy(s.Players[0]), B = Energy(s.Players[1]), MatchA = m == null ? null : "–", MatchB = m == null ? null : "–" });
            return rows;
        }
        static string Energy(SegmentPlayerStats p) => p.EnergyAtEnd < 0 ? "–" : CoachText.Fixed(p.EnergyAtEnd, 2);

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
                Heading = "체인지오버 · " + games + " 구간 (포인트 " + seg.FromPoint + "–" + seg.ToPoint + ")",
                Rows = Rows(s, m), RallyNote = "이번 구간 평균 랠리 " + CoachText.Fixed(s.MeanRallyLength, 1) + "구 · 체력은 구간 끝 값 (0.15–1)",
                CurrentA = state.Tactics[0].Copy(), CurrentAText = CoachText.Tactic(state.Tactics[0]),
                NextChangeover = state.Score.TieBreak ? "다음 체인지오버: 6포인트 후" : "다음 체인지오버: 2게임 후",
                Groups = TacticGroups(false)
            };
            var change = session.OpponentChange;
            if (change != null)
            {
                view.OpponentChanged = true;
                view.OpponentKicker = names[1] + " 코치가 전술을 바꿨습니다";
                var before = state.Tactics[1];
                var parts = new List<string>();
                if (before.Target != change.Tactic.Target) parts.Add("공격 방향 " + CoachText.Target(before.Target) + " → " + CoachText.Target(change.Tactic.Target));
                if (before.Aggression != change.Tactic.Aggression) parts.Add("공격성 " + CoachText.Aggression(before.Aggression) + " → " + CoachText.Aggression(change.Tactic.Aggression));
                if (before.Serve != change.Tactic.Serve) parts.Add("서브 " + CoachText.Serve(before.Serve) + " → " + CoachText.Serve(change.Tactic.Serve));
                view.OpponentText = string.Join(", ", parts) + " (다음 포인트부터). " + string.Join(" ", change.Reasons.Select(r => CoachText.Reason(r, names[0])));
            }
            var a = s.Players[0]; var b = m.Players[1];
            view.Observations = new List<string>
            {
                "이번 구간 " + names[0] + " 에러 포핸드 " + a.ForehandErrors + ", 백핸드 " + a.BackhandErrors + ".",
                names[1] + " 누적 에러 포핸드 " + b.ForehandErrors + ", 백핸드 " + b.BackhandErrors + ".",
                "이번 구간 " + names[0] + " 위너 " + a.Winners + ", 에러 " + (a.ForehandErrors + a.BackhandErrors + a.DoubleFaults) + "."
            };
            return view;
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
            var summary = Rows(whole, null);
            summary.Insert(7, new StatRow { Label = "평균 랠리", A = CoachText.Fixed(whole.MeanRallyLength, 1), B = CoachText.Fixed(whole.MeanRallyLength, 1) });
            return new ReviewView
            {
                Names = names, Games = (int[])record.FinalScore.Games.Clone(), Winner = record.FinalScore.Winner, Points = record.FinalScore.PointsPlayed,
                Segments = segments, GameWinners = gameWinners, Summary = summary, Landings = Landings(record, 0)
            }.WithCounts();
        }

        static ReviewView WithCounts(this ReviewView v)
        {
            v.BackhandTargets = v.Landings.Count(l => l.BackhandTarget);
            v.Outs = v.Landings.Count(l => !l.In);
            return v;
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
                        In = Court.SinglesIn(p, receiverEnd)
                    });
                    hit = null;
                }
            }
            return result;
        }
    }
}
