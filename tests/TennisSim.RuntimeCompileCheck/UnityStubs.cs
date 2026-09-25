// Minimal stand-ins for the UnityEngine and UnityEngine.UIElements members the coach Runtime uses (Unity 6
// signatures). Bodies are empty: this project is only compiled, never run. Add a member here only after checking it
// in the Unity 6 scripting reference; a wrong stub hides a real Unity API error.
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { public Transform transform => null; public GameObject gameObject => null; }
    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }
    public class Transform : Component { public void SetParent(Transform parent, bool worldPositionStays) { } }
    public class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
    }
    public static class Time { public static float unscaledDeltaTime => 0; }
    public static class Debug { public static void LogError(object message) { } }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }
    public class Font : Object { }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color clear => default;
    }
    public static class ColorUtility { public static bool TryParseHtmlString(string htmlString, out Color color) { color = default; return true; } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => default;
        public static Vector2 operator *(Vector2 a, float d) => default;
    }
    public struct Vector2Int { public Vector2Int(int x, int y) { } }
    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x, y, width, height;
        public float xMin => x; public float yMin => y; public float xMax => x + width; public float yMax => y + height;
    }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public static float Max(float a, float b) => 0; public static int Max(int a, int b) => 0;
        public static float Min(float a, float b) => 0; public static int Min(int a, int b) => 0;
        public static float Abs(float f) => 0; public static float Sin(float f) => 0; public static float Cos(float f) => 0;
        public static float Ceil(float f) => 0; public static int FloorToInt(float f) => 0;
        public static float Clamp(float value, float min, float max) => 0; public static float Clamp01(float value) => 0;
        public static bool Approximately(float a, float b) => false;
    }
}

namespace UnityEngine.UIElements
{
    public enum Align { Auto, FlexStart, Center, FlexEnd, Stretch }
    public enum Justify { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround }
    public enum Wrap { NoWrap, Wrap, WrapReverse }
    public enum DisplayStyle { Flex, None }
    public enum Visibility { Visible, Hidden }
    public enum ScrollViewMode { Vertical, Horizontal, VerticalAndHorizontal }
    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }
    public enum PanelScaleMode { ConstantPixelSize, ConstantPhysicalSize, ScaleWithScreenSize }
    public enum PanelScreenMatchMode { MatchWidthOrHeight, ShrinkToFit, ExpandToFill }
    public enum LengthUnit { Pixel, Percent }

    public struct Length { public Length(float value, LengthUnit unit) { } public static Length Percent(float value) => default; }
    public struct StyleLength { public static implicit operator StyleLength(float v) => default; public static implicit operator StyleLength(Length v) => default; }
    public struct StyleFloat { public static implicit operator StyleFloat(float v) => default; }
    public struct StyleColor { public static implicit operator StyleColor(Color v) => default; }
    public struct StyleEnum<T> where T : struct, IConvertible { public static implicit operator StyleEnum<T>(T v) => default; }
    public struct FontDefinition { public static FontDefinition FromFont(Font f) => default; }
    public struct StyleFontDefinition { public StyleFontDefinition(FontDefinition f) { } }

    public interface IStyle
    {
        StyleEnum<Align> alignItems { get; set; } StyleEnum<Align> alignContent { get; set; } StyleEnum<Align> alignSelf { get; set; }
        StyleEnum<Justify> justifyContent { get; set; } StyleEnum<Wrap> flexWrap { get; set; }
        StyleEnum<DisplayStyle> display { get; set; } StyleEnum<Visibility> visibility { get; set; }
        StyleEnum<TextAnchor> unityTextAlign { get; set; } StyleFontDefinition unityFontDefinition { get; set; }
        StyleFloat flexGrow { get; set; } StyleFloat flexShrink { get; set; } StyleLength flexBasis { get; set; }
        StyleLength width { get; set; } StyleLength height { get; set; } StyleLength minWidth { get; set; } StyleLength minHeight { get; set; }
        StyleLength marginTop { get; set; } StyleLength marginBottom { get; set; } StyleLength marginLeft { get; set; } StyleLength marginRight { get; set; }
        StyleLength paddingTop { get; set; } StyleLength paddingBottom { get; set; } StyleLength paddingLeft { get; set; } StyleLength paddingRight { get; set; }
        StyleFloat borderTopWidth { get; set; } StyleFloat borderBottomWidth { get; set; } StyleFloat borderLeftWidth { get; set; } StyleFloat borderRightWidth { get; set; }
        StyleColor borderTopColor { get; set; } StyleColor borderBottomColor { get; set; } StyleColor borderLeftColor { get; set; } StyleColor borderRightColor { get; set; }
        StyleLength borderTopLeftRadius { get; set; } StyleLength borderTopRightRadius { get; set; } StyleLength borderBottomLeftRadius { get; set; } StyleLength borderBottomRightRadius { get; set; }
        StyleColor backgroundColor { get; set; } StyleColor color { get; set; }
    }
    public interface IResolvedStyle
    {
        DisplayStyle display { get; }
        float marginLeft { get; } float marginRight { get; } float paddingLeft { get; } float paddingRight { get; }
        float borderLeftWidth { get; } float borderRightWidth { get; }
    }

    public class EventBase { }
    public class GeometryChangedEvent : EventBase { public Rect oldRect => default; public Rect newRect => default; }
    public delegate void EventCallback<in TEventType>(TEventType evt);
    public class Painter2D
    {
        public Color strokeColor { get; set; } public Color fillColor { get; set; } public float lineWidth { get; set; }
        public void BeginPath() { } public void ClosePath() { } public void MoveTo(Vector2 pos) { } public void LineTo(Vector2 pos) { }
        public void Stroke() { } public void Fill() { }
    }
    public class MeshGenerationContext { public Painter2D painter2D => null; }

    public class VisualElement
    {
        public enum MeasureMode { Undefined, Exactly, AtMost }
        public IStyle style => null;
        public IResolvedStyle resolvedStyle => null;
        public Rect layout => default;
        public Rect contentRect => default;
        public int childCount => 0;
        public VisualElement this[int key] => null;
        public Action<MeshGenerationContext> generateVisualContent { get; set; }
        public void Add(VisualElement child) { }
        public void Clear() { }
        public IEnumerable<VisualElement> Children() => null;
        public VisualElementStyleSheetSet styleSheets => null;
        public void AddToClassList(string className) { }
        public void RemoveFromClassList(string className) { }
        public bool ClassListContains(string cls) => false;
        public void EnableInClassList(string className, bool enable) { }
        public void MarkDirtyRepaint() { }
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback) where TEventType : EventBase, new() { }
    }
    public class TextElement : VisualElement
    {
        public string text { get; set; }
        public Vector2 MeasureTextSize(string textToMeasure, float width, MeasureMode widthMode, float height, MeasureMode heightMode) => default;
    }
    public class Label : TextElement { public Label() { } public Label(string text) { } }
    public class Button : TextElement { public Button() { } public Button(Action clickEvent) { } public event Action clicked; }
    public class ScrollView : VisualElement
    {
        public ScrollView() { } public ScrollView(ScrollViewMode scrollViewMode) { }
        public VisualElement contentViewport => null;
        public ScrollerVisibility horizontalScrollerVisibility { get; set; }
    }
    public class StyleSheet : ScriptableObject { }
    public class ThemeStyleSheet : StyleSheet { }
    public class VisualElementStyleSheetSet { public void Add(StyleSheet styleSheet) { } }
    public class PanelSettings : ScriptableObject
    {
        public ThemeStyleSheet themeStyleSheet { get; set; } public PanelScaleMode scaleMode { get; set; }
        public Vector2Int referenceResolution { get; set; } public PanelScreenMatchMode screenMatchMode { get; set; } public float match { get; set; }
    }
    public class UIDocument : MonoBehaviour { public PanelSettings panelSettings { get; set; } public VisualElement rootVisualElement => null; }
}
