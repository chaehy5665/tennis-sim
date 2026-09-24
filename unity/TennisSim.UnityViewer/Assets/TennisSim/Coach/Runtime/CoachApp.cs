using System;
using System.Collections.Generic;
using System.Linq;
using TennisSim.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace TennisSim.Coach
{
    // Runtime coaching UI: pre-match tactics, live 2D top view, changeover instructions and review. The panel and its
    // theme are created at runtime, so the scene only needs this component. Core decides every outcome; this class
    // advances the session with frame time and draws what the record says.
    public sealed class CoachApp : MonoBehaviour
    {
        public uint Seed = 3;
        public bool AdaptiveOpponent = true;
        public CoachSession Session { get; private set; }
        public VisualElement Root { get; private set; }
        public float Speed { get; private set; } = 4;
        public bool Paused { get; private set; }

        // Bundled fonts (design system type.fonts): Pretendard for text, JetBrains Mono for numbers. Legacy Font assets
        // import with a dynamic atlas, so only the glyphs actually drawn are rasterised.
        Font sansRegular, sansBold, monoMedium, monoBold;
        Tactic pending = new Tactic();
        VisualElement screen;
        // Live match widgets, updated in place every frame.
        CourtView court;
        Label[] gameLabels, pointLabels, tacticLabels;
        VisualElement[] serveMarks;
        Label pointInfo;
        VisualElement feedList;
        int feedCount = -1;

        static readonly string[] Steps = { "경기 전", "경기", "체인지오버", "리뷰" };

        void Start()
        {
            CreatePanel();
            NewSession(Seed);
        }

        void Update()
        {
            if (Session == null || Session.Phase != CoachPhase.Playing || Paused) return;
            var stop = Session.Advance(Time.unscaledDeltaTime * Speed);
            AfterAdvance(stop);
        }

        // Public entry points used by the buttons and by PlayMode tests.
        public void NewSession(uint seed)
        {
            Seed = seed;
            Session = new CoachSession(CoachMatchup.Input(seed), AdaptiveOpponent);
            pending = new Tactic();
            ShowPreMatch();
        }
        public void StartMatch(Tactic initial = null) { if (initial != null) pending = initial.Copy(); Session.Start(pending); pending = pending.Copy(); ShowMatch(); }
        public void SkipToNextStop() { if (Session.Phase == CoachPhase.Playing) AfterAdvance(Session.AdvanceToNextStop()); }
        public void Resume(Tactic change) { Session.Resume(change); ShowMatch(); }

        void AfterAdvance(ChangeoverStop stop)
        {
            if (stop == ChangeoverStop.Changeover) ShowChangeover();
            else if (stop == ChangeoverStop.Finished) ShowReview();
            else RefreshMatch();
        }

        void CreatePanel()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("TennisSimCoachTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1280, 800);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = .5f;
            var host = new GameObject("CoachUI");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            var document = host.AddComponent<UIDocument>();
            document.panelSettings = settings;
            host.SetActive(true);
            Root = document.rootVisualElement;
            Root.styleSheets.Add(Resources.Load<StyleSheet>("TennisSimCoach"));
            Root.AddToClassList("tsc-root");
            sansRegular = LoadFont("Pretendard-Regular"); sansBold = LoadFont("Pretendard-Bold");
            monoMedium = LoadFont("JetBrainsMono-Medium"); monoBold = LoadFont("JetBrainsMono-Bold");
            Root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(sansRegular));
        }

        // ---------- helpers ----------
        static VisualElement Box(params string[] classes) { var e = new VisualElement(); foreach (var c in classes) e.AddToClassList(c); return e; }
        // The label style is uppercase; USS has no text-transform, so the bound string is converted (Korean is unaffected).
        Label Text(string text, params string[] classes)
        {
            var l = new Label(Display(text, Array.IndexOf(classes, "tsc-label") >= 0));
            foreach (var c in classes) l.AddToClassList(c);
            ApplyFont(l, text, classes);
            return l;
        }
        // Label text is uppercase (USS has no text-transform); Korean words are kept whole across line breaks.
        // Uppercase applies only to all-Latin labels ("EMBER"); a label with Hangul keeps names as written.
        static string Display(string text, bool label) => CoachText.KeepAll(label && !HasHangul(text) ? text.ToUpperInvariant() : text);
        static Font LoadFont(string name)
        {
            var font = Resources.Load<Font>("Fonts/" + name);
            if (font == null) Debug.LogError("TennisSim coach font missing: Resources/Fonts/" + name);
            return font;
        }
        // Type steps with weight 600/700 (headline, title, label, score) use the Bold file; the USS keeps font style
        // normal so the Bold file is not emboldened again. score/stat use JetBrains Mono unless the text holds Hangul,
        // which JetBrains Mono lacks.
        void ApplyFont(VisualElement e, string text, IList<string> classes)
        {
            bool bold = classes.Contains("tsc-headline") || classes.Contains("tsc-title") || classes.Contains("tsc-label") || classes.Contains("tsc-score") || classes.Contains("tsc-banner__icon");
            bool mono = (classes.Contains("tsc-score") || classes.Contains("tsc-stat")) && !HasHangul(text);
            var font = mono ? (bold ? monoBold : monoMedium) : (bold ? sansBold : sansRegular);
            if (font != null) e.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
        }
        static bool HasHangul(string text) => text != null && text.Any(CoachText.IsHangul);
        Label Number(string text, string cls) => Text(text, cls);
        // A badge keeps space-2 towards its text neighbour: after a badge by default, before it when it trails the text.
        Label Badge(int player, bool trailing = false) => Text(player == 0 ? "A" : "B", "tsc-label", "tsc-badge", player == 0 ? "tsc-badge--a" : "tsc-badge--b", trailing ? "tsc-badge--trailing" : "tsc-badge--leading");
        Button MakeButton(string text, Action onClick, bool primary)
        {
            var b = new Button(onClick) { text = Display(text, true) };
            ApplyFont(b, text, new[] { "tsc-label" });
            b.AddToClassList("tsc-button"); b.AddToClassList(primary ? "tsc-button--primary" : "tsc-button--secondary");
            return b;
        }
        // Token values from TennisSimCoach.uss for the few styles set in code.
        static Color Hex(string html) { ColorUtility.TryParseHtmlString(html, out var c); return c; }
        // Buttons wrap onto further lines, right aligned with space-2 between them, and never leave their container.
        // Button row without layout state. Each button's natural width (text + padding + border) is measured once; the
        // row's width comes from its parent (stretched in a column, flex-grow with basis 0 in a row), never from its own
        // content, so the decision cannot feed back into itself: stacked = row width < natural widths + 8px gaps.
        // Row: right aligned, 8px between buttons. Stacked: one column, every button the row's full width, 8px apart.
        static VisualElement ButtonRow(params Button[] buttons) => ButtonRow(false, buttons);
        static VisualElement ButtonRow(bool fillParentRow, params Button[] buttons)
        {
            var r = Box("tsc-button-row");
            if (fillParentRow) { r.style.flexGrow = 1; r.style.flexBasis = 0; }
            foreach (var b in buttons) r.Add(b);
            var natural = new float[buttons.Length];
            bool? stacked = null;
            r.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                for (int i = 0; i < buttons.Length; i++)
                    if (natural[i] <= 0)
                    {
                        var st = buttons[i].resolvedStyle;
                        float text = buttons[i].MeasureTextSize(buttons[i].text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;
                        if (text > 0) natural[i] = Mathf.Ceil(text + st.paddingLeft + st.paddingRight + st.borderLeftWidth + st.borderRightWidth);
                    }
                if (natural.Any(w => w <= 0)) return;
                bool stack = r.contentRect.width < natural.Sum() + 8 * (buttons.Length - 1);
                if (stacked == stack) return;
                stacked = stack;
                r.EnableInClassList("tsc-button-row--stacked", stack);
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].style.marginLeft = !stack && i > 0 ? 8 : 0;
                    buttons[i].style.marginTop = stack && i > 0 ? 8 : 0;
                }
            });
            return r;
        }
        static VisualElement Row(params VisualElement[] children) { var r = Box("tsc-row"); r.style.alignItems = Align.Center; foreach (var c in children) r.Add(c); return r; }

        VisualElement Page(int step)
        {
            Root.Clear();
            var header = Box("tsc-header");
            header.Add(Text("TennisSim 코치", "tsc-title"));
            var steps = Box("tsc-steps");
            for (int i = 0; i < Steps.Length; i++) steps.Add(Text((i + 1) + " " + Steps[i], "tsc-label", "tsc-step", i == step ? "tsc-step--current" : "tsc-step-other"));
            header.Add(steps);
            header.Add(Text("Seed " + Seed + " · 1세트 · 하드코트", "tsc-label", "tsc-on-ground-muted"));
            Root.Add(header);
            // The body scrolls vertically; the header stays. The body is at least one viewport tall so flex-grow
            // children (the match court, the review columns) still fill the screen when the content is shorter.
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.style.flexGrow = 1;
            screen = Box("tsc-screen");
            var body = screen;
            scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(e => body.style.minHeight = e.newRect.height);
            scroll.Add(screen);
            Root.Add(scroll);
            return screen;
        }

        VisualElement PlayerPanel(PreMatchView v, int player)
        {
            var panel = Box("tsc-panel");
            panel.style.width = 320;
            panel.Add(Text(player == 0 ? "내 선수" : "상대 분석", "tsc-label", "tsc-gap-bottom"));
            panel.Add(Row(Badge(player), Text(v.Names[player], "tsc-headline")));
            foreach (var a in v.Attributes[player])
            {
                var track = Box("tsc-bar-track");
                var fill = Box("tsc-bar-fill", player == 0 ? "tsc-bar-fill--a" : "tsc-bar-fill--b");
                fill.style.width = Length.Percent(100 * Mathf.Clamp01(a.Fraction));
                track.Add(fill);
                var label = Text(a.Label, "tsc-body", "tsc-muted"); label.style.width = 96;
                var value = Number(a.Value, "tsc-stat"); value.style.width = 64; value.style.unityTextAlign = TextAnchor.MiddleRight;
                var row = Row(label, track, value); row.style.marginTop = 8;
                panel.Add(row);
            }
            if (player == 1)
            {
                var memo = Box("tsc-inset"); memo.style.marginTop = 16;
                memo.Add(Text("코치 메모", "tsc-label", "tsc-inset__kicker"));
                memo.Add(Text(v.ScoutingMemo, "tsc-body"));
                panel.Add(memo);
                var note = Text(v.OpponentCoachNote, "tsc-body", "tsc-muted"); note.style.marginTop = 16;
                panel.Add(note);
            }
            return panel;
        }

        VisualElement Chips(List<TacticGroup> groups, Func<Tactic> get, Action<Tactic> set, Tactic current, bool descriptions)
        {
            var box = Box();
            foreach (var g in groups)
            {
                var group = descriptions ? Box("tsc-panel", "tsc-gap-bottom") : Box("tsc-gap-bottom");
                group.Add(Text(g.Label, "tsc-label", "tsc-gap-bottom"));
                var row = Box("tsc-row");
                row.style.flexWrap = Wrap.Wrap;
                // Chips carry an 8px right margin; pull the row out by it so the last chip meets the panel padding.
                row.style.marginRight = -8;
                foreach (var o in g.Options)
                {
                    var option = o;
                    var chip = new Button();
                    chip.AddToClassList("tsc-chip");
                    bool selected = CoachViews.IsSelected(get(), o);
                    if (selected) chip.AddToClassList("tsc-chip--selected");
                    chip.style.flexGrow = 1; chip.style.flexBasis = 0;
                    chip.Add(Text(o.Label, "tsc-label"));
                    if (descriptions) chip.Add(Text(o.Description, "tsc-body"));
                    else if (current != null && CoachViews.IsSelected(current, o)) chip.Add(Text("현재", "tsc-body"));
                    chip.clicked += () => set(CoachViews.With(get(), option));
                    row.Add(chip);
                }
                group.Add(row);
                box.Add(group);
            }
            return box;
        }

        // ---------- screens ----------
        void ShowPreMatch()
        {
            var page = Page(0);
            var v = CoachViews.PreMatch(Session.Input);
            var columns = Box("tsc-row", "tsc-grow"); columns.style.alignItems = Align.Stretch;
            var left = PlayerPanel(v, 0); left.AddToClassList("tsc-gap-right");
            var middle = Box("tsc-grow", "tsc-gap-right");
            middle.Add(Text("경기 전 전술", "tsc-headline"));
            middle.Add(Text("경기 중에는 체인지오버(홀수 게임 뒤)에만 바꿀 수 있습니다.", "tsc-body", "tsc-on-ground-muted", "tsc-gap-bottom"));
            var chips = Box();
            void Redraw()
            {
                chips.Clear();
                chips.Add(Chips(v.Groups, () => pending, t => { pending = t; Redraw(); }, null, true));
            }
            Redraw();
            middle.Add(chips);
            var start = ButtonRow(MakeButton("경기 시작", () => StartMatch(), true));
            middle.Add(start);
            columns.Add(left); columns.Add(middle); columns.Add(PlayerPanel(v, 1));
            page.Add(columns);
        }

        void ShowMatch()
        {
            var page = Page(1);
            var columns = Box("tsc-row", "tsc-grow"); columns.style.alignItems = Align.Stretch;
            var main = Box("tsc-grow", "tsc-gap-right");
            var board = Box(); board.style.borderTopWidth = board.style.borderBottomWidth = board.style.borderLeftWidth = board.style.borderRightWidth = 1;
            board.style.borderTopColor = board.style.borderBottomColor = board.style.borderLeftColor = board.style.borderRightColor = Hex("#8c969e");
            board.style.paddingLeft = board.style.paddingRight = 16; board.style.paddingTop = board.style.paddingBottom = 8; board.style.marginBottom = 16;
            gameLabels = new Label[2]; pointLabels = new Label[2]; serveMarks = new VisualElement[2];
            var names = new[] { Session.Input.Players[0].Name, Session.Input.Players[1].Name };
            for (int i = 0; i < 2; i++)
            {
                // The serving mark is the ball itself, drawn as a dot rather than a glyph.
                serveMarks[i] = Box(); serveMarks[i].style.width = serveMarks[i].style.height = 12; serveMarks[i].style.backgroundColor = Hex("#ffeb04");
                serveMarks[i].style.borderTopLeftRadius = serveMarks[i].style.borderTopRightRadius = serveMarks[i].style.borderBottomLeftRadius = serveMarks[i].style.borderBottomRightRadius = 6;
                serveMarks[i].style.marginLeft = 8;
                gameLabels[i] = Number("0", "tsc-score"); gameLabels[i].style.width = 64; gameLabels[i].style.unityTextAlign = TextAnchor.MiddleRight;
                pointLabels[i] = Number("0", "tsc-score"); pointLabels[i].style.width = 96; pointLabels[i].style.unityTextAlign = TextAnchor.MiddleRight;
                board.Add(Row(Badge(i), Text(names[i], "tsc-title"), serveMarks[i], Box("tsc-grow"), gameLabels[i], pointLabels[i]));
            }
            main.Add(board);
            // The court takes all vertical space left by the scoreboard and controls; it letterboxes to keep proportions.
            court = new CourtView(); court.style.flexGrow = 1; court.style.minHeight = 200; court.style.marginBottom = 16;
            main.Add(court);
            var controls = Row();
            controls.style.borderTopWidth = 1; controls.style.borderTopColor = Hex("#8c969e"); controls.style.paddingTop = 16;
            var pause = MakeButton(Paused ? "재생" : "일시정지", null, true);
            pause.clicked += () => { Paused = !Paused; pause.text = Display(Paused ? "재생" : "일시정지", true); };
            controls.Add(pause);
            var speeds = Box("tsc-row");
            void DrawSpeeds()
            {
                speeds.Clear();
                foreach (var s in new[] { 1f, 4f, 16f })
                {
                    var speed = s;
                    var chip = new Button(() => { Speed = speed; DrawSpeeds(); }) { text = s + "×" };
                    ApplyFont(chip, chip.text, new[] { "tsc-label" });
                    chip.AddToClassList("tsc-chip"); chip.AddToClassList("tsc-label");
                    if (Mathf.Approximately(Speed, s)) chip.AddToClassList("tsc-chip--selected");
                    chip.style.marginLeft = 8; chip.style.marginBottom = 0;
                    speeds.Add(chip);
                }
            }
            DrawSpeeds();
            controls.Add(speeds);
            controls.Add(MakeButton("다음 체인지오버까지", SkipToNextStop, false));
            pointInfo = Text("", "tsc-label", "tsc-on-ground-muted"); pointInfo.style.marginLeft = 16;
            controls.Add(pointInfo);
            main.Add(controls);

            var side = Box(); side.style.width = 300;
            var now = Box("tsc-panel", "tsc-gap-bottom");
            now.Add(Text("현재 전술", "tsc-label", "tsc-gap-bottom"));
            tacticLabels = new Label[2];
            for (int i = 0; i < 2; i++) { tacticLabels[i] = Text("", "tsc-body"); now.Add(Row(Badge(i), tacticLabels[i])); }
            now.Add(Text("전술은 체인지오버에서만 바꿀 수 있습니다.", "tsc-body", "tsc-muted"));
            side.Add(now);
            var feed = Box("tsc-panel", "tsc-grow");
            feed.Add(Text("최근 포인트", "tsc-label", "tsc-gap-bottom"));
            feedList = Box(); feed.Add(feedList); feedCount = -1;
            side.Add(feed);
            columns.Add(main); columns.Add(side);
            page.Add(columns);
            RefreshMatch();
        }

        void RefreshMatch()
        {
            if (court == null || Session.Phase != CoachPhase.Playing) return;
            var v = CoachViews.Match(Session);
            for (int i = 0; i < 2; i++)
            {
                gameLabels[i].text = v.Games[i].ToString();
                pointLabels[i].text = v.PointText[i];
                serveMarks[i].style.visibility = v.Server == i ? Visibility.Visible : Visibility.Hidden;
                tacticLabels[i].text = Display(v.Tactics[i], false);
            }
            pointInfo.text = Display("포인트 " + v.Point + " · 게임 " + v.Game + " · " + Speed + "×", true);
            court.Set(v);
            if (Session.Points.Count != feedCount)
            {
                feedCount = Session.Points.Count;
                feedList.Clear();
                foreach (var f in v.Feed)
                {
                    var n = Number(f.Point.ToString(), "tsc-stat"); n.style.width = 36;
                    var item = Row(n, Badge(f.Winner), Text(f.Text, "tsc-body")); item.style.marginBottom = 8;
                    feedList.Add(item);
                }
            }
        }

        void ShowChangeover()
        {
            court = null;
            var page = Page(2);
            var v = CoachViews.Changeover(Session);
            pending = v.CurrentA.Copy();
            var columns = Box("tsc-row", "tsc-grow"); columns.style.alignItems = Align.Stretch;
            var main = Box("tsc-grow", "tsc-gap-right");
            main.AddToClassList("tsc-changeover");
            main.Add(Text(v.Heading, "tsc-label", "tsc-on-ground-muted"));
            main.Add(Row(Badge(0), Text(v.Names[0] + " " + v.Games[0] + " – " + v.Games[1] + " " + v.Names[1], "tsc-headline"), Badge(1, true)));
            if (v.OpponentChanged)
            {
                var banner = Box("tsc-banner"); banner.style.marginTop = 16;
                // A symbol, not a label: no uppercase. Size in USS, weight from the Bold file.
                banner.Add(Text("i", "tsc-banner__icon"));
                var words = Box("tsc-grow");
                words.Add(Text(v.OpponentKicker, "tsc-label", "tsc-muted"));
                words.Add(Text(v.OpponentText, "tsc-body"));
                banner.Add(words);
                main.Add(banner);
            }
            var table = Box("tsc-panel"); table.style.marginTop = 16;
            var head = Row(Text("", "tsc-cell--label"), Text("이번 구간", "tsc-label"), Box("tsc-grow"), Text("경기 누적", "tsc-label"));
            table.Add(head);
            table.Add(StatHeader(v.Names, true));
            foreach (var r in v.Rows) table.Add(StatLine(r, true));
            table.Add(Text(v.RallyNote, "tsc-body", "tsc-muted"));
            main.Add(table);
            var notes = Box("tsc-row"); notes.style.marginTop = 16;
            // Basis 0: the cards share the row width equally and wrap their text instead of taking its unwrapped width.
            foreach (var o in v.Observations) { var card = Box("tsc-panel", "tsc-grow"); card.style.flexBasis = 0; card.style.marginRight = 16; card.Add(Text(o, "tsc-body")); notes.Add(card); }
            main.Add(notes);

            var side = Box("tsc-panel"); side.style.width = 360;
            side.Add(Text("내 전술 변경", "tsc-title"));
            side.Add(Text("다음 포인트부터 적용됩니다. " + v.NextChangeover, "tsc-body", "tsc-muted", "tsc-gap-bottom"));
            var chips = Box();
            var summary = Text("", "tsc-body");
            void Redraw()
            {
                chips.Clear();
                chips.Add(Chips(v.Groups, () => pending, t => { pending = t; Redraw(); }, v.CurrentA, false));
                summary.text = Display(CoachSession.Same(pending, v.CurrentA) ? "변경 없음: " + v.CurrentAText + " 유지" : "변경: " + CoachText.Tactic(pending), false);
            }
            Redraw();
            side.Add(chips);
            var summaryBox = Box("tsc-inset", "tsc-inset--compact"); summaryBox.style.marginTop = 8; summaryBox.style.marginBottom = 16;
            summaryBox.Add(summary);
            side.Add(summaryBox);
            side.Add(ButtonRow(MakeButton("유지하고 계속", () => Resume(null), false), MakeButton("적용하고 계속", () => Resume(pending), true)));
            columns.Add(main); columns.Add(side);
            page.Add(columns);
        }

        VisualElement StatHeader(string[] names, bool withMatch)
        {
            var row = Row(Text("", "tsc-cell--label"));
            int columns = withMatch ? 2 : 1;
            for (int c = 0; c < columns; c++)
                for (int i = 0; i < 2; i++) { var cell = Row(Badge(i), Text(names[i], "tsc-label")); cell.style.width = 110; cell.style.justifyContent = Justify.FlexEnd; row.Add(cell); }
            return row;
        }

        VisualElement StatLine(StatRow r, bool withMatch)
        {
            var row = Box("tsc-table-row");
            row.Add(Text(r.Label, "tsc-body", "tsc-cell", "tsc-cell--label"));
            foreach (var value in withMatch ? new[] { r.A, r.B, r.MatchA, r.MatchB } : new[] { r.A, r.B })
            {
                var cell = Number(value ?? "", "tsc-stat"); cell.AddToClassList("tsc-cell"); cell.style.width = 110;
                row.Add(cell);
            }
            return row;
        }

        void ShowReview()
        {
            court = null;
            var page = Page(3);
            var v = CoachViews.Review(Session);
            var top = Row(Badge(0), Text(v.Names[0] + " " + v.Games[0] + " – " + v.Games[1] + " " + v.Names[1], "tsc-headline"), Badge(1, true),
                ButtonRow(true, MakeButton("같은 Seed로 다시", () => NewSession(Seed), false), MakeButton("다음 경기 준비", () => NewSession(Seed + 1), true)));
            page.Add(Text("경기 리뷰 · " + v.Points + "포인트 · " + (v.Winner == 0 ? "승리" : "패배"), "tsc-label", "tsc-on-ground-muted"));
            page.Add(top);

            var timeline = Box("tsc-panel"); timeline.style.marginTop = 16; timeline.style.marginBottom = 16;
            timeline.Add(Text("전술 구간별 흐름 · 카드 아래는 게임별 승자", "tsc-label", "tsc-gap-bottom"));
            // Segment cards in rows built here, not flex-wrap (Yoga does not grow the parent for wrapped lines): n cards
            // per row from the width, all the same width, each row as tall as its tallest card. Empty slots in the last
            // row are invisible fillers. Rows are rebuilt only when n changes.
            var cards = new List<VisualElement>();
            foreach (var s in v.Segments)
            {
                var cell = Box("tsc-card"); cell.style.flexGrow = 1; cell.style.flexBasis = 0; cell.style.paddingLeft = cell.style.paddingRight = 4;
                var inner = Box("tsc-inset", "tsc-inset--compact"); inner.style.flexGrow = 1;
                inner.Add(Text(s.Games, "tsc-label"));
                inner.Add(Text("A " + s.TacticA, "tsc-body"));
                inner.Add(Text("B " + s.TacticB, "tsc-body", "tsc-muted"));
                inner.Add(Number("A " + s.Won + "/" + s.Points, "tsc-stat"));
                // Game winners live inside their segment card, one per line (no flex-wrap), so they move with the card.
                var winners = Box(); winners.style.marginTop = 8; winners.style.alignItems = Align.FlexStart;
                for (int g = s.FirstGame; g <= s.LastGame && g <= v.GameWinners.Count; g++)
                {
                    int w = v.GameWinners[g - 1];
                    var chip = Text(g + " " + v.Names[w], "tsc-label", "tsc-badge", w == 0 ? "tsc-badge--a" : "tsc-badge--b");
                    chip.style.borderTopLeftRadius = chip.style.borderTopRightRadius = chip.style.borderBottomLeftRadius = chip.style.borderBottomRightRadius = 4;
                    chip.style.marginTop = 4;
                    winners.Add(chip);
                }
                inner.Add(winners);
                cell.Add(inner); cards.Add(cell);
            }
            var grid = Box(); grid.style.marginLeft = grid.style.marginRight = -4;
            int perRow = 0;
            grid.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                // n = floor((W + 8) / (180 + 8)) for panel content width W: the grid is pulled out 4px each side, so its
                // own width is already W + 8, and each 180px card carries 4px padding on both sides.
                int n = Math.Max(1, Mathf.FloorToInt(grid.contentRect.width / (180 + 8)));
                n = Math.Min(n, Math.Max(1, cards.Count));
                if (n == perRow) return;
                perRow = n;
                grid.Clear();
                for (int i = 0; i < cards.Count; i += n)
                {
                    var line = Box("tsc-row"); line.style.alignItems = Align.Stretch; line.style.marginBottom = 8;
                    for (int j = 0; j < n; j++)
                    {
                        if (i + j < cards.Count) line.Add(cards[i + j]);
                        else { var filler = Box("tsc-card"); filler.style.flexGrow = 1; filler.style.flexBasis = 0; filler.style.visibility = Visibility.Hidden; line.Add(filler); }
                    }
                    grid.Add(line);
                }
            });
            timeline.Add(grid);
            page.Add(timeline);

            var columns = Box("tsc-row", "tsc-grow"); columns.style.alignItems = Align.Stretch;
            var map = Box("tsc-panel", "tsc-gap-right"); map.style.width = 300;
            map.Add(Text(v.Names[0] + " 랠리 샷 첫 착지 · " + v.Landings.Count + "구", "tsc-label", "tsc-gap-bottom"));
            // Fills the panel height; draws the whole half court (net to beyond the baseline and sidelines) letterboxed.
            var half = new HalfCourtView(v.Landings); half.AddToClassList("tsc-inset"); half.AddToClassList("tsc-inset--compact"); half.style.flexGrow = 1; half.style.minHeight = 200; half.style.marginBottom = 8;
            map.Add(half);
            map.Add(Text("주황 채움 = 백핸드 쪽 샷 " + v.BackhandTargets + "구 · 회색 빈 원 = 그 외", "tsc-body", "tsc-muted", "tsc-legend"));
            map.Add(Text("흰 빈 원 = 아웃 " + v.Outs + "구", "tsc-body", "tsc-muted", "tsc-legend"));
            var table = Box("tsc-panel", "tsc-grow"); table.style.flexBasis = 0;
            table.Add(Text("경기 전체 기록", "tsc-label", "tsc-gap-bottom"));
            table.Add(StatHeader(v.Names, false));
            foreach (var r in v.Summary) table.Add(StatLine(r, false));
            columns.Add(map); columns.Add(table);
            page.Add(columns);
        }
    }
}
