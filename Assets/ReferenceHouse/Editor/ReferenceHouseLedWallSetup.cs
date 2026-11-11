using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ReferenceHouseTools
{
    [InitializeOnLoad]
    public static class ReferenceHouseLedWallSetup
    {
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string CanvasName = "LED Wall | Canvas 1872x1560";
        const string Output = "output/led-wall";
        const string CapturePending = "ReferenceHouse.LED.CapturePending";
        static readonly Color Purple = new Color32(83, 0, 156, 255);
        static readonly Color Lavender = new Color32(224, 178, 255, 255);
        static readonly Color Orange = new Color32(255, 165, 0, 255);
        static double nextPoll;
        static Font font;
        static Sprite rounded;

        static ReferenceHouseLedWallSetup()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Path.Combine(Output, "request.txt");
            if (!File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            try
            {
                if (command == "setup") SetupAndPlay();
                else if (command == "capture") Capture();
                else if (command == "stop") EditorApplication.isPlaying = false;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Output, "error.txt"), ex.ToString());
                Debug.LogException(ex);
            }
        }

        [MenuItem("Reference House/LED Wall/Set up 1872x1560 and Play")]
        public static void SetupAndPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before setting up the LED wall.");
            Directory.CreateDirectory(Output);
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Save the open scene before switching to ReferenceHouse.");
                EditorSceneManager.OpenScene(ScenePath);
            }
            // Keep a recoverable copy before adding the persistent UI objects.
            string backup = Path.Combine(Output, "ReferenceHouse_before_LED.unity");
            if (!File.Exists(backup)) File.Copy(ScenePath, backup);
            var old = GameObject.Find(CanvasName);
            if (old) Undo.DestroyObjectImmediate(old);
            var camera = Camera.main;
            if (!camera) throw new InvalidOperationException("Reference house camera is missing.");
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            var root = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(ReferenceHouseLedWall));
            Undo.RegisterCreatedObjectUndo(root, "Add LED wall test canvas");
            root.layer = 5;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1872, 1560);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            var controller = root.GetComponent<ReferenceHouseLedWall>();
            controller.sceneCamera = camera;
            controller.canvas = canvas;

            var header = Panel(root.transform, "Title", new Vector2(64, 1496), new Vector2(760, 76), Purple, new Vector2(0, 1));
            Text(header, "Title text", "Ενεργειακή τομή κατοικίας", 40, Color.white, new Vector2(30, -12), new Vector2(700, 52));
            var resolution = Panel(root.transform, "Resolution badge", new Vector2(1808, 1496), new Vector2(395, 66), Purple, Vector2.one);
            Text(resolution, "Resolution text", "LED WALL · 1872 × 1560", 26, Color.white, new Vector2(24, -15), new Vector2(350, 36));

            controller.connector = Panel(root.transform, "Object connector", Vector2.zero, new Vector2(100, 4), Orange, new Vector2(0, .5f), false);
            controller.marker = Panel(root.transform, "Object marker", Vector2.zero, new Vector2(16, 16), Orange, new Vector2(.5f, .5f));
            controller.label = Panel(root.transform, "Photovoltaic label", Vector2.zero, new Vector2(348, 70), Lavender, new Vector2(.5f, .5f));
            Text(controller.label, "Object text", "Φωτοβολταϊκά", 36, Purple, new Vector2(22, -12), new Vector2(304, 48));
            var target = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.gameObject.scene == root.scene && r.name.StartsWith("PV module 05") && r.name.Contains("cells"))
                .OrderBy(r => r.name).FirstOrDefault();
            if (!target)
                target = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                    .FirstOrDefault(r => r.gameObject.scene == root.scene && r.name.StartsWith("PV module 05"));
            if (!target) throw new InvalidOperationException("Could not find the photovoltaic module to label.");
            controller.target = target.transform;
            controller.targetLocalPoint = target.transform.InverseTransformPoint(target.bounds.center);

            int[] sizes = {24, 36, 48};
            for (int i = 0; i < sizes.Length; i++)
            {
                var panel = Panel(root.transform, "Panel " + (i + 1) + " | " + sizes[i] + " px", new Vector2(64 + i * 592, 48), new Vector2(560, 230), Purple, Vector2.zero);
                var strip = Panel(panel, "Orange accent", new Vector2(24, 212), new Vector2(64, 5), Orange, Vector2.zero, false);
                Text(panel, "Size label", "0" + (i + 1) + "  /  " + sizes[i] + " px", 26, Lavender, new Vector2(24, -32), new Vector2(512, 36));
                var sample = Text(panel, "Sample text", "Ενέργεια στο σπίτι\nAa Αα 0123456789", sizes[i], Color.white, new Vector2(24, -88), new Vector2(512, 130));
                sample.lineSpacing = 1.08f;
            }

            PlayerSettings.defaultScreenWidth = 1872;
            PlayerSettings.defaultScreenHeight = 1560;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            ConfigureGameView();
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root;
            File.WriteAllText(Path.Combine(Output, "setup.txt"), "Scene saved. Canvas: 1872x1560. Samples: 24, 36, 48 px. Target: " + target.name);
            SessionState.SetBool(CapturePending, true);
            EditorApplication.isPlaying = true;
        }

        static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Color color, Vector2 pivot, bool round = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            if (round && rounded) { image.sprite = rounded; image.type = Image.Type.Sliced; }
            return rect;
        }

        static Text Text(Transform parent, string name, string value, int size, Color color, Vector2 position, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.layer = 5;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
            rect.sizeDelta = box;
            var text = go.GetComponent<Text>();
            text.font = font; text.text = value; text.fontSize = size; text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        [MenuItem("Reference House/LED Wall/Use 1872x1560 Game view")]
        public static void ConfigureGameView()
        {
            // Unity does not expose custom Game view sizes as a public editor API.
            var assembly = typeof(Editor).Assembly;
            var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var group = sizesType.GetMethod("GetGroup").Invoke(sizes, new object[] {GameViewSizeGroupType.Standalone});
            var groupType = group.GetType();
            int count = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null), index = -1;
            for (int i = 0; i < count; i++)
            {
                var size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] {i});
                var type = size.GetType();
                if ((int)type.GetProperty("width").GetValue(size) == 1872 && (int)type.GetProperty("height").GetValue(size) == 1560 && type.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var sizeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize", true), Enum.Parse(sizeType, "FixedResolution"), 1872, 1560, "LED Wall");
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] {size});
                index = count;
                sizesType.GetMethod("SaveToHDD").Invoke(sizes, null);
            }
            var gameViewType = assembly.GetType("UnityEditor.GameView", true);
            var view = EditorWindow.GetWindow(gameViewType);
            gameViewType.GetMethod("SizeSelectionCallback").Invoke(view, new object[] {index, null});
            gameViewType.GetProperty("lowResolutionForAspectRatios")?.SetValue(view, false);
            var gizmos = gameViewType.GetField("m_Gizmos", BindingFlags.Instance | BindingFlags.NonPublic);
            gizmos?.SetValue(view, false);
            view.Show(); view.Focus(); view.Repaint();
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(CapturePending, false)) return;
            SessionState.SetBool(CapturePending, false);
            EditorApplication.delayCall += Capture;
        }

        [MenuItem("Reference House/LED Wall/Capture house and panels")]
        public static void Capture()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play before capturing.");
            ConfigureGameView();
            var controller = UnityEngine.Object.FindFirstObjectByType<ReferenceHouseLedWall>();
            if (!controller) throw new InvalidOperationException("LED wall Canvas is missing.");
            controller.CapturePair(Path.GetFullPath(Output));
        }
    }
}
