using System;
using System.Globalization;
using TennisSim.Core;

namespace TennisSim.Coach
{
    // Korean wording for the coaching screens. Pure formatting: every number comes from the engine record.
    public static class CoachText
    {
        // Option names never repeat across axes: attack direction Balanced is "양쪽" because aggression Balanced is "균형".
        public static string Target(TargetStyle t) => t == TargetStyle.TargetBackhand ? "백핸드 공략" : "양쪽";
        public static string Aggression(Aggression a) => a == Core.Aggression.Safe ? "안전" : a == Core.Aggression.Aggressive ? "공격" : "균형";
        public static string Serve(ServeDirection s) => s == ServeDirection.Wide ? "와이드" : s == ServeDirection.Body ? "바디" : s == ServeDirection.T ? "T" : "혼합";
        public static string Tactic(Tactic t) => Target(t.Target) + " · " + Aggression(t.Aggression) + " · " + Serve(t.Serve);
        // Spaced separators: KeepAll joins Hangul to its neighbours, so an unspaced "양쪽·안전·혼합" could not wrap at all.
        public static string Short(Tactic t) => (t.Target == TargetStyle.TargetBackhand ? "백핸드" : "양쪽") + " · " + Aggression(t.Aggression) + " · " + Serve(t.Serve);

        public static string Stroke(string stroke) => stroke == "Forehand" ? "포핸드" : stroke == "Backhand" ? "백핸드" : stroke == "Serve" ? "서브" : stroke;

        // How the point ended, from the last hit: a ball the opponent could not return is a winner (an ace on a serve).
        public static string Cause(PointSummary p, string[] names)
        {
            if (p.Reason == "DoubleFault") return names[p.Server] + " 더블 폴트";
            if (p.LastHitter < 0) return p.Reason;
            string who = names[p.LastHitter] + " " + Stroke(p.LastStroke);
            switch (p.Reason)
            {
                case "UnreturnedBall": return p.LastShotKind == "Serve" ? names[p.LastHitter] + " 서브 에이스" : who + " 위너";
                case "Out": return who + " 아웃";
                case "Net": return who + " 네트";
                default: return who + " " + p.Reason;
            }
        }

        // Game points as a scoreboard shows them: 0/15/30/40, AD at advantage, raw numbers in a tiebreak.
        public static string[] PointScore(ScoreState s)
        {
            int a = s.Points[0], b = s.Points[1];
            if (s.TieBreak) return new[] { a.ToString(CultureInfo.InvariantCulture), b.ToString(CultureInfo.InvariantCulture) };
            if (a >= 3 && b >= 3) return a == b ? new[] { "40", "40" } : a > b ? new[] { "AD", "40" } : new[] { "40", "AD" };
            string[] labels = { "0", "15", "30", "40" };
            return new[] { labels[Math.Min(3, a)], labels[Math.Min(3, b)] };
        }

        // UI Toolkit breaks Korean between any two syllables; Korean text should break only at spaces (keep-all).
        // A WORD JOINER (U+2060, zero width, present in Pretendard) between a Hangul character and any non-space
        // neighbour keeps each space-separated word whole. Display only: source strings and Core values are unchanged.
        public static string KeepAll(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new System.Text.StringBuilder(text.Length * 2);
            for (int i = 0; i < text.Length; i++)
            {
                if (i > 0 && !char.IsWhiteSpace(text[i - 1]) && !char.IsWhiteSpace(text[i]) && text[i - 1] != '\u2060' && text[i] != '\u2060' && (IsHangul(text[i - 1]) || IsHangul(text[i])))
                    sb.Append('\u2060');
                sb.Append(text[i]);
            }
            return sb.ToString();
        }
        public static bool IsHangul(char c) => (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF') || (c >= '\u3130' && c <= '\u318F');

        // No value (zero denominator, not applicable): one full-width dash. Ranges keep the short dash ("1–6").
        public const string None = "—";

        public static string Ratio(int k, int n) => k.ToString(CultureInfo.InvariantCulture) + "/" + n.ToString(CultureInfo.InvariantCulture);
        public static string Percent(double x) => Math.Round(100 * x).ToString(CultureInfo.InvariantCulture) + "%";
        public static string Fixed(double x, int digits) => x.ToString("F" + digits, CultureInfo.InvariantCulture);

        // One sentence per opponent-coach reason; `opponent` is the player the coach is playing against.
        public static string Reason(CoachReason r, string opponent)
        {
            switch (r.Kind)
            {
                case CoachReasonKind.TargetMeasuredErrors:
                    return r.A > r.B
                        ? opponent + "의 백핸드 에러율이 " + Percent(r.A) + "로 포핸드 " + Percent(r.B) + "보다 높습니다."
                        : opponent + "의 포핸드 에러율이 " + Percent(r.B) + "로 백핸드 " + Percent(r.A) + "보다 높습니다.";
                case CoachReasonKind.TargetScouting:
                    return r.A > r.B
                        ? "스카우팅상 " + opponent + "의 백핸드가 약합니다 (포핸드 " + Fixed(r.A, 2) + ", 백핸드 " + Fixed(r.B, 2) + ")."
                        : "스카우팅상 " + opponent + "의 백핸드가 약하지 않습니다 (포핸드 " + Fixed(r.A, 2) + ", 백핸드 " + Fixed(r.B, 2) + ").";
                case CoachReasonKind.CounterSafe: return opponent + " 선수가 안전하게 치고 있어 공격으로 바꿉니다.";
                case CoachReasonKind.CounterAggressive: return opponent + " 선수가 공격적으로 치고 있어 균형으로 받습니다.";
                case CoachReasonKind.SteadyPlayer: return "컨트롤이 높은 선수라 안전하게 버팁니다 (평균 " + Fixed(r.A, 2) + ").";
                case CoachReasonKind.NeutralStyle: return "균형으로 돌아갑니다.";
                case CoachReasonKind.BigServerWide: return "서브가 강해 와이드 서브에 집중합니다 (서브 파워 " + Fixed(r.A, 2) + ").";
                case CoachReasonKind.ServeRead: return "고정 코스 서브가 읽혀 섞습니다 (서브 포인트 " + Ratio((int)r.A, (int)r.B) + ").";
                case CoachReasonKind.HoldStyle: return opponent + " 선수가 방금 공격성을 바꿔, 한 구간 균형으로 지켜봅니다.";
                case CoachReasonKind.TryStyle:
                    return Aggression(r.From) + "으로 " + Percent(r.A) + "(" + ((int)r.B).ToString(CultureInfo.InvariantCulture) + "포인트)에 그쳐 "
                        + Aggression(r.To) + "을 한 구간 시험합니다.";
                case CoachReasonKind.MeasuredStyle:
                    return "시험해 보니 " + Aggression(r.To) + "이 " + Percent(r.A) + "로 " + Aggression(r.From) + " " + Percent(r.B) + "보다 나아 "
                        + Aggression(r.To) + "으로 갑니다.";
                case CoachReasonKind.SelfScouting:
                    return r.To == Core.Aggression.Safe
                        ? "우리 선수는 파워(" + Fixed(r.A, 2) + ")에 비해 컨트롤(" + Fixed(r.B, 2) + ")이 낮아 안전하게 칩니다."
                        : "우리 선수는 컨트롤(" + Fixed(r.B, 2) + ")에 비해 파워(" + Fixed(r.A, 2) + ")가 약해 먼저 공격합니다.";
                case CoachReasonKind.WatchingStyle: return opponent + " 선수가 또 " + Aggression(r.From) + "으로 바꿨습니다. 한 구간 더 지켜봅니다.";
                case CoachReasonKind.KeepStyle:
                    return opponent + " 선수가 " + Aggression(r.From) + "으로 바꾼 것을 봤습니다. "
                        + (r.A > 0 ? "우리 선수에게 맞는 " + Aggression(r.To) + "을 유지합니다." : "지금 " + Aggression(r.To) + "이 맞는 대응이라 유지합니다.");
                case CoachReasonKind.OpponentScouting:
                    return r.To == Core.Aggression.Safe
                        ? opponent + "의 공이 무겁지만(파워 " + Fixed(r.A, 2) + ") 컨트롤(" + Fixed(r.B, 2) + ")이 낮아 안전하게 버팁니다."
                        : opponent + "의 공이 가벼워(파워 " + Fixed(r.A, 2) + ") 공격합니다.";
                default: return r.Kind.ToString();
            }
        }
    }
}
