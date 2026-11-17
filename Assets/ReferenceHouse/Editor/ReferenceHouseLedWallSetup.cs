using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    [InitializeOnLoad]
    public static class ReferenceHouseLedWallSetup
    {
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string CanvasName = "LED Wall | Canvas 1872x1560";
        const string Output = "output/led-wall";
        const string UI = "Assets/ReferenceHouse/UI/";
        const string CapturePending = "ReferenceHouse.LED.CapturePending";
        static double nextPoll;

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
                else if (command == "play-live") PlayLive();
                else if (command == "capture") Capture();
                else if (command == "review-live") ReviewLive();
                else if (command == "energy-setup") ReferenceHouseEnergyCornerSetup.Apply();
                else if (command == "energy-verify") ReferenceHouseEnergyCornerVerification.StartVerification();
                else if (command == "energy-build") ReferenceHouseEnergyCornerSetup.BuildWindows();
                else if (command == "highlight-setup") ReferenceHouseObjectHighlightSetup.Apply();
                else if (command == "highlight-verify") ReferenceHouseHighlightVerification.StartVerification();
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
            SaveDiagram();
            ConfigureGameView();
            SessionState.SetBool(CapturePending, true);
            EditorApplication.isPlaying = true;
        }

        public static void SaveDiagram()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play before rebuilding the diagram.");
            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                    throw new InvalidOperationException("Save the open scene before switching to ReferenceHouse.");
                EditorSceneManager.OpenScene(ScenePath);
            }

            var cardTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(UI + "MeterCard.png");
            var bold = AssetDatabase.LoadAssetAtPath<Font>(UI + "SourceSans3-Bold.otf");
            var regular = AssetDatabase.LoadAssetAtPath<Font>(UI + "SourceSans3-Regular.otf");
            if (!cardTexture || !regular || !bold) throw new InvalidOperationException("Missing LED artwork or fonts.");
            var camera = Camera.main;
            if (!camera) throw new InvalidOperationException("Reference house camera is missing.");
            var root = GameObject.Find(CanvasName);
            if (!root)
            {
                root = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                Undo.RegisterCreatedObjectUndo(root, "Create energy diagram");
            }
            for (int i = root.transform.childCount - 1; i >= 0; --i)
                Undo.DestroyObjectImmediate(root.transform.GetChild(i).gameObject);
            root.layer = 5;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvas.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>();
            // The output camera always draws the UI into a native 1872 x 1560 frame.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            if (!root.TryGetComponent<ReferenceHouseHotspotInput>(out _))
                Undo.AddComponent<ReferenceHouseHotspotInput>(root);
            if (!root.TryGetComponent<ReferenceHouseLedWall>(out var wall))
                wall = Undo.AddComponent<ReferenceHouseLedWall>(root);
            Undo.RecordObject(wall, "Assign energy cards");
            var interactive = Box(root.transform, "Interactive house", new Rect(0, 0, ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height));
            var interactiveGroup = interactive.gameObject.AddComponent<CanvasGroup>();
            var diagram = Box(interactive, "Energy paths and buttons", new Rect(0, 0, ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height));
            var diagramGraphic = diagram.gameObject.AddComponent<ReferenceHouseDiagram>();
            diagramGraphic.raycastTarget = false;
            ReferenceHouseObjectHighlightSetup.Populate(diagramGraphic, camera);
            PutText(diagram, "Title", ReferenceHouseDiagram.Title, ReferenceHouseDiagram.TitleTextBounds,
                bold, 20, Color.white, TextAnchor.MiddleCenter);

            var labels = ReferenceHouseDiagram.Labels;
            foreach (var label in labels)
                PutText(diagram, label.Id.ToString(), label.Text, label.TextBounds, bold, 20,
                    ReferenceHouseDiagram.InkColor, TextAnchor.MiddleLeft);

            wall.detailPanels = new GameObject[labels.Count];
            for (int i = 0; i < labels.Count; ++i)
            {
                var label = labels[i];
                RectTransform panel;
                if (label.Id == Hotspot.Meter)
                {
                    panel = Box(interactive, "Μετρητής | Smart Meters", ReferenceHouseDiagram.MeterCardBounds);
                    var image = panel.gameObject.AddComponent<RawImage>();
                    image.texture = cardTexture;
                    image.raycastTarget = false;
                }
                else
                {
                    panel = ReferenceHouseProductCards.Build(interactive, label, bold, regular);
                }
                panel.gameObject.SetActive(false);
                wall.detailPanels[i] = panel.gameObject;
            }

            if (!camera.TryGetComponent<ReferenceHouseLedOutput>(out var output))
                output = Undo.AddComponent<ReferenceHouseLedOutput>(camera.gameObject);
            Undo.RecordObject(output, "Assign LED wall canvas");
            output.wallCanvas = canvas;
            ReferenceHouseEnergyCornerSetup.ConfigureScene(wall, interactiveGroup,
                diagram.GetComponent<ReferenceHouseDiagram>(), bold, regular);
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (!EditorSceneManager.SaveScene(root.scene)) throw new IOException("Could not save the reference house.");
            Selection.activeGameObject = root;
            Debug.Log("ENERGY_DIAGRAM_SAVED");
        }

        static RectTransform Box(Transform parent, string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
            return rect;
        }

        static void PutText(Transform parent, string name, string value, Rect area, Font font, float size, Color ink, TextAnchor alignment)
        {
            var rect = Box(parent, name, area);
            // Legacy Text has integer sizes. 35 at half scale gives exactly 17.5 native pixels.
            float density = size % 1 == 0 ? 1 : 2;
            rect.localScale = Vector3.one / density;
            rect.sizeDelta *= density;
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = Mathf.RoundToInt(size * density);
            text.text = value;
            text.alignment = alignment;
            text.color = ink;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }

        [MenuItem("Reference House/LED Wall/Use 1872x1560 Game view")]
        public static void ConfigureGameView()
        {
            SelectGameViewSize(ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height, "LED Wall");
        }

        [MenuItem("Reference House/LED Wall/Play live 16x9 (1920x1080)")]
        public static void PlayLive()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open the ReferenceHouse scene to play its LED output.");
            SelectGameViewSize(ReferenceHouseLedOutput.OutputWidth, ReferenceHouseLedOutput.OutputHeight, "LED Wall Live");
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = true;
        }

        static void SelectGameViewSize(int width, int height, string title)
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
                if ((int)type.GetProperty("width").GetValue(size) == width && (int)type.GetProperty("height").GetValue(size) == height && type.GetProperty("sizeType").GetValue(size).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                var sizeType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var size = Activator.CreateInstance(assembly.GetType("UnityEditor.GameViewSize", true), Enum.Parse(sizeType, "FixedResolution"), width, height, title);
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

        [MenuItem("Reference House/LED Wall/Capture house and meter")]
        public static void Capture()
        {
            ConfigureGameView();
            StartReview(true);
        }

        [MenuItem("Reference House/LED Wall/Verify live 16x9 buttons")]
        public static void ReviewLive()
        {
            SelectGameViewSize(ReferenceHouseLedOutput.OutputWidth, ReferenceHouseLedOutput.OutputHeight, "LED Wall Live");
            StartReview(false);
        }

        static void StartReview(bool native)
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play before capturing.");
            var wall = UnityEngine.Object.FindFirstObjectByType<ReferenceHouseLedWall>();
            if (!wall) throw new InvalidOperationException("LED wall Canvas is missing.");
            wall.StartCoroutine(CaptureFrames(wall, native));
        }

        static IEnumerator CaptureFrames(ReferenceHouseLedWall wall, bool native)
        {
            Directory.CreateDirectory(Output);
            var controller = UnityEngine.Object.FindFirstObjectByType<EnergyCorner.EnergyCornerController>();
            var previousState = controller ? controller.State : EnergyCorner.CornerState.Interactive;
            int previousProduct = controller ? controller.CurrentProduct : 0;
            bool wasSimulating = controller && controller.IsSimulatingPresence;
            bool previousPresence = controller && controller.Presence;
            int previous = wall.SelectedIndex;
            try
            {
                if (controller) { controller.SetSimulatedPresence(true); controller.CloseProduct(); }
                for (int i = 0; i < 12; ++i) yield return null;
                int w = native ? ReferenceHouseLedWall.Width : ReferenceHouseLedOutput.OutputWidth;
                int h = native ? ReferenceHouseLedWall.Height : ReferenceHouseLedOutput.OutputHeight;
                if (Screen.width != w || Screen.height != h) throw new InvalidOperationException($"Unexpected Game view: {Screen.width}x{Screen.height}.");
                yield return ReferenceHouseHotspotReview.Verify(wall);
                wall.CloseDetails();
                if (native)
                {
                    yield return new WaitForEndOfFrame();
                    SaveFrame("House_Energy_1872x1560.png");
                }
                wall.ActivateMeter();
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return new WaitForEndOfFrame();
                var panel = (RectTransform)wall.detailPanels[wall.SelectedIndex].transform;
                var canvas = wall.GetComponent<Canvas>();
                var corners = new Vector3[4];
                panel.GetWorldCorners(corners);
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                var max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                var expected = new Vector2(356, 672);
                if (Vector2.Distance(max - min, expected) > .1f || Mathf.Abs(canvas.scaleFactor - 1f) > .001f)
                    throw new InvalidOperationException($"Meter artwork scaled to {max - min} at canvas scale {canvas.scaleFactor}.");
                SaveFrame(native ? "House_Meter_1872x1560.png" : "House_Meter_1920x1080.png");
                string report = JsonUtility.ToJson(new ScaleReport {
                    screenWidth = w, screenHeight = h, canvasScale = canvas.scaleFactor, nativeCardPixels = max - min,
                    boldPixels = 20, regularPixels = 17.5f, clickVerificationPassed = true
                }, true);
                File.WriteAllText(Path.Combine(Output, native ? "meter-native.json" : "meter-live.json"), report);
                Debug.Log("METER_CAPTURE_COMPLETE " + w + "x" + h);
            }
            finally
            {
                wall.CloseDetails();
                if (previous >= 0) wall.ActivateHotspot(previous);
                if (controller)
                {
                    if (wasSimulating) controller.SetSimulatedPresence(previousPresence);
                    else controller.UseSensorPresence();
                    controller.GoTo(previousState, previousProduct);
                }
            }
        }

        [Serializable] class ScaleReport
        {
            public int screenWidth, screenHeight;
            public float canvasScale, boldPixels, regularPixels;
            public Vector2 nativeCardPixels;
            public bool clickVerificationPassed;
        }

        static void SaveFrame(string name)
        {
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                // Check the rendered frame too: an active UGUI object can still have no visible geometry.
                var title = ReferenceHouseDiagram.TitleTextBounds;
                var firstLabel = ReferenceHouseDiagram.Labels[0].TextBounds;
                VerifyColor(image, title.xMin, title.center.y, ReferenceHouseDiagram.TitleColor);
                VerifyColor(image, firstLabel.xMin - 4, firstLabel.center.y, ReferenceHouseDiagram.LabelColor);
                File.WriteAllBytes(Path.Combine(Output, name), image.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(image); }
        }

        static void VerifyColor(Texture2D frame, float nativeX, float nativeY, Color32 expected)
        {
            int x = Mathf.RoundToInt(nativeX * frame.width / ReferenceHouseLedWall.Width);
            int y = frame.height - 1 - Mathf.RoundToInt(nativeY * frame.height / ReferenceHouseLedWall.Height);
            Color32 actual = frame.GetPixel(x, y);
            if (Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Abs(actual.g - expected.g),
                Mathf.Abs(actual.b - expected.b)) > 8)
                throw new InvalidOperationException($"LED artwork at {x},{y} rendered {actual} instead of {expected}.");
        }
    }
}
