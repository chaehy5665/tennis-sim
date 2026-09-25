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
            var title = Text("TennisSim 코치", "tsc-title", "tsc-header__title");
            header.Add(title);
            var steps = Box("tsc-steps");
            for (int i = 0; i < Steps.Length; i++) steps.Add(Text((i + 1) + " " + Steps[i], "tsc-label", "tsc-step", i == step ? "tsc-step--current" : "tsc-step-other"));
            header.Add(steps);
            var info = Text("Seed " + Seed + " · 1세트 · 하드코트", "tsc-label", "tsc-on-ground-muted", "tsc-header__info");
            header.Add(info);
            // Nothing in the header shrinks. The match info is either whole or hidden: it shows only when the header
            // fits the title, the step items, 32px and the info's natural width. That width is the larger of its
            // rendered width (while shown) and MeasureTextSize plus letter spacing (0.72px per character, in case the
            // measure leaves it out), measured after ApplyFont set the font. Hiding it never changes the title or step
            // widths, so the check cannot feed back.
            float infoWidth = 0;
            header.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float measured = info.MeasureTextSize(info.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x + .72f * info.text.Length;
                infoWidth = Mathf.Max(infoWidth, Mathf.Ceil(measured));
                if (info.resolvedStyle.display == DisplayStyle.Flex) infoWidth = Mathf.Max(infoWidth, Mathf.Ceil(info.layout.width));
                float stepsWidth = 0;
                foreach (var s in steps.Children()) stepsWidth += s.layout.width + s.resolvedStyle.marginLeft + s.resolvedStyle.marginRight;
                bool fits = header.contentRect.width >= title.layout.width + stepsWidth + 32 + infoWidth;
                var display = fits ? DisplayStyle.Flex : DisplayStyle.None;
                if (info.resolvedStyle.display != display) info.style.display = display;
            });
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

        // Which chip the changeover's description box explains: the hovered chip, else the focused one, else the last
        // clicked one. Chips are rebuilt on every click, so hover and focus are cleared with them; the click remains.
        sealed class ChoiceFocus
        {
            public TacticOption Hovered, Focused, Clicked;
            public Action Changed;
            public TacticOption Shown => Hovered ?? Focused ?? Clicked;
        }

        VisualElement Chips(List<TacticGroup> groups, Func<Tactic> get, Action<Tactic> set, Tactic current, bool descriptions, ChoiceFocus focus = null)
        {
            var box = Box();
            foreach (var g in groups)
            {
                var group = descriptions ? Box("tsc-panel", "tsc-gap-bottom") : Box("tsc-gap-bottom");
                group.Add(Text(g.Label, "tsc-label", "tsc-gap-bottom"));
                var row = Box("tsc-row");
                // Chips are flex-basis 0 / grow 1, so this row never actually wraps. If chips get a minimum width, build
                // the rows in C# like the review segment cards: Yoga does not grow the parent for wrapped lines.
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
                    if (focus != null)
                    {
                        chip.RegisterCallback<PointerEnterEvent>(_ => { focus.Hovered = option; focus.Changed(); });
                        chip.RegisterCallback<PointerLeaveEvent>(_ => { if (focus.Hovered == option) { focus.Hovered = null; focus.Changed(); } });
                        chip.RegisterCallback<FocusInEvent>(_ => { focus.Focused = option; focus.Changed(); });
                        chip.RegisterCallback<FocusOutEvent>(_ => { if (focus.Focused == option) { focus.Focused = null; focus.Changed(); } });
                    }
                    chip.clicked += () =>
                    {
                        if (focus != null) { focus.Clicked = option; focus.Hovered = focus.Focused = null; }
                        set(CoachViews.With(get(), option));
                    };
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
            // Evidence column (design system changeover.md): heading, banner, direction | serve course, segment comparison.
            var main = Box("tsc-grow", "tsc-gap-right");
            main.AddToClassList("tsc-changeover");
            main.Add(Text(v.Heading, "tsc-label", "tsc-on-ground-muted"));
            main.Add(Row(Badge(0), Text(v.Names[0] + " " + v.Games[0] + " – " + v.Games[1] + " " + v.Names[1], "tsc-headline"), Badge(1, true)));
            // One InfoBanner for the opponent coach: a change ("… 코치가 전술을 바꿨습니다") or, without a change, a note on
            // the user's change ("… 코치가 지켜보고 있습니다"). Same component, kicker and body either way.
            if (v.OpponentBanner)
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
            var pair = Box("tsc-row"); pair.style.marginTop = 16; pair.style.alignItems = Align.Stretch;
            var direction = EvidenceView(v.Direction); direction.style.flexGrow = 1; direction.style.flexBasis = 0; direction.style.marginRight = 16;
            var serve = EvidenceView(v.Serve); serve.style.flexGrow = 1; serve.style.flexBasis = 0;
            pair.Add(direction); pair.Add(serve);
            main.Add(pair);
            var compare = EvidenceView(v.Compare, v.Names); compare.style.marginTop = 16;
            main.Add(compare);

            // Decision column (changeover.md "버튼 상태"): chips, the choice description (2 lines) and the change summary
            // (3 lines). Both boxes have a fixed height, so clicking a chip never moves the buttons.
            var side = Box("tsc-panel"); side.style.width = 360;
            side.Add(Text("내 전술 변경", "tsc-title"));
            side.Add(Text("다음 포인트부터 적용됩니다. " + v.NextChangeover, "tsc-body", "tsc-muted", "tsc-gap-bottom"));
            var chips = Box();
            var describe = Box("tsc-inset", "tsc-inset--compact", "tsc-inset--lines-2"); describe.style.marginTop = 8;
            var summary = Box("tsc-inset", "tsc-inset--compact", "tsc-inset--lines-3"); summary.style.marginTop = 8; summary.style.marginBottom = 16;
            var buttons = Box();
            var focus = new ChoiceFocus();
            // The chip name beside its description; the description wraps in its own column (at most two lines, measured
            // with Pretendard for every option at 310px).
            void Describe()
            {
                describe.Clear();
                var o = focus.Shown;
                if (o == null) { describe.Add(Text(CoachViews.ChoiceHint, "tsc-body", "tsc-muted")); return; }
                var name = Text(o.Label, "tsc-label"); name.style.marginRight = 8; name.style.marginTop = 3; name.style.flexShrink = 0;
                var text = Text(o.Description, "tsc-body"); text.style.flexGrow = 1; text.style.flexShrink = 1;
                var line = Box("tsc-row"); line.style.alignItems = Align.FlexStart;
                line.Add(name); line.Add(text);
                describe.Add(line);
            }
            focus.Changed = Describe;
            // Unchanged: one primary "그대로 계속". Changed: secondary "변경 취소" (back to the current tactic) and
            // primary "적용하고 계속". The primary always continues with the chips as they are.
            void Redraw()
            {
                chips.Clear();
                chips.Add(Chips(v.Groups, () => pending, t => { pending = t; Redraw(); }, v.CurrentA, false, focus));
                Describe();
                summary.Clear();
                foreach (var line in CoachViews.ChangeSummary(v.CurrentA, pending)) summary.Add(Text(line, "tsc-body"));
                buttons.Clear();
                buttons.Add(CoachSession.Same(pending, v.CurrentA)
                    ? ButtonRow(MakeButton("그대로 계속", () => Resume(null), true))
                    : ButtonRow(MakeButton("변경 취소", () => { pending = v.CurrentA.Copy(); focus.Clicked = null; Redraw(); }, false), MakeButton("적용하고 계속", () => Resume(pending), true)));
            }
            Redraw();
            side.Add(chips);
            side.Add(describe);
            side.Add(summary);
            side.Add(buttons);
            columns.Add(main); columns.Add(side);
            page.Add(columns);
        }

        // One evidence panel: title (same name as the chip group), sample size, "참고용" SampleTag when the whole panel
        // is below its threshold, SplitBar lines, then a StatTable and the note lines. Only values are ever muted; the
        // view model decides which (EvidencePanel.MuteAll for a tagged panel, Muted/MatchMuted for a row or group).
        // With names, the table is the segment comparison: a Badge per player over its "직전"/"이번" columns, and the
        // "직전" values in line-muted. With Groups, a group row ("이번 구간" / "경기 누적") spans each group's cells,
        // 16px apart.
        VisualElement EvidenceView(EvidencePanel p, string[] names = null)
        {
            var panel = Box("tsc-panel");
            var head = Row(Text(p.Title, "tsc-title"), Box("tsc-grow"), Text(p.Sample, "tsc-stat", "tsc-muted"));
            if (p.SampleTag) { var tag = Text("참고용", "tsc-label", "tsc-sample-tag"); tag.style.marginLeft = 8; head.Add(tag); }
            head.style.marginBottom = 8;
            panel.Add(head);
            foreach (var s in p.Split)
            {
                var split = Box("tsc-split", "tsc-grow");
                var labels = Box("tsc-split__labels");
                labels.Add(Text(s.LeftText, "tsc-stat", s.Muted ? "tsc-muted" : "tsc-plain"));
                labels.Add(Text(s.RightText, "tsc-stat", s.Muted ? "tsc-muted" : "tsc-plain"));
                var bar = Box("tsc-split__bar");
                var fill = Box("tsc-split__fill"); fill.style.width = Length.Percent(100 * Mathf.Clamp01(s.Backhand));
                bar.Add(fill);
                split.Add(labels); split.Add(bar);
                var name = Text(s.Label, "tsc-label"); name.style.width = 40;
                var line = Row(name, split); line.style.marginBottom = 8;
                panel.Add(line);
            }
            int cellWidth = p.Compact ? 72 : 80;
            Label Cell(string text, params string[] classes)
            {
                var cell = Text(text, classes);
                cell.AddToClassList("tsc-cell");
                if (p.Compact) cell.AddToClassList("tsc-cell--compact");
                return cell;
            }
            // The first cell after the segment's columns starts the match group, 16px (space-3) further right.
            void Gap(VisualElement cell) => cell.style.marginLeft = 16;
            int perPlayer = names == null ? 0 : p.Columns.Length / 2;
            if (names != null)
            {
                var badges = Row(Text("", "tsc-cell", "tsc-cell--label"));
                for (int i = 0; i < 2; i++) { var cell = Row(Badge(i), Text(names[i], "tsc-label")); cell.style.width = cellWidth * perPlayer; cell.style.justifyContent = Justify.FlexEnd; badges.Add(cell); }
                panel.Add(badges);
            }
            if (p.Groups.Length > 0)
            {
                var groups = Box("tsc-table-group");
                groups.Add(Text("", "tsc-cell", "tsc-cell--label"));
                for (int g = 0; g < p.Groups.Length; g++)
                {
                    var cell = Text(p.Groups[g], "tsc-label");
                    cell.style.width = cellWidth * (g == 0 ? p.Columns.Length : p.MatchColumns.Length);
                    cell.style.unityTextAlign = TextAnchor.MiddleRight;
                    if (g > 0) Gap(cell);
                    groups.Add(cell);
                }
                panel.Add(groups);
            }
            var columns = Row(Text("", "tsc-cell", "tsc-cell--label"));
            foreach (var c in p.Columns) columns.Add(Cell(c, "tsc-label"));
            for (int i = 0; i < p.MatchColumns.Length; i++) { var cell = Cell(p.MatchColumns[i], "tsc-label"); if (i == 0) Gap(cell); columns.Add(cell); }
            panel.Add(columns);
            foreach (var r in p.Rows)
            {
                var row = Box("tsc-table-row");
                if (r.Note == null) row.Add(Text(r.Label, "tsc-body", "tsc-cell", "tsc-cell--label"));
                else
                {
                    // A two-line item name: the name, then what it holds in 12px line-muted.
                    var item = Box("tsc-cell--label"); item.style.flexGrow = 1;
                    item.Add(Text(r.Label, "tsc-body"));
                    item.Add(Text(r.Note, "tsc-cell__note"));
                    row.Add(item);
                }
                for (int i = 0; i < r.Values.Length; i++)
                {
                    bool previous = perPlayer == 2 && i % 2 == 0;
                    row.Add(Cell(r.Values[i], "tsc-stat", r.Muted || previous ? "tsc-muted" : "tsc-plain"));
                }
                if (r.MatchValues != null)
                    for (int i = 0; i < r.MatchValues.Length; i++) { var cell = Cell(r.MatchValues[i], "tsc-stat", r.MatchMuted ? "tsc-muted" : "tsc-plain"); if (i == 0) Gap(cell); row.Add(cell); }
                panel.Add(row);
            }
            for (int i = 0; i < p.Notes.Count; i++)
            {
                var note = Text(p.Notes[i].Text, "tsc-body", p.Notes[i].Muted ? "tsc-muted" : "tsc-plain");
                if (i == 0) note.style.marginTop = 8;
                panel.Add(note);
            }
            return panel;
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
                        // Same padding, grow and basis as a real card, so every row splits its width identically.
                        else { var filler = Box("tsc-card"); filler.style.flexGrow = 1; filler.style.flexBasis = 0; filler.style.paddingLeft = filler.style.paddingRight = 4; filler.style.visibility = Visibility.Hidden; line.Add(filler); }
                    }
                    grid.Add(line);
                }
            });
            timeline.Add(grid);
            page.Add(timeline);

            // The opponent coach's changes, one Inset line each: where, what changed, and its stated reason.
            var changes = Box("tsc-panel"); changes.style.marginBottom = 16;
            changes.Add(Text(v.Names[1] + " 코치의 변경", "tsc-label", "tsc-gap-bottom"));
            if (v.OpponentChanges.Count == 0) changes.Add(Text(v.NoOpponentChange, "tsc-body", "tsc-muted"));
            foreach (var c in v.OpponentChanges)
            {
                var line = Box("tsc-inset", "tsc-inset--compact"); line.style.marginBottom = 8;
                line.Add(Text(c.Kicker, "tsc-label", "tsc-inset__kicker"));
                line.Add(Text(c.Change, "tsc-body"));
                line.Add(Text(c.Reasons, "tsc-body", "tsc-muted"));
                changes.Add(line);
            }
            page.Add(changes);

            var columns = Box("tsc-row", "tsc-grow"); columns.style.alignItems = Align.Stretch;
            var map = Box("tsc-panel", "tsc-gap-right"); map.style.width = 300;
            map.Add(Text(v.Names[0] + " 랠리 샷 첫 착지", "tsc-label", "tsc-gap-bottom"));
            // Filter chips: all shots, or only those hit under each attack-direction tactic actually used.
            var filterRow = Box("tsc-row"); filterRow.style.marginBottom = 8;
            map.Add(filterRow);
            // Fills the panel height; draws the whole half court (net to beyond the baseline and sidelines) letterboxed.
            var half = new HalfCourtView(v.Landings); half.AddToClassList("tsc-inset"); half.AddToClassList("tsc-inset--compact"); half.style.flexGrow = 1; half.style.minHeight = 200; half.style.marginBottom = 8;
            map.Add(half);
            var count = Text("", "tsc-body", "tsc-legend");
            map.Add(count);
            map.Add(Text("채운 원 = 백핸드 쪽 샷 · 빈 원 = 그 외 · 흰 빈 원 = 아웃", "tsc-body", "tsc-muted", "tsc-legend"));
            string filter = "all";
            void DrawFilter()
            {
                var shown = CoachViews.Filter(v.Landings, filter);
                half.Set(shown);
                count.text = Display(CoachViews.LandingLegend(shown) + " · 아웃 " + shown.Count(l => !l.In) + "구", false);
                filterRow.Clear();
                foreach (var f in v.LandingFilters)
                {
                    var option = f;
                    var chip = new Button(() => { filter = option.Key; DrawFilter(); }) { text = Display(f.Label, true) };
                    chip.AddToClassList("tsc-chip"); chip.AddToClassList("tsc-label");
                    ApplyFont(chip, f.Label, new[] { "tsc-label" });
                    if (f.Key == filter) chip.AddToClassList("tsc-chip--selected");
                    chip.style.marginBottom = 0;
                    filterRow.Add(chip);
                }
            }
            DrawFilter();
            var table = Box("tsc-panel", "tsc-grow"); table.style.flexBasis = 0;
            table.Add(Text("경기 전체 기록", "tsc-label", "tsc-gap-bottom"));
            table.Add(StatHeader(v.Names, false));
            foreach (var r in v.Summary) table.Add(StatLine(r, false));
            columns.Add(map); columns.Add(table);
            page.Add(columns);
        }
    }
}
