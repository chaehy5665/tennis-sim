using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TennisSim.Coach.Editor
{
    public static class CoachSceneSetup
    {
        public const string ScenePath = "Assets/TennisSim/Scenes/Coach.unity";
        [MenuItem("TennisSim/Create or open coach scene")]
        public static void Setup()
        {
            if (!File.Exists("Assets/TennisSim/Coach/Plugins/TennisSim.Core.dll"))
                throw new FileNotFoundException("Run scripts/sync-core-to-unity.sh first; the coach UI runs actual Core.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
            else
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // UI Toolkit draws the whole screen; a camera only clears to the ground colour behind it.
                var camera = new GameObject("CoachCamera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(6 / 255f, 11 / 255f, 17 / 255f);
                new GameObject("Coach").AddComponent<CoachApp>();
                Directory.CreateDirectory("Assets/TennisSim/Scenes");
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save coach scene");
            }
            if (UnityEngine.Object.FindObjectsOfType<CoachApp>().Length != 1) throw new InvalidOperationException("Scene must contain exactly one CoachApp");
            // Keep the replay scene if it is registered; the coach scene goes first so a Player build opens it.
            var others = EditorBuildSettings.scenes.Where(s => s.path != ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }.Concat(others).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("TENNISSIM_COACH_SCENE_READY editor=" + Application.unityVersion + " scene=" + ScenePath);
        }
    }
}
