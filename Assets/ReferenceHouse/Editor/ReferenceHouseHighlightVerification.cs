using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EnergyCorner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    /// <summary>Checks rendered equipment highlights in Editor Play mode; never builds a player.</summary>
    [InitializeOnLoad]
    public static class ReferenceHouseHighlightVerification
    {
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string Output = "output/energy-corner";
        const string ReportPath = Output + "/highlight-verification.json";
        const string PendingKey = "ReferenceHouse.Highlight.VerifyPending";
        const string StartedKey = "ReferenceHouse.Highlight.VerifyStarted";
        static bool running, finished;
        static Report report;

        static readonly Hotspot[] Products =
        {
            Hotspot.Meter, Hotspot.HeatPump, Hotspot.Charger, Hotspot.Photovoltaics, Hotspot.Battery
        };
        static readonly Key[] Keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };

        static ReferenceHouseHighlightVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Watchdog;
        }

        [MenuItem("Reference House/Energy Corner/Verify equipment highlights")]
        public static void StartVerification()
        {
            if (running || SessionState.GetBool(PendingKey, false))
                throw new InvalidOperationException("Equipment highlight verification is already running.");
            Directory.CreateDirectory(Output);
            finished = false;
            report = new Report { status = "running", startedAtUtc = DateTime.UtcNow.ToString("O") };
            WriteReport();
            SessionState.SetString(StartedKey, report.startedAtUtc);
            SessionState.SetBool(PendingKey, true);
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    if (SceneManager.GetActiveScene().path != ScenePath)
                    {
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                            if (SceneManager.GetSceneAt(i).isDirty)
                                throw new InvalidOperationException("Save open scenes before verifying equipment highlights.");
                        EditorSceneManager.OpenScene(ScenePath);
                    }
                    ReferenceHouseLedWallSetup.ConfigureGameView();
                    EditorApplication.isPlaying = true;
                }
                else
                {
                    ReferenceHouseLedWallSetup.ConfigureGameView();
                    EditorApplication.delayCall += Begin;
                }
            }
            catch (Exception exception) { Finish(exception); }
        }

        static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                EditorApplication.delayCall += Begin;
            else if (state == PlayModeStateChange.ExitingPlayMode && running && !finished)
                Finish(new InvalidOperationException("Play mode ended during equipment highlight verification."));
        }

        static void Begin()
        {
            if (!SessionState.GetBool(PendingKey, false) || running) return;
            SessionState.SetBool(PendingKey, false);
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Highlight checks require Editor Play mode.");
                if (report == null && File.Exists(ReportPath)) report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
                report = report ?? new Report { status = "running", startedAtUtc = DateTime.UtcNow.ToString("O") };
                var bridge = UnityEngine.Object.FindFirstObjectByType<ReferenceHouseEnergyCornerBridge>();
                if (!bridge) throw new InvalidOperationException("Energy Corner bridge is missing from the saved scene.");
                running = true;
                bridge.StartCoroutine(SafeRun(Verify(bridge)));
            }
            catch (Exception exception) { Finish(exception); }
        }

        static IEnumerator SafeRun(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            Exception error = null;
            while (stack.Count > 0)
            {
                object current = null;
                bool moved = false;
                try
                {
                    moved = stack.Peek().MoveNext();
                    if (moved) current = stack.Peek().Current;
                }
                catch (Exception exception) { error = exception; }
                if (error != null) break;
                if (!moved)
                {
                    (stack.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (current is IEnumerator nested) stack.Push(nested);
                else yield return current;
            }
            while (stack.Count > 0)
            {
                try { (stack.Pop() as IDisposable)?.Dispose(); }
                catch (Exception exception) { if (error == null) error = exception; }
            }
            Finish(error);
        }

        static IEnumerator Verify(ReferenceHouseEnergyCornerBridge bridge)
        {
            for (int i = 0; i < 12; i++) yield return null;
            var controller = bridge.controller;
            var wall = bridge.wall;
            var diagram = bridge.diagram;
            Require(controller && wall && diagram, "Saved bridge has controller, cards and diagram references");
            Require(Screen.width == ReferenceHouseLedWall.Width && Screen.height == ReferenceHouseLedWall.Height,
                "Highlight captures use native 1872x1560 wall pixels");
            report.screenWidth = Screen.width;
            report.screenHeight = Screen.height;
            foreach (var product in Products) Require(diagram.HasObjectOutline(product), "Projected outline exists for " + product);

            var cfg = EnergyCornerConfig.Current;
            float originalIdle = cfg.idleTimeoutSec, originalPopup = cfg.popupTimeoutSec, originalFade = cfg.videoFadeSec;
            bool originalKeyboard = cfg.keyboardInput;
            var originalState = controller.State;
            int originalProduct = controller.CurrentProduct;
            bool originalSimulation = controller.IsSimulatingPresence, originalPresence = controller.Presence;
            bool originalSensor = controller.SensorPresence;
            bool originalOverlay = bridge.debugOverlay && bridge.debugOverlay.IsVisible;
            bool serialWasEnabled = controller.serial && controller.serial.enabled;
            var previousKeyboard = Keyboard.current;
            Keyboard keyboard = null;
            Color32[] baseline = null;
            try
            {
                if (controller.serial) controller.serial.enabled = false;
                if (bridge.debugOverlay) bridge.debugOverlay.SetVisible(false);
                cfg.keyboardInput = true;
                cfg.idleTimeoutSec = 60;
                cfg.popupTimeoutSec = 0;
                cfg.videoFadeSec = .03f;
                keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                controller.SetSimulatedPresence(false);
                controller.GoTo(CornerState.Attract);
                yield return Press(keyboard, Key.P);
                yield return new WaitForSecondsRealtime(.15f);
                Expect(bridge, null, "Presence reveals the house with no equipment highlighted");
                yield return Capture("Highlight_baseline", frame => baseline = frame.GetPixels32());
                Require(baseline != null, "Unselected baseline frame is available");

                for (int i = 0; i < Products.Length; i++)
                {
                    int productNumber = i + 1;
                    var expected = Products[i];
                    yield return Press(keyboard, Keys[i]);
                    Expect(bridge, expected, "Key " + productNumber + " selects only " + expected);
                    var blocked = VisibleCardBounds(wall);
                    yield return Capture("Highlight_" + productNumber + "_" + expected, frame =>
                    {
                        var pixels = frame.GetPixels32();
                        int added = CountAddedAmber(diagram, expected, baseline, pixels, blocked);
                        Require(added >= 6, expected + " gains visible amber outline pixels (" + added + ")");
                        report.pixels.Add(new PixelResult { product = expected.ToString(), addedAmberPixels = added });
                        if (expected != Hotspot.Meter)
                        {
                            int staleMeter = CountAddedAmber(diagram, Hotspot.Meter, baseline, pixels, blocked);
                            Require(staleMeter <= 3, "Meter outline is cleared while " + expected + " is selected (" + staleMeter + " extra amber pixels)");
                        }
                    });
                }

                yield return Press(keyboard, Key.Digit5);
                Expect(bridge, null, "Pressing the same button closes its card and clears the object highlight");
                yield return Capture("Highlight_closed", frame =>
                {
                    var pixels = frame.GetPixels32();
                    foreach (var product in Products)
                    {
                        int added = CountAddedAmber(diagram, product, baseline, pixels, null);
                        Require(added <= 3, "Closed cards leave " + product + " unhighlighted (" + added + " extra amber pixels)");
                    }
                });

                yield return Press(keyboard, Key.Digit1);
                yield return Press(keyboard, Key.Escape);
                Expect(bridge, null, "Escape clears the active object highlight");
                yield return Press(keyboard, Key.Digit3);
                controller.GoTo(CornerState.Attract);
                Expect(bridge, null, "Returning to videos clears every object highlight");
                report.keyboardBindingsVerified = true;
                report.renderedHighlightsVerified = true;
            }
            finally
            {
                cfg.idleTimeoutSec = originalIdle;
                cfg.popupTimeoutSec = originalPopup;
                cfg.videoFadeSec = originalFade;
                cfg.keyboardInput = originalKeyboard;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (controller)
                {
                    controller.SetPresence(originalSensor);
                    if (originalSimulation) controller.SetSimulatedPresence(originalPresence);
                    else controller.UseSensorPresence();
                    controller.GoTo(originalState, originalProduct);
                    if (controller.serial && serialWasEnabled) controller.serial.enabled = true;
                }
                if (bridge.debugOverlay) bridge.debugOverlay.SetVisible(originalOverlay);
            }
        }

        static void Expect(ReferenceHouseEnergyCornerBridge bridge, Hotspot? expected, string message)
        {
            int index = -1;
            if (expected.HasValue)
                for (int i = 0; i < ReferenceHouseDiagram.Labels.Count; i++)
                    if (ReferenceHouseDiagram.Labels[i].Id == expected.Value) index = i;
            Require(bridge.wall.SelectedIndex == index && bridge.diagram.SelectedIndex == index &&
                bridge.diagram.HighlightedObject == expected, message);
        }

        static int CountAddedAmber(ReferenceHouseDiagram diagram, Hotspot product, Color32[] baseline,
            Color32[] current, Rect? blocked)
        {
            var samples = new HashSet<int>();
            foreach (var shape in diagram.equipmentOutlines)
            {
                if (shape == null || shape.product != product || shape.points == null || shape.points.Length < 3) continue;
                for (int segment = 0; segment < shape.points.Length; segment++)
                {
                    Vector2 a = shape.points[segment], b = shape.points[(segment + 1) % shape.points.Length];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                    for (int i = 0; i <= steps; i++)
                    {
                        var point = Vector2.Lerp(a, b, i / (float)steps);
                        for (int dy = -4; dy <= 4; dy++)
                            for (int dx = -4; dx <= 4; dx++)
                            {
                                int x = Mathf.RoundToInt(point.x) + dx, topY = Mathf.RoundToInt(point.y) + dy;
                                if (x < 0 || topY < 0 || x >= Screen.width || topY >= Screen.height) continue;
                                var native = new Vector2(x, topY);
                                if (blocked.HasValue && blocked.Value.Contains(native)) continue;
                                bool label = false;
                                foreach (var item in ReferenceHouseDiagram.Labels)
                                {
                                    // HitBounds may deliberately include the equipment itself (the meter
                                    // has a wide left hit area). Exclude the visible pill, not its click area.
                                    var avoid = item.TextBounds;
                                    avoid.xMin -= 14; avoid.xMax += 14; avoid.yMin -= 14; avoid.yMax += 14;
                                    if (avoid.Contains(native)) { label = true; break; }
                                }
                                if (!label) samples.Add((Screen.height - 1 - topY) * Screen.width + x);
                            }
                    }
                }
            }
            int added = 0;
            foreach (int index in samples)
            {
                var old = baseline[index];
                var value = current[index];
                if (value.r >= 210 && value.g >= 85 && value.g <= 245 && value.r - value.b >= 70 &&
                    (value.r > old.r + 12 || value.g > old.g + 12)) added++;
            }
            return added;
        }

        static Rect? VisibleCardBounds(ReferenceHouseLedWall wall)
        {
            if (wall.SelectedIndex < 0) return null;
            var rect = (RectTransform)wall.detailPanels[wall.SelectedIndex].transform;
            var canvas = wall.GetComponent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(min.x - 10, Screen.height - max.y - 10, max.x + 10, Screen.height - min.y + 10);
        }

        static IEnumerator Press(Keyboard keyboard, Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        static IEnumerator Capture(string name, Action<Texture2D> inspect)
        {
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return null;
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            if (!image) throw new InvalidOperationException("Could not capture " + name);
            try
            {
                string path = Output + "/" + name + "_" + image.width + "x" + image.height + ".png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                report.screenshots.Add(path);
                inspect?.Invoke(image);
            }
            finally { UnityEngine.Object.Destroy(image); }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            report.checks.Add(message);
            WriteReport();
        }

        static void Watchdog()
        {
            if ((!running && !SessionState.GetBool(PendingKey, false)) || finished) return;
            if (DateTime.TryParse(SessionState.GetString(StartedKey, ""), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var start) && (DateTime.UtcNow - start).TotalSeconds > 150)
                Finish(new TimeoutException("Equipment highlight verification exceeded 150 seconds."));
        }

        static void Finish(Exception error)
        {
            if (finished) return;
            finished = true;
            running = false;
            SessionState.SetBool(PendingKey, false);
            report = report ?? new Report();
            report.status = error == null ? "passed" : "failed";
            report.error = error?.ToString() ?? "";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            WriteReport();
            if (error == null) Debug.Log("EQUIPMENT_HIGHLIGHTS_VERIFIED " + report.checks.Count + " checks (Editor Play mode only).");
            else Debug.LogError("EQUIPMENT_HIGHLIGHTS_FAILED " + error);
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(error == null ? 0 : 1);
        }

        static void WriteReport()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        [Serializable] sealed class Report
        {
            public string status, startedAtUtc, finishedAtUtc, error;
            public int screenWidth, screenHeight;
            public bool keyboardBindingsVerified, renderedHighlightsVerified;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
            public List<PixelResult> pixels = new List<PixelResult>();
        }

        [Serializable] sealed class PixelResult
        {
            public string product;
            public int addedAmberPixels;
        }
    }
}
