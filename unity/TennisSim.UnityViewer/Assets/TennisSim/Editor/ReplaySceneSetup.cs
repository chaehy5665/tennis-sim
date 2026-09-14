using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace TennisSim.Viewer.Editor
{
    public static class ReplaySceneSetup
    {
        public const string ScenePath = "Assets/TennisSim/Scenes/Replay.unity";
        [MenuItem("TennisSim/Create or open replay scene")]
        public static void Setup()
        {
            if (GraphicsSettings.currentRenderPipeline != null) throw new InvalidOperationException("Use a Built-in 3D project; this source scaffold has not been verified with SRP.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
            else
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                new GameObject("ReplayViewer").AddComponent<ReplayPresenter>();
                Directory.CreateDirectory("Assets/TennisSim/Scenes");
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Could not save replay scene");
            }
            var viewers = UnityEngine.Object.FindObjectsOfType<ReplayPresenter>();
            if (viewers.Length != 1) throw new InvalidOperationException("Scene must contain exactly one ReplayPresenter");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("TENNISSIM_SCENE_READY editor=" + Application.unityVersion + " scene=" + ScenePath);
        }
    }
}
