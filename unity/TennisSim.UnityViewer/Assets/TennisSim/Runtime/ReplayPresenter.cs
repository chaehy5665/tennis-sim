using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TennisSim.Viewer
{
    public sealed class ReplayPresenter : MonoBehaviour
    {
        public string ReplayFile = "sample-42.json";
        public ReplayController Controller { get; private set; }
        public string Error { get; private set; } = "";
        public Transform PlayerA { get; private set; }
        public Transform PlayerB { get; private set; }
        public Transform Ball { get; private set; }
        public Transform Marker { get; private set; }
        public ReplayEvent MarkerEvent { get; private set; }
        public bool EnlargeBall = true;
        readonly List<Material> materials = new List<Material>();
        readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();
        string pathInput;
        GameObject stage;
        Vector2 hudScroll;
        const float PlayerHeight = 1.8f;
        void Start()
        {
            BuildStage(); pathInput = Path.Combine(Application.streamingAssetsPath, "Replays", ReplayFile); Load(pathInput);
        }
        public void Load(string path)
        {
            Controller = null; Error = ""; MarkerEvent = null;
            if (Marker != null) Marker.gameObject.SetActive(false);
            try { Controller = new ReplayController(new ReplayTimeline(ReplayLoader.Load(path))); Present(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException)
            { Error = ex.Message; }
            if (PlayerA != null) { PlayerA.gameObject.SetActive(Controller != null); PlayerB.gameObject.SetActive(Controller != null); Ball.gameObject.SetActive(Controller != null); }
        }
        void Update() { if (Controller != null) { Controller.Advance(Time.unscaledDeltaTime); Present(); } }
        public static Vector3 Map(Position p) { var m = CoordinateMapper.Map(p); return new Vector3((float)m.X, (float)m.Y, (float)m.Z); }
        public void Present()
        {
            if (Controller == null || Ball == null) return;
            var state = Controller.State;
            PlayerA.position = Map(state.A) + Vector3.up * (PlayerHeight / 2);
            PlayerB.position = Map(state.B) + Vector3.up * (PlayerHeight / 2);
            Ball.position = Map(state.Ball);
            Ball.localScale = Vector3.one * (float)(CoordinateMapper.BallRadius * 2 * (EnlargeBall ? 3 : 1));
            var events = Controller.Timeline.Data.Events;
            int index = Controller.SelectedEvent >= 0 ? Controller.SelectedEvent : Controller.Timeline.LastEventIndex(Controller.Time);
            MarkerEvent = null;
            // Reconstruct a single current-point marker on every seek; no accumulated history.
            for (int i = index; i >= 0 && events[i].Point == state.Point; i--)
                if (events[i].IsMarker) { MarkerEvent = events[i]; break; }
            Marker.gameObject.SetActive(MarkerEvent != null);
            if (MarkerEvent != null)
            {
                Marker.position = Map(MarkerEvent.State.Ball);
                Marker.GetComponent<Renderer>().sharedMaterial = ColorMaterial(MarkerEvent.Kind == "BallHit" ? "Hit" : MarkerEvent.Kind == "BallBounced" ? "Bounce" : "Net event", MarkerEvent.Kind == "BallHit" ? Color.red : MarkerEvent.Kind == "BallBounced" ? Color.cyan : Color.magenta);
            }
        }
        public void BuildStage()
        {
            if (stage != null) return;
            stage = new GameObject("ReplayStage"); stage.transform.SetParent(transform, false);
            float w = (float)CoordinateMapper.HalfWidth, l = (float)CoordinateMapper.HalfLength, s = (float)CoordinateMapper.ServiceLine;
            Primitive("Court", PrimitiveType.Cube, new Vector3(0, -.06f, 0), new Vector3(18, .1f, 34), new Color(.08f, .25f, .25f));
            // Engine dimensions are OUTSIDE edges; line thickness is drawn inward.
            foreach (float x in new[] { -w + .025f, w - .025f }) Primitive("Sideline", PrimitiveType.Cube, new Vector3(x, 0, 0), new Vector3(.05f, .012f, l * 2), Color.white);
            foreach (float z in new[] { -l + .025f, l - .025f, -s + .025f, s - .025f }) Primitive("BaselineOrService", PrimitiveType.Cube, new Vector3(0, 0, z), new Vector3(w * 2, .012f, .05f), Color.white);
            Primitive("CentreService", PrimitiveType.Cube, Vector3.zero, new Vector3(.05f, .012f, s * 2), Color.white);
            // Twenty strips follow the engine's piecewise-linear net-height profile.
            for (int i = 0; i < 20; i++)
            {
                float x = -5.029f + (i + .5f) * (10.058f / 20);
                float h = .914f + .156f * Mathf.Min(1, Mathf.Abs(x) / 5.029f);
                Primitive("Net", PrimitiveType.Cube, new Vector3(x, h / 2, 0), new Vector3(10.058f / 20, h, .025f), new Color(.55f, .59f, .62f));
            }
            PlayerA = Primitive("PlayerA", PrimitiveType.Capsule, Vector3.zero, new Vector3(.6f, PlayerHeight / 2, .6f), new Color(1, .45f, .12f));
            PlayerB = Primitive("PlayerB", PrimitiveType.Capsule, Vector3.zero, new Vector3(.6f, PlayerHeight / 2, .6f), new Color(.25f, .65f, 1));
            Ball = Primitive("Ball", PrimitiveType.Sphere, Vector3.zero, Vector3.one, Color.yellow);
            Marker = Primitive("EventMarker", PrimitiveType.Cube, Vector3.zero, Vector3.one * .12f, Color.red); Marker.gameObject.SetActive(false);
            var cameraObject = new GameObject("ReplayCamera"); cameraObject.transform.SetParent(stage.transform);
            var camera = cameraObject.AddComponent<Camera>(); camera.tag = "MainCamera";
            camera.transform.position = new Vector3(23, 28, -32); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 23; camera.nearClipPlane = .1f; camera.farClipPlane = 150;
            camera.backgroundColor = new Color(.025f, .045f, .065f); camera.clearFlags = CameraClearFlags.SolidColor;
            // Leave a HUD column without covering the court.
            camera.rect = new Rect(.30f, 0, .70f, 1);
            var lightObject = new GameObject("ReplayLight"); lightObject.transform.SetParent(stage.transform);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        }
        Transform Primitive(string label, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type); go.name = label; go.transform.SetParent(stage.transform); go.transform.position = position; go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>(); if (collider != null) { collider.enabled = false; Destroy(collider); }
            go.GetComponent<Renderer>().sharedMaterial = ColorMaterial(label, color); return go.transform;
        }
        Material ColorMaterial(string key, Color color)
        {
            if (palette.TryGetValue(key, out var material)) return material;
            var shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Standard shader missing. Prepare a Built-in 3D project.");
            material = new Material(shader) { color = color }; palette.Add(key, material); materials.Add(material); return material;
        }
        void OnDestroy() { foreach (var material in materials) if (material != null) Destroy(material); }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(8, 8, Mathf.Max(220, Screen.width * .30f - 16), Screen.height - 16), GUI.skin.box);
            hudScroll = GUILayout.BeginScrollView(hudScroll);
            GUILayout.Label("TennisSim • recorded replay");
            pathInput = GUILayout.TextField(pathInput ?? "");
            if (GUILayout.Button("Load JSON")) Load(pathInput);
            if (!string.IsNullOrEmpty(Error)) GUILayout.Label("LOAD ERROR: " + Error);
            if (Controller != null)
            {
                var c = Controller; var d = c.Timeline.Data; var state = c.State;
                GUILayout.Label(d.FileName + " | seed " + d.Seed);
                GUILayout.Label("A: " + d.PlayerIds[0] + " (orange)   B: " + d.PlayerIds[1] + " (blue)");
                GUILayout.Label("Point " + state.Point + " / " + c.Timeline.TotalPoints);
                GUILayout.Label(c.Time.ToString("F3") + " / " + c.Timeline.Duration.ToString("F3") + " s");
                GUILayout.Label("Score: " + state.Score.Display);
                GUILayout.Label("Phase: " + state.Phase);
                if (GUILayout.Button(c.Playing ? "Pause" : "Play")) c.SetPlaying(!c.Playing);
                if (GUILayout.Button("Restart")) c.Restart();
                GUILayout.BeginHorizontal(); foreach (double speed in new[] { .25, 1.0, 2.0 }) if (GUILayout.Button(speed + "x")) c.SetSpeed(speed); GUILayout.EndHorizontal();
                GUILayout.Label("Speed: " + c.Speed + "x");
                float seek = GUILayout.HorizontalSlider((float)c.Time, (float)c.Timeline.Start, (float)c.Timeline.Duration);
                if (seek != (float)c.Time) c.Seek(seek);
                GUILayout.BeginHorizontal(); if (GUILayout.Button("Previous event")) c.StepEvent(-1); if (GUILayout.Button("Next event")) c.StepEvent(1); GUILayout.EndHorizontal();
                int index = c.SelectedEvent >= 0 ? c.SelectedEvent : c.Timeline.LastEventIndex(c.Time);
                if (index >= 0) GUILayout.Label((c.SelectedEvent >= 0 ? "Selected: " : "Latest: ") + d.Events[index]);
                if (MarkerEvent != null) GUILayout.Label("Marker: " + MarkerEvent);
                GUILayout.Label("Markers: red hit / cyan bounce / magenta net");
                EnlargeBall = GUILayout.Toggle(EnlargeBall, "Ball 3x visual size (centre unchanged)");
                if (state.Score.Complete) GUILayout.Label("Winner: " + d.PlayerIds[state.Score.Winner] + " | " + state.Score.Display);
                if (c.Time == c.Timeline.Duration) GUILayout.Label("End: " + d.Status + " " + d.Diagnostic);
                GUILayout.Label("Recorded samples + linear interpolation. Realism uncalibrated.");
                Present();
            }
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
    }
}
