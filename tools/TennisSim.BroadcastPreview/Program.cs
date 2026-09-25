using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TennisSim.Coach;
using TennisSim.Core;

// Still frames of the 2.5D broadcast view (docs/BROADCAST_ASSETS.md) drawn from a recorded match. The record is only
// read: positions come from its frames and events, projection from BroadcastCamera, motion from BroadcastMotion.
//
//   broadcast-preview --replay <file> [--name <prefix>] [--out <dir>] [--no-png] [--no-guides]
//                     [--times t1,t2,...] [--point N --hit K [--offsets -0.3,-0.15,0,0.15,0.3]] [--deepest]
//
// Each selected time becomes <prefix>-<time>.svg, and .png when /snap/bin/chromium is present.

var inv = CultureInfo.InvariantCulture;
var opts = new Dictionary<string, string>();
for (int i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--")) { Console.Error.WriteLine("unexpected argument " + args[i]); return 2; }
    bool flag = i + 1 >= args.Length || args[i + 1].StartsWith("--") && !double.TryParse(args[i + 1], NumberStyles.Float, inv, out _);
    opts[args[i].Substring(2)] = flag ? "" : args[++i];
}
if (!opts.TryGetValue("replay", out var replayPath)) { Console.Error.WriteLine("--replay <file> is required"); return 2; }

string root = Directory.GetCurrentDirectory();
while (root != null && !File.Exists(Path.Combine(root, "TennisSim.sln"))) root = Path.GetDirectoryName(root);
if (root == null) { Console.Error.WriteLine("run inside the repository"); return 2; }
string outDir = Path.GetFullPath(opts.TryGetValue("out", out var o) ? o : Path.Combine(root, "artifacts", "broadcast-preview"));
Directory.CreateDirectory(outDir);
string prefix = opts.TryGetValue("name", out var n) ? n : Path.GetFileNameWithoutExtension(replayPath);

var record = TennisSim.Cli.ReplayJson.Load(replayPath);
var events = record.Events;
double[] Doubles(string s) => s.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => double.Parse(x, inv)).ToArray();

// ---------- which moments ----------
var times = new List<(double Time, string Label)>();
if (opts.TryGetValue("times", out var ts)) foreach (var t in Doubles(ts)) times.Add((t, "t"));
if (opts.TryGetValue("point", out var ps))
{
    int point = int.Parse(ps, inv), k = opts.TryGetValue("hit", out var hs) ? int.Parse(hs, inv) : 1;
    var hits = events.Where(e => e.Kind == "BallHit" && e.Point == point).ToList();
    if (k < 1 || k > hits.Count) { Console.Error.WriteLine($"point {point} has {hits.Count} hits"); return 2; }
    var offsets = opts.TryGetValue("offsets", out var os) ? Doubles(os) : new[] { -.3, -.15, 0, .15, .3 };
    foreach (var off in offsets) times.Add((hits[k - 1].Time + off, $"p{point}-hit{k}{(off >= 0 ? "+" : "")}{off.ToString("0.00", inv)}"));
}
if (opts.ContainsKey("deepest"))
{
    var deep = record.Frames.SelectMany(f => f.Players.Select(p => (f.Time, Depth: Math.Abs(p.Position.Z)))).OrderByDescending(x => x.Depth).First();
    times.Add((deep.Time, "deepest-" + deep.Depth.ToString("0.0", inv) + "m"));
}
if (times.Count == 0) { Console.Error.WriteLine("choose moments with --times, --point/--hit or --deepest"); return 2; }

// ---------- state at a time (REPLAY_CONTRACT "시간축과 표시 선택", simplified for stills) ----------
// Samples are frames and events on one time axis. Positions are interpolated between neighbours, towards an event's
// Before state, and held (not walked) across a reset.
var samples = record.Frames.Select((f, i) => (Time: f.Time, Order: (long)i, State: f, Before: (FrameState)null, Kind: ""))
    .Concat(events.Select(e => (Time: e.Time, Order: 1_000_000_000L + e.Sequence, State: e.State, Before: e.Before, Kind: e.Kind)))
    .OrderBy(s => s.Time).ThenBy(s => s.Order).ToList();
FrameState Sample(double t)
{
    int next = samples.FindIndex(s => s.Time > t);
    if (next == 0) return samples[0].State;
    var prev = samples[(next < 0 ? samples.Count : next) - 1];
    if (next < 0 || samples[next].Kind == "PlayersRepositioned") return prev.State;
    var end = samples[next].Before ?? samples[next].State;
    double f = (t - prev.Time) / (samples[next].Time - prev.Time);
    var s = new FrameState { Time = t, Phase = prev.State.Phase, Point = prev.State.Point, Score = prev.State.Score, Tactics = prev.State.Tactics };
    s.Players = prev.State.Players.Select((p, i) => { var c = p.Copy(); c.Position = Vec3.Lerp(p.Position, end.Players[i].Position, f); return c; }).ToArray();
    s.Ball = prev.State.Ball.Copy(); s.Ball.Position = Vec3.Lerp(prev.State.Ball.Position, end.Ball.Position, f);
    return s;
}

// ---------- colours: the coach USS tokens, plus the two broadcast tokens of design system v29 ----------
var colours = new Dictionary<string, string>
{
    ["ball-shadow"] = "rgba(0, 0, 0, 0.45)",
    ["hud-plate"] = "rgba(6, 11, 17, 0.88)",
};
string ussPath = Path.Combine(root, "unity/TennisSim.UnityViewer/Assets/TennisSim/Coach/Resources/TennisSimCoach.uss");
foreach (Match m in Regex.Matches(File.ReadAllText(ussPath), @"--([a-z-]+):\s*(#[0-9a-fA-F]{6})\s*;")) colours[m.Groups[1].Value] = m.Groups[2].Value;
string C(string token) => colours[token];

string fontDir = Path.Combine(root, "unity/TennisSim.UnityViewer/Assets/TennisSim/Coach/Resources/Fonts");
string Url(string file) => new Uri(Path.Combine(fontDir, file)).AbsoluteUri;
string F(double v) => v.ToString("0.0", inv);
string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

var cam = BroadcastCamera.Default();
var motionNames = new Dictionary<MotionState, string>
{
    [MotionState.Idle] = "대기", [MotionState.Serve] = "서브", [MotionState.Move] = "이동", [MotionState.Prepare] = "준비", [MotionState.Strike] = "타격",
    [MotionState.Miss] = "놓침", [MotionState.Recover] = "회복", [MotionState.AfterPoint] = "포인트 뒤", [MotionState.Reposition] = "재배치",
};
string Stroke(string s) => s == "Forehand" ? "포핸드" : s == "Backhand" ? "백핸드" : s == "Serve" ? "서브" : "";
string EndReason(string r) => r == "Out" ? "아웃" : r == "UnreturnedBall" ? "못 받음" : r == "Net" ? "네트" : r == "DoubleFault" ? "더블 폴트" : r;
string[] names = record.Input.Players.Select(p => p.Name).ToArray();
bool guides = !opts.ContainsKey("no-guides");

string Draw(double t)
{
    var s = Sample(t);
    var motion = BroadcastMotion.Classify(events.Where(e => e.Time <= t).ToList(), t);
    var sb = new StringBuilder();
    void Add(string x) => sb.AppendLine(x);
    string Pt(Vec3 v) { var p = cam.Project(v); return F(p.X) + "," + F(p.Y); }

    Add($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1280 800\" width=\"1280\" height=\"800\" font-family=\"Pretendard, sans-serif\">");
    Add($"<rect width=\"1280\" height=\"800\" fill=\"{C("ground")}\"/>");
    Add($"<polygon points=\"{string.Join(" ", BroadcastCourt.RunOffCorners.Select(Pt))}\" fill=\"{C("court")}\"/>");
    foreach (var seg in BroadcastCourt.Lines())
    {
        var a = cam.Project(seg.A); var b = cam.Project(seg.B);
        Add($"<line x1=\"{F(a.X)}\" y1=\"{F(a.Y)}\" x2=\"{F(b.X)}\" y2=\"{F(b.Y)}\" stroke=\"{C("line")}\" stroke-width=\"{BroadcastSpec.LineWidth((a.Depth + b.Depth) / 2).ToString("0.00", inv)}\"/>");
    }
    // Ground shadows first, then everything standing, far to near, the net at the z = 0 plane.
    var marks = s.Players.Select(p => cam.Player(p.Position)).ToArray();
    foreach (var m in marks) Add($"<ellipse cx=\"{F(m.Foot.X)}\" cy=\"{F(m.Foot.Y)}\" rx=\"{F(m.ShadowRadiusX)}\" ry=\"{F(m.ShadowRadiusY)}\" fill=\"{C("ball-shadow")}\"/>");
    var ball = cam.Ball(s.Ball.Position);
    Add($"<ellipse cx=\"{F(ball.Shadow.X)}\" cy=\"{F(ball.Shadow.Y)}\" rx=\"{F(BroadcastSpec.BallShadowWidth / 2)}\" ry=\"{F(BroadcastSpec.BallShadowHeight / 2)}\" fill=\"{C("ball-shadow")}\"/>");
    var standing = new List<(double Z, double Depth, Action Draw)>();
    for (int i = 0; i < s.Players.Length; i++)
    {
        var m = marks[i]; string colour = C(i == 0 ? "player-a" : "player-b");
        standing.Add((s.Players[i].Position.Z, m.Foot.Depth, () =>
            Add($"<rect x=\"{F(m.Foot.X - m.HalfWidth)}\" y=\"{F(m.Head.Y)}\" width=\"{F(2 * m.HalfWidth)}\" height=\"{F(m.Foot.Y - m.Head.Y)}\" rx=\"{F(m.HalfWidth)}\" fill=\"{colour}\"/>")));
    }
    standing.Add((s.Ball.Position.Z, ball.Ball.Depth, () =>
    {
        if (ball.HeightGuide) Add($"<line x1=\"{F(ball.Shadow.X)}\" y1=\"{F(ball.Shadow.Y)}\" x2=\"{F(ball.Ball.X)}\" y2=\"{F(ball.Ball.Y + ball.Diameter / 2)}\" stroke=\"{C("ball")}\" stroke-width=\"1\" stroke-dasharray=\"2 3\" opacity=\".7\"/>");
        Add($"<circle cx=\"{F(ball.Ball.X)}\" cy=\"{F(ball.Ball.Y)}\" r=\"{F(ball.Diameter / 2)}\" fill=\"{C("ball")}\" stroke=\"{C("ground")}\" stroke-width=\"{BroadcastSpec.BallOutline.ToString(inv)}\"/>");
    }));
    void Net()
    {
        var tape = BroadcastCourt.NetTape();
        var foot = new[] { new Vec3(BroadcastSpec.NetPostX, 0, 0), new Vec3(-BroadcastSpec.NetPostX, 0, 0) };
        Add($"<polygon points=\"{string.Join(" ", tape.Concat(foot).Select(Pt))}\" fill=\"{C("net")}\" fill-opacity=\".35\"/>");
        Add($"<polyline points=\"{string.Join(" ", tape.Select(Pt))}\" fill=\"none\" stroke=\"{C("line")}\" stroke-width=\"2\"/>");
        foreach (var x in new[] { -BroadcastSpec.NetPostX, BroadcastSpec.NetPostX })
            Add($"<polyline points=\"{Pt(new Vec3(x, 0, 0))} {Pt(new Vec3(x, Court.NetHeight(x), 0))}\" stroke=\"{C("net")}\" stroke-width=\"3\"/>");
    }
    foreach (var d in standing.Where(x => x.Z > 0).OrderByDescending(x => x.Depth)) d.Draw();
    Net();
    foreach (var d in standing.Where(x => x.Z <= 0).OrderByDescending(x => x.Depth)) d.Draw();
    for (int i = 0; i < marks.Length; i++)
    {
        var b = marks[i].Badge;
        Add($"<rect x=\"{F(b.X)}\" y=\"{F(b.Y)}\" width=\"{F(b.Width)}\" height=\"{F(b.Height)}\" rx=\"{F(b.Width / 2)}\" fill=\"{C(i == 0 ? "player-a" : "player-b")}\"/>");
        Add($"<text x=\"{F(b.X + b.Width / 2)}\" y=\"{F(b.Y + 15.5)}\" font-size=\"12\" font-weight=\"700\" text-anchor=\"middle\" fill=\"{C("on-selected")}\">{s.Players[i].Id}</text>");
    }

    // HUD plates.
    string Plate(ScreenRect r) => $"<rect x=\"{F(r.X)}\" y=\"{F(r.Y)}\" width=\"{F(r.Width)}\" height=\"{F(r.Height)}\" fill=\"{C("hud-plate")}\"/>";
    string Badge(double x, double y, int i) => $"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"22\" height=\"22\" rx=\"11\" fill=\"{C(i == 0 ? "player-a" : "player-b")}\"/><text x=\"{F(x + 11)}\" y=\"{F(y + 15.5)}\" font-size=\"12\" font-weight=\"700\" text-anchor=\"middle\" fill=\"{C("on-selected")}\">{s.Players[i].Id}</text>";
    var sc = BroadcastSpec.Scoreboard; Add(Plate(sc));
    var parts = (s.Score.Display ?? "").Split(' ');
    var pts = parts.Length > 1 ? parts[1].Split('-') : new string[0];
    for (int i = 0; i < 2; i++)
    {
        double y = sc.Y + 11 + i * 40;
        Add(Badge(sc.X + 12, y, i));
        Add($"<text x=\"{F(sc.X + 44)}\" y=\"{F(y + 17)}\" font-size=\"15\" fill=\"{C("line")}\">{Esc(names[i])}</text>");
        if (s.Score.Server == i) Add($"<circle cx=\"{F(sc.X + 54 + 9 * names[i].Length)}\" cy=\"{F(y + 11)}\" r=\"5\" fill=\"{C("ball")}\"/>");
        Add($"<text x=\"{F(sc.X + 226)}\" y=\"{F(y + 21)}\" font-family=\"JetBrains Mono, monospace\" font-size=\"26\" font-weight=\"700\" text-anchor=\"end\" fill=\"{C("line")}\">{s.Score.Games[i]}</text>");
        if (pts.Length == 2) Add($"<text x=\"{F(sc.X + 284)}\" y=\"{F(y + 21)}\" font-family=\"JetBrains Mono, monospace\" font-size=\"26\" font-weight=\"700\" text-anchor=\"end\" fill=\"{C("line")}\">{Esc(pts[i])}</text>");
    }
    var ct = BroadcastSpec.CurrentTactic; Add(Plate(ct));
    Add($"<text x=\"{F(ct.X + 16)}\" y=\"{F(ct.Y + 22)}\" font-size=\"12\" font-weight=\"700\" letter-spacing=\".72\" fill=\"{C("line-muted")}\">현재 전술</text>");
    Add(Badge(ct.X + 16, ct.Y + 30, 0));
    Add($"<text x=\"{F(ct.X + 48)}\" y=\"{F(ct.Y + 47)}\" font-size=\"15\" fill=\"{C("line")}\">{Esc(CoachText.Short(s.Tactics[0]))}</text>");
    var lastEnd = events.LastOrDefault(e => e.Kind == "PointEnded" && e.Time <= t && t - e.Time <= 3);
    if (lastEnd != null)
    {
        var lp = BroadcastSpec.LastPoint; int w = Array.FindIndex(s.Players, p => p.Id == lastEnd.PlayerId);
        Add(Plate(lp)); Add(Badge(lp.X + 12, lp.Y + 11, w));
        Add($"<text x=\"{F(lp.X + 44)}\" y=\"{F(lp.Y + 28)}\" font-size=\"15\" fill=\"{C("line")}\">{Esc(names[w])} 득점 · {EndReason(lastEnd.Reason)}</text>");
    }
    var cb = BroadcastSpec.ControlBar; Add(Plate(cb));
    Add($"<text x=\"{F(cb.X + 16)}\" y=\"{F(cb.Y + 33)}\" font-size=\"12\" font-weight=\"700\" letter-spacing=\".72\" fill=\"{C("net")}\">조작 막대 자리 · 포인트 {s.Point}</text>");

    // Header band: the coach UI keeps it; here it carries the preview annotation.
    Add($"<rect width=\"1280\" height=\"56\" fill=\"{C("ground")}\"/><line x1=\"0\" y1=\"55.5\" x2=\"1280\" y2=\"55.5\" stroke=\"{C("net")}\"/>");
    string Who(int i)
    {
        var span = motion.SpanAt(i, t);
        if (span == null) return s.Players[i].Id + " —";
        string stroke = span.State == MotionState.Strike ? Stroke(span.Stroke) : span.State == MotionState.Prepare || span.State == MotionState.Move ? Stroke(span.PlannedStroke) : "";
        return s.Players[i].Id + " " + motionNames[span.State] + (stroke != "" ? "(" + stroke + ")" : "");
    }
    Add($"<text x=\"32\" y=\"34\" font-size=\"18\" font-weight=\"700\" fill=\"{C("line")}\">2.5D 미리 보기</text>");
    Add($"<text x=\"1248\" y=\"33\" font-size=\"13\" text-anchor=\"end\" fill=\"{C("line-muted")}\">{Esc(Path.GetFileName(replayPath))} · t={t.ToString("0.000", inv)} s · {Who(0)} · {Who(1)}</text>");
    if (guides) Add($"<rect x=\"{F(BroadcastSpec.SafeArea.X)}\" y=\"{F(BroadcastSpec.SafeArea.Y)}\" width=\"{F(BroadcastSpec.SafeArea.Width)}\" height=\"{F(BroadcastSpec.SafeArea.Height)}\" fill=\"none\" stroke=\"{C("ball")}\" stroke-dasharray=\"6 6\" opacity=\".45\"/>");
    Add("</svg>");
    return sb.ToString();
}

string chromium = "/snap/bin/chromium";
bool png = !opts.ContainsKey("no-png") && File.Exists(chromium);
if (!png && !opts.ContainsKey("no-png")) Console.WriteLine("chromium not found at " + chromium + ": writing SVG only");
foreach (var (t, label) in times)
{
    string stem = Path.Combine(outDir, prefix + "-" + (label == "t" ? "t" + t.ToString("0.000", inv) : label));
    string svg = Draw(t);
    File.WriteAllText(stem + ".svg", svg);
    Console.WriteLine("WROTE " + stem + ".svg");
    if (!png) continue;
    string html = "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
        $"@font-face{{font-family:Pretendard;src:url({Url("Pretendard-Regular.otf")});font-weight:400}}" +
        $"@font-face{{font-family:Pretendard;src:url({Url("Pretendard-Bold.otf")});font-weight:700}}" +
        $"@font-face{{font-family:'JetBrains Mono';src:url({Url("JetBrainsMono-Bold.ttf")});font-weight:700}}" +
        "html,body{margin:0;width:1280px;height:800px;overflow:hidden}</style></head><body>" + svg + "</body></html>";
    File.WriteAllText(stem + ".html", html);
    var run = Process.Start(new ProcessStartInfo(chromium, $"--headless --disable-gpu --no-sandbox --hide-scrollbars --window-size=1280,800 --screenshot={stem}.png {new Uri(stem + ".html").AbsoluteUri}")
        { RedirectStandardError = true, RedirectStandardOutput = true });
    run.StandardError.ReadToEnd(); run.WaitForExit(60000);
    File.Delete(stem + ".html");
    Console.WriteLine(File.Exists(stem + ".png") ? "WROTE " + stem + ".png" : "PNG FAILED for " + stem + " (chromium exit " + run.ExitCode + ")");
}
return 0;
