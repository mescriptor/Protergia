using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using EnergyCorner;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Hotspot = ReferenceHouseTools.ReferenceHouseDiagram.Hotspot;

namespace ReferenceHouseTools
{
    /// <summary>Runs real input bindings and the software protocol path against the saved kiosk scene.</summary>
    [InitializeOnLoad]
    public static class ReferenceHouseEnergyCornerVerification
    {
        const string ScenePath = "Assets/ReferenceHouse/Scenes/ReferenceHouse.unity";
        const string Output = "output/energy-corner";
        const string ReportPath = Output + "/verification.json";
        const string PendingKey = "ReferenceHouse.EnergyCorner.VerifyPending";
        const string StartedKey = "ReferenceHouse.EnergyCorner.VerifyStarted";
        const string ModeKey = "ReferenceHouse.EnergyCorner.VerifyMode";
        static bool running, finished;
        static VerificationReport report;

        // Deliberately independent of the production bridge mapping.
        static readonly Hotspot[] ExpectedProducts =
        {
            Hotspot.Meter, Hotspot.HeatPump, Hotspot.Charger, Hotspot.Photovoltaics, Hotspot.Battery
        };
        static readonly Key[] DigitKeys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
        static readonly Key[] NumpadKeys = { Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5 };

        static ReferenceHouseEnergyCornerVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Watchdog;
        }

        [MenuItem("Reference House/Energy Corner/Verify integration")]
        public static void StartVerification() => Start(false);

        [MenuItem("Reference House/Energy Corner/Verify live 16x9 integration")]
        public static void StartLiveVerification() => Start(true);

        static void Start(bool live)
        {
            if (running || SessionState.GetBool(PendingKey, false))
                throw new InvalidOperationException("Energy Corner verification is already running.");
            Directory.CreateDirectory(Output);
            finished = false;
            report = new VerificationReport
            {
                status = "running", startedAtUtc = DateTime.UtcNow.ToString("O"),
                mode = live ? "live" : "native", hardwareVerified = false,
                hardwareNote = "Software input and protocol parser only; physical Pico, USB transport and sensor calibration require on-site verification."
            };
            WriteReport();
            SessionState.SetString(StartedKey, report.startedAtUtc);
            SessionState.SetString(ModeKey, report.mode);
            SessionState.SetBool(PendingKey, true);
            try
            {
                if (!EditorApplication.isPlaying)
                {
                    if (SceneManager.GetActiveScene().path != ScenePath)
                    {
                        for (int i = 0; i < SceneManager.sceneCount; i++)
                            if (SceneManager.GetSceneAt(i).isDirty)
                                throw new InvalidOperationException("Save the open scene before verifying ReferenceHouse.");
                        EditorSceneManager.OpenScene(ScenePath);
                    }
                    if (live) ReferenceHouseLedWallSetup.PlayLive();
                    else
                    {
                        ReferenceHouseLedWallSetup.ConfigureGameView();
                        EditorApplication.isPlaying = true;
                    }
                }
                else
                {
                    if (live) ReferenceHouseLedWallSetup.PlayLive();
                    else ReferenceHouseLedWallSetup.ConfigureGameView();
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
                Finish(new InvalidOperationException("Play mode ended before verification completed."));
        }

        static void Begin()
        {
            if (!SessionState.GetBool(PendingKey, false) || running) return;
            SessionState.SetBool(PendingKey, false);
            try
            {
                if (!EditorApplication.isPlaying) throw new InvalidOperationException("Verification requires Play mode.");
                if (report == null && File.Exists(ReportPath))
                    report = JsonUtility.FromJson<VerificationReport>(File.ReadAllText(ReportPath));
                report = report ?? new VerificationReport { status = "running", startedAtUtc = DateTime.UtcNow.ToString("O") };
                report.mode = SessionState.GetString(ModeKey, "native");
                var bridge = UnityEngine.Object.FindFirstObjectByType<ReferenceHouseEnergyCornerBridge>();
                if (!bridge) throw new InvalidOperationException("The saved ReferenceHouse scene has no Energy Corner bridge. Apply the Energy Corner setup first.");
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
            // Awake/OnEnable/Start and the LED compositor must have completed before exercising inputs.
            for (int frame = 0; frame < 12; frame++) yield return null;
            var controller = bridge.controller;
            var wall = bridge.wall;
            var video = bridge.video;
            var overlay = bridge.debugOverlay;
            Require(controller && wall && video && bridge.interactiveLayer && overlay,
                "Saved scene references: controller, wall, video, interactive layer and diagnostics");
            var cfg = EnergyCornerConfig.Current;
            Require(cfg.buttonCount == 5 && cfg.keyboardInput,
                "On-site config enables all five products and keyboard input");
            Require(video.layer && video.target && video.player, "Native video components are assigned");
            Require(wall.detailPanels != null && wall.detailPanels.Length == 5, "Five product panels are assigned");
            report.screenWidth = Screen.width;
            report.screenHeight = Screen.height;
            report.playlistCount = video.PlaylistCount;
            int expectedWidth = report.mode == "live" ? ReferenceHouseLedOutput.OutputWidth : ReferenceHouseLedWall.Width;
            int expectedHeight = report.mode == "live" ? ReferenceHouseLedOutput.OutputHeight : ReferenceHouseLedWall.Height;
            Require(Screen.width == expectedWidth && Screen.height == expectedHeight,
                $"{report.mode} output is {expectedWidth}x{expectedHeight} (actual {Screen.width}x{Screen.height})");

            float originalIdle = cfg.idleTimeoutSec, originalPopup = cfg.popupTimeoutSec, originalFade = cfg.videoFadeSec;
            bool originalKeyboard = cfg.keyboardInput, originalOverlay = overlay.IsVisible;
            CornerState originalState = controller.State;
            int originalProduct = controller.CurrentProduct;
            bool originalSimulation = controller.IsSimulatingPresence, originalPresence = controller.Presence;
            bool originalSensor = controller.SensorPresence;
            var serial = controller.serial;
            bool serialWasEnabled = serial && serial.enabled;
            bool originalSerialPresence = serial && serial.Presence;
            bool originalSerialFault = serial && serial.SensorFault;
            var parse = typeof(EnergyCornerSerial).GetMethod("HandleLine", BindingFlags.Instance | BindingFlags.NonPublic);
            var previousKeyboard = Keyboard.current;
            var previousMouse = Mouse.current;
            Keyboard keyboard = null;
            Mouse mouse = null;
            int buttonEvents = 0;
            UnityAction<int> countButton = _ => buttonEvents++;
            try
            {
                if (serial) serial.enabled = false; // Do not mix a real installation into deterministic software tests.
                if (serial && parse != null) Parse(parse, serial, "OK SENSOR");
                cfg.keyboardInput = true;
                cfg.idleTimeoutSec = 60;
                cfg.popupTimeoutSec = 0;
                cfg.videoFadeSec = .03f;
                overlay.SetVisible(false);
                keyboard = InputSystem.AddDevice<Keyboard>();
                mouse = InputSystem.AddDevice<Mouse>();
                keyboard.MakeCurrent();
                mouse.MakeCurrent();
                controller.onButtonPressed.AddListener(countButton);

                controller.SetPresence(false);
                controller.SetSimulatedPresence(false);
                // Restart the state to exercise the same attract hooks as startup.
                controller.GoTo(CornerState.Interactive);
                controller.GoTo(CornerState.Attract);
                yield return new WaitForSecondsRealtime(.12f);
                Expect(controller, wall, CornerState.Attract, 0, "Attract closes every product card");
                Require(video.IsActive && video.layer.alpha > .98f && bridge.interactiveLayer.alpha < .02f,
                    "Attract displays the video layer and hides interactive UI");
                Require(video.player.targetTexture &&
                    video.player.targetTexture.width == ReferenceHouseLedWall.Width &&
                    video.player.targetTexture.height == ReferenceHouseLedWall.Height,
                    "Video is composed at native 1872x1560 before output stretching");
                if (video.PlaylistCount > 0)
                {
                    yield return WaitForVideo(video);
                    Require(video.HasVideo && video.target.enabled, "Bundled attract video decodes and displays a frame");
                }
                else Require(video.noVideoLabel && video.noVideoLabel.gameObject.activeInHierarchy && !video.target.enabled,
                    "An empty playlist displays the welcome fallback");
                string firstVideo = video.CurrentVideo;
                yield return Capture("Attract");
                yield return Gesture(mouse, ScreenPoint(Hotspot.Meter));
                Expect(controller, wall, CornerState.Attract, 0, "Pointer input cannot activate cards beneath the video");

                yield return Press(keyboard, Key.P);
                Require(controller.IsSimulatingPresence && controller.Presence,
                    "P selects simulated visitor presence");
                Expect(controller, wall, CornerState.Interactive, 0, "P wakes the house");
                yield return new WaitForSecondsRealtime(.12f);
                Require(!video.IsActive && video.layer.alpha < .02f && bridge.interactiveLayer.alpha > .98f,
                    "Presence fades out video and reveals interactive UI");

                for (int product = 1; product <= 5; product++)
                {
                    int before = buttonEvents;
                    yield return Press(keyboard, DigitKeys[product - 1], 4);
                    Require(buttonEvents == before + 1, $"Holding digit {product} emits one press");
                    Expect(controller, wall, CornerState.Product, product, $"Digit {product} maps to {ExpectedProducts[product - 1]}");
                    VerifyTextFits(wall.detailPanels[wall.SelectedIndex]);
                    yield return Capture($"Card_{product}_{ExpectedProducts[product - 1]}");
                    yield return Press(keyboard, DigitKeys[product - 1]);
                    Expect(controller, wall, CornerState.Interactive, 0, $"Digit {product} again closes the card");
                }
                for (int product = 1; product <= 5; product++)
                {
                    yield return Press(keyboard, NumpadKeys[product - 1]);
                    Expect(controller, wall, CornerState.Product, product, $"Numpad {product} maps to {ExpectedProducts[product - 1]}");
                }
                yield return Press(keyboard, Key.Escape);
                Expect(controller, wall, CornerState.Interactive, 0, "Escape closes the active card");

                yield return Gesture(mouse, ScreenPoint(Hotspot.Meter));
                Expect(controller, wall, CornerState.Product, 1, "Actual mouse binding opens meter through controller");
                yield return Gesture(mouse, ScreenPoint(Hotspot.Photovoltaics));
                Expect(controller, wall, CornerState.Product, 4, "Mouse switches product through controller");
                wall.Activate(Hotspot.HeatPump);
                Expect(controller, wall, CornerState.Product, 2, "Public wall activation uses the same controller");
                wall.CloseDetails();
                Expect(controller, wall, CornerState.Interactive, 0, "Public close updates controller and presentation");

                yield return Press(keyboard, Key.F1);
                Require(overlay.IsVisible && overlay.BlocksInteraction, "F1 opens maintenance diagnostics");
                yield return Gesture(mouse, ScreenPoint(Hotspot.Meter));
                Expect(controller, wall, CornerState.Interactive, 0, "Maintenance overlay prevents underlying hotspot clicks");
                yield return Press(keyboard, Key.F1);
                Require(!overlay.IsVisible && !overlay.BlocksInteraction, "F1 closes maintenance diagnostics");

                // Cache changes even when manual simulation suppresses their effect.
                controller.SetPresence(false);
                yield return Press(keyboard, Key.S);
                Require(controller.IsSimulatingPresence && !controller.Presence, "S simulates visitor departure");
                controller.SetPresence(true);
                Require(!controller.Presence, "Sensor change cannot overwrite explicit simulation");
                yield return Press(keyboard, Key.F2);
                Require(!controller.IsSimulatingPresence && controller.Presence,
                    "F2 restores the latest sensor state without requiring a new edge");
                controller.SetSimulatedPresence(true);
                controller.SetPresence(false);
                controller.UseSensorPresence();
                Require(!controller.IsSimulatingPresence && !controller.Presence,
                    "Returning to Auto also restores a cached absent sensor state");

                if (!serial) throw new InvalidOperationException("Serial component is missing from the saved scene.");
                if (parse == null) throw new MissingMethodException("EnergyCornerSerial.HandleLine");
                Parse(parse, serial, "S");
                Parse(parse, serial, "P");
                Require(controller.Presence, "Serial P parser reaches the common presence controller");
                Parse(parse, serial, "STATE S");
                Require(!controller.Presence, "Serial STATE S heartbeat restores absence");
                Parse(parse, serial, "STATE P");
                Require(controller.Presence, "Serial STATE P heartbeat restores presence");
                controller.CloseProduct();
                for (int product = 1; product <= 5; product++)
                {
                    Parse(parse, serial, "B" + product);
                    Expect(controller, wall, CornerState.Product, product, $"Serial B{product} maps to {ExpectedProducts[product - 1]}");
                }
                Parse(parse, serial, "B5");
                Expect(controller, wall, CornerState.Interactive, 0, "Repeated serial button closes its card");
                int beforeInvalid = buttonEvents;
                controller.HandleButton(6);
                Expect(controller, wall, CornerState.Interactive, 0, "Unmapped product 6 is ignored");
                Parse(parse, serial, "B6");
                Expect(controller, wall, CornerState.Interactive, 0, "Unmapped serial B6 is ignored");
                Require(buttonEvents == beforeInvalid, "Unmapped product inputs do not emit accepted button activity");
                report.serialParserVerified = true;

                // Popup timeout is independent of presence; idle timeout is not.
                controller.SetSimulatedPresence(true);
                cfg.popupTimeoutSec = .12f;
                controller.HandleButton(3);
                yield return WaitForState(controller, CornerState.Interactive, 3f);
                Expect(controller, wall, CornerState.Interactive, 0, "Popup expires while the visitor remains present");
                cfg.popupTimeoutSec = 0;
                cfg.idleTimeoutSec = 2;
                controller.HandleButton(1);
                controller.SetSimulatedPresence(false);
                yield return new WaitForSecondsRealtime(.2f);
                controller.HandleButton(2);
                Require(controller.IdleRemaining > 1.7f, "A button press restarts the absent visitor idle timer");
                controller.SetSimulatedPresence(true);
                Expect(controller, wall, CornerState.Product, 2, "Visitor return retains the current product");
                Require(controller.IdleRemaining < 0, "Presence suspends the idle countdown");

                cfg.idleTimeoutSec = .12f;
                controller.SetSimulatedPresence(false);
                yield return WaitForState(controller, CornerState.Attract, 3f);
                yield return new WaitForSecondsRealtime(.12f);
                Expect(controller, wall, CornerState.Attract, 0, "Visitor departure and idle timeout return to Attract");
                Require(video.IsActive && video.layer.alpha > .98f, "Idle timeout resumes the video layer");
                if (video.PlaylistCount > 1)
                    Require(video.CurrentVideo != firstVideo, "Returning to Attract advances to the next playlist video");
                else if (video.PlaylistCount == 1)
                    Require(video.CurrentVideo == firstVideo, "A one-video playlist resumes its looping video");
                cfg.idleTimeoutSec = 60;
                yield return VerifyPlaylist(controller, video, cfg);
                if (video.PlaylistCount > 0) yield return WaitForVideo(video);
                Require(video.PlaylistCount == report.playlistCount && video.HasVideo && video.target.enabled,
                    "Original playlist is restored and decodes after fixture checks");
                yield return Capture("Attract_restored");
                yield return Press(keyboard, Key.Digit4);
                Expect(controller, wall, CornerState.Product, 4, "A product key wakes Attract directly into its card");
                report.videoStatus = video.Status;
                report.keyboardBindingsVerified = true;
                report.pointerBindingsVerified = true;
            }
            finally
            {
                cfg.idleTimeoutSec = originalIdle;
                cfg.popupTimeoutSec = originalPopup;
                cfg.videoFadeSec = originalFade;
                cfg.keyboardInput = originalKeyboard;
                if (controller) controller.onButtonPressed.RemoveListener(countButton);
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                if (serial && parse != null)
                {
                    Parse(parse, serial, "OK SENSOR");
                    Parse(parse, serial, originalSerialPresence ? "STATE P" : "STATE S");
                    if (originalSerialFault) Parse(parse, serial, "ERR SENSOR");
                }
                if (controller)
                {
                    controller.SetPresence(originalSensor);
                    if (originalSimulation) controller.SetSimulatedPresence(originalPresence);
                    else controller.UseSensorPresence();
                    controller.GoTo(originalState, originalProduct);
                }
                if (overlay) overlay.SetVisible(originalOverlay);
                if (serial && serialWasEnabled) serial.enabled = true;
            }
        }

        static void Parse(MethodInfo parse, EnergyCornerSerial serial, string line)
        {
            try { parse.Invoke(serial, new object[] { line }); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }

        static IEnumerator VerifyPlaylist(EnergyCornerController controller, AttractVideoPlayer video, EnergyCornerConfig cfg)
        {
            var filesField = typeof(AttractVideoPlayer).GetField("files", BindingFlags.Instance | BindingFlags.NonPublic);
            var indexField = typeof(AttractVideoPlayer).GetField("index", BindingFlags.Instance | BindingFlags.NonPublic);
            if (filesField == null || indexField == null)
                throw new MissingFieldException("AttractVideoPlayer playlist fixture requires files/index.");
            var files = filesField.GetValue(video) as List<string>;
            if (files == null) throw new InvalidOperationException("The video playlist is unavailable.");
            var originalFiles = new List<string>(files);
            int originalIndex = (int)indexField.GetValue(video);
            CornerState originalState = controller.State;
            int originalProduct = controller.CurrentProduct;
            bool originalSimulation = controller.IsSimulatingPresence, originalPresence = controller.Presence;
            float originalIdle = cfg.idleTimeoutSec, originalPopup = cfg.popupTimeoutSec, originalFade = cfg.videoFadeSec;
            string source = video.player.url;
            if (Uri.TryCreate(source, UriKind.Absolute, out var sourceUri) && sourceUri.IsFile) source = sourceUri.LocalPath;
            source = Path.GetFullPath(source);
            if (!File.Exists(source)) throw new FileNotFoundException("Playlist verification requires the bundled playable clip.", source);

            string tempRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tmp"));
            string fixtureRoot = Path.GetFullPath(Path.Combine(tempRoot, "energy-corner-video-" + Guid.NewGuid().ToString("N")));
            if (!fixtureRoot.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Playlist fixture escaped the project temporary directory.");
            string copiedClip = Path.Combine(fixtureRoot, "02_valid_copy" + Path.GetExtension(source));
            string missingA = Path.Combine(fixtureRoot, "01_missing_a.mp4");
            string missingB = Path.Combine(fixtureRoot, "02_missing_b.mp4");
            try
            {
                Directory.CreateDirectory(fixtureRoot);
                File.Copy(source, copiedClip);
                cfg.idleTimeoutSec = 60;
                cfg.popupTimeoutSec = 0;
                cfg.videoFadeSec = .03f;
                controller.SetSimulatedPresence(false);

                SetFixturePlaylist(controller, video, files, indexField, source, copiedClip);
                yield return WaitForVideo(video);
                Require(video.HasVideo && video.target.enabled && video.CurrentVideo == Path.GetFileName(source),
                    "Two-clip playlist starts by decoding the first valid clip");
                controller.GoTo(CornerState.Interactive);
                yield return new WaitForSecondsRealtime(.1f);
                controller.GoTo(CornerState.Attract);
                yield return WaitForVideo(video);
                Require(video.HasVideo && video.target.enabled && video.CurrentVideo == Path.GetFileName(copiedClip),
                    "Leaving and returning to Attract decodes the next valid playlist clip");

                SetFixturePlaylist(controller, video, files, indexField, missingA, copiedClip);
                yield return WaitForVideo(video);
                Require(video.HasVideo && video.target.enabled && video.CurrentVideo == Path.GetFileName(copiedClip),
                    "A real decoder error on the first clip skips to a playable second clip");

                SetFixturePlaylist(controller, video, files, indexField, missingA, missingB);
                float deadline = Time.realtimeSinceStartup + 16f;
                while (video.IsActive && !video.Status.StartsWith("videos unavailable", StringComparison.Ordinal) &&
                    Time.realtimeSinceStartup < deadline) yield return null;
                Require(video.IsActive && !video.HasVideo && !video.target.enabled && video.layer.alpha > .98f &&
                    video.noVideoLabel && video.noVideoLabel.gameObject.activeInHierarchy &&
                    video.Status.StartsWith("videos unavailable", StringComparison.Ordinal),
                    "All invalid clips reach a visible welcome fallback with the video image disabled");
                yield return Capture("Attract_fallback");
                report.playlistBehaviorVerified = true;
            }
            finally
            {
                // Stop the fixture decoder before restoring the actual playlist and releasing the copied file.
                controller.GoTo(CornerState.Interactive);
                video.player.Stop();
                files.Clear();
                files.AddRange(originalFiles);
                indexField.SetValue(video, originalIndex);
                cfg.idleTimeoutSec = originalIdle;
                cfg.popupTimeoutSec = originalPopup;
                cfg.videoFadeSec = originalFade;
                if (originalSimulation) controller.SetSimulatedPresence(originalPresence);
                else controller.UseSensorPresence();
                controller.GoTo(originalState, originalProduct);
                string checkedFile = Path.GetFullPath(copiedClip);
                if (!checkedFile.StartsWith(fixtureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to clean up a file outside the playlist fixture.");
                if (File.Exists(checkedFile)) File.Delete(checkedFile);
                if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, false);
            }
        }

        static void SetFixturePlaylist(EnergyCornerController controller, AttractVideoPlayer video,
            List<string> files, FieldInfo indexField, params string[] clips)
        {
            controller.GoTo(CornerState.Interactive);
            files.Clear();
            files.AddRange(clips);
            indexField.SetValue(video, 0);
            controller.GoTo(CornerState.Attract);
        }

        static IEnumerator Press(Keyboard keyboard, Key key, int holdFrames = 1)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            for (int frame = 0; frame < holdFrames; frame++) yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }

        static IEnumerator Gesture(Mouse mouse, Vector2 position)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
        }

        static Vector2 ScreenPoint(Hotspot id)
        {
            foreach (var label in ReferenceHouseDiagram.Labels)
                if (label.Id == id)
                {
                    var point = label.HitBounds.center;
                    return new Vector2(point.x * Screen.width / ReferenceHouseLedWall.Width,
                        Screen.height - point.y * Screen.height / ReferenceHouseLedWall.Height);
                }
            throw new InvalidOperationException("Missing hotspot " + id);
        }

        static IEnumerator WaitForState(EnergyCornerController controller, CornerState expected, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (controller.State != expected && Time.realtimeSinceStartup < deadline) yield return null;
            if (controller.State != expected)
                throw new TimeoutException($"Expected {expected}; controller stayed in {controller.State}.");
        }

        static IEnumerator WaitForVideo(AttractVideoPlayer video)
        {
            float deadline = Time.realtimeSinceStartup + 16f;
            while (!video.HasVideo && video.IsActive && Time.realtimeSinceStartup < deadline) yield return null;
            if (!video.HasVideo)
                throw new TimeoutException("Attract video did not display a frame: " + video.Status);
        }

        static void Expect(EnergyCornerController controller, ReferenceHouseLedWall wall, CornerState state, int product, string check)
        {
            if (controller.State != state || controller.CurrentProduct != product)
                throw new InvalidOperationException($"{check}: state was {controller.State}/{controller.CurrentProduct}, expected {state}/{product}.");
            int expectedIndex = -1;
            if (product > 0)
                for (int i = 0; i < ReferenceHouseDiagram.Labels.Count; i++)
                    if (ReferenceHouseDiagram.Labels[i].Id == ExpectedProducts[product - 1]) expectedIndex = i;
            if (wall.SelectedIndex != expectedIndex)
                throw new InvalidOperationException($"{check}: panel index {wall.SelectedIndex}, expected {expectedIndex}.");
            for (int i = 0; i < wall.detailPanels.Length; i++)
                if (!wall.detailPanels[i] || wall.detailPanels[i].activeInHierarchy != (i == expectedIndex))
                    throw new InvalidOperationException($"{check}: unexpected visibility for panel {i}.");
            Record(check);
        }

        static void VerifyTextFits(GameObject panel)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var text in panel.GetComponentsInChildren<Text>())
            {
                if (text.verticalOverflow == VerticalWrapMode.Overflow) continue;
                if (text.preferredHeight > text.rectTransform.rect.height + 1)
                    throw new InvalidOperationException($"Card text '{text.name}' is clipped: {text.preferredHeight} > {text.rectTransform.rect.height}.");
            }
            Record("Card text fits: " + panel.name);
        }

        static IEnumerator Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            // WaitForEndOfFrame is not serviced by editor batch mode. Two stable rendered frames work in both modes.
            yield return null;
            yield return null;
            var frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (!frame) throw new InvalidOperationException("Could not capture " + name);
            try
            {
                string path = Output + "/" + name + "_" + frame.width + "x" + frame.height + ".png";
                File.WriteAllBytes(path, frame.EncodeToPNG());
                report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.Destroy(frame); }
        }

        static void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException(check);
            Record(check);
        }

        static void Record(string check)
        {
            report.checks.Add(check);
            WriteReport();
        }

        static void Watchdog()
        {
            if ((!running && !SessionState.GetBool(PendingKey, false)) || finished) return;
            if (DateTime.TryParse(SessionState.GetString(StartedKey, ""), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var start) && (DateTime.UtcNow - start).TotalSeconds > 180)
                Finish(new TimeoutException("Energy Corner verification exceeded 180 seconds. Check the editor log for import, play-mode or rendering failures."));
        }

        static void Finish(Exception error)
        {
            if (finished) return;
            finished = true;
            running = false;
            SessionState.SetBool(PendingKey, false);
            report = report ?? new VerificationReport();
            report.status = error == null ? "passed" : "failed";
            report.finishedAtUtc = DateTime.UtcNow.ToString("O");
            report.error = error?.ToString() ?? "";
            WriteReport();
            if (error == null) Debug.Log("ENERGY_CORNER_VERIFIED " + report.checks.Count + " checks; hardware verification remains on site.");
            else Debug.LogError("ENERGY_CORNER_VERIFICATION_FAILED " + error);
            if (Application.isBatchMode) EditorApplication.delayCall += () => EditorApplication.Exit(error == null ? 0 : 1);
        }

        static void WriteReport()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        sealed class VerificationReport
        {
            public string status, startedAtUtc, finishedAtUtc, mode, error, hardwareNote, videoStatus;
            public int screenWidth, screenHeight, playlistCount;
            public bool keyboardBindingsVerified, pointerBindingsVerified, serialParserVerified, playlistBehaviorVerified, hardwareVerified;
            public List<string> checks = new List<string>();
            public List<string> screenshots = new List<string>();
        }
    }
}
