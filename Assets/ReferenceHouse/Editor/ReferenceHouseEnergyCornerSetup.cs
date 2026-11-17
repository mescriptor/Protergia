using System;
using System.IO;
using EnergyCorner;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace ReferenceHouseTools
{
    public static class ReferenceHouseEnergyCornerSetup
    {
        public const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string RootName = "Energy Corner | Inputs and state";

        [MenuItem("Reference House/Energy Corner/Apply integration")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before applying Energy Corner setup.");
            ApplyPlayerSettings();
            ReferenceHouseLedWallSetup.SaveDiagram();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("ENERGY_CORNER_SETUP_COMPLETE");
        }

        public static void ApplyPlayerSettings()
        {
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = ReferenceHouseLedOutput.OutputWidth;
            PlayerSettings.defaultScreenHeight = ReferenceHouseLedOutput.OutputHeight;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = true;
            PlayerSettings.usePlayerLog = true;
        }

        public static void ConfigureScene(ReferenceHouseLedWall wall, CanvasGroup interactive,
            ReferenceHouseDiagram diagram, Font bold, Font regular)
        {
            ApplyPlayerSettings();
            var root = GameObject.Find(RootName);
            if (!root)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Energy Corner controls");
            }
            var serial = EnsureComponent<EnergyCornerSerial>(root);
            var keyboard = EnsureComponent<KeyboardInputSource>(root);
            var controller = EnsureComponent<EnergyCornerController>(root);
            var debug = EnsureComponent<DebugOverlay>(root);
            EnsureComponent<KioskSetup>(root);
            var bridge = EnsureComponent<ReferenceHouseEnergyCornerBridge>(root);
            Undo.RecordObjects(new UnityEngine.Object[] { controller, debug, bridge }, "Connect Energy Corner");
            controller.serial = serial;
            controller.keyboard = keyboard;
            debug.controller = controller;
            debug.serial = serial;
            bridge.controller = controller;
            bridge.wall = wall;
            bridge.interactiveLayer = interactive;
            bridge.diagram = diagram;
            bridge.debugOverlay = debug;

            var layerRect = Box(wall.transform, "Attract videos", new Rect(0, 0,
                ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height));
            var background = layerRect.gameObject.AddComponent<Image>();
            background.color = new Color32(25, 6, 49, 255);
            background.raycastTarget = false;
            var group = layerRect.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.blocksRaycasts = true;

            var fallback = Box(layerRect, "Welcome", new Rect(200, 500, 1472, 460)).gameObject.AddComponent<Text>();
            fallback.font = bold;
            fallback.fontSize = 54;
            fallback.color = Color.white;
            fallback.alignment = TextAnchor.MiddleCenter;
            fallback.text = "Protergia\n\nΕνέργεια για το σπίτι σου";
            fallback.raycastTarget = false;
            var image = Box(layerRect, "Video image", new Rect(0, 0,
                ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height)).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var player = layerRect.gameObject.AddComponent<VideoPlayer>();
            var attract = layerRect.gameObject.AddComponent<AttractVideoPlayer>();
            attract.controller = controller;
            attract.player = player;
            attract.target = image;
            attract.layer = group;
            attract.noVideoLabel = fallback;
            bridge.video = attract;
            debug.video = attract;
            interactive.alpha = 0f;
            interactive.interactable = false;
            interactive.blocksRaycasts = false;
            EnsureInitialVideo();
        }

        static T EnsureComponent<T>(GameObject root) where T : Component =>
            root.TryGetComponent<T>(out var value) ? value : Undo.AddComponent<T>(root);

        static RectTransform Box(Transform parent, string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            return rect;
        }

        static void EnsureInitialVideo()
        {
            string folder = Path.Combine(Application.streamingAssetsPath, "Videos");
            Directory.CreateDirectory(folder);
            string source = Path.Combine(Application.dataPath, "ReferenceHouse/Video/Protergia Club - SKY express.mp4");
            string target = Path.Combine(folder, "01_Protergia_Club_SKY_express.mp4");
            if (!File.Exists(target) && File.Exists(source))
            {
                File.Copy(source, target);
                AssetDatabase.ImportAsset("Assets/StreamingAssets/Videos/01_Protergia_Club_SKY_express.mp4");
            }
        }

        [MenuItem("Reference House/Energy Corner/Build Windows player")]
        public static void BuildWindows()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building.");
            Apply();
            const string folder = "Builds/EnergyCorner";
            Directory.CreateDirectory(folder);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = folder + "/EnergyCorner.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Directory.CreateDirectory("output/energy-corner");
            File.WriteAllText("output/energy-corner/build.txt", report.summary.result + "\n" +
                report.summary.totalErrors + " errors\n" + report.summary.totalWarnings + " warnings\n" +
                report.summary.totalSize + " bytes\n" + DateTime.UtcNow.ToString("O"));
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Energy Corner Windows build failed: " + report.summary.result);
            const string handoff = "docs/EnergyCorner-Handoff.md";
            if (File.Exists(handoff)) File.Copy(handoff, folder + "/EnergyCorner-Handoff.md", true);
            const string quickstart = "docs/EnergyCorner-Quickstart.txt";
            if (File.Exists(quickstart)) File.Copy(quickstart, folder + "/START-HERE.txt", true);
            const string benchTool = "Tools/EnergyCorner/ec_serial_test.py";
            if (File.Exists(benchTool))
            {
                Directory.CreateDirectory(folder + "/Tools");
                File.Copy(benchTool, folder + "/Tools/ec_serial_test.py", true);
            }
            Debug.Log("ENERGY_CORNER_BUILD_COMPLETE " + Path.GetFullPath(folder));
        }
    }
}
