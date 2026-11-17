using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EnergyCorner
{
    /// <summary>
    /// F1 toggles a maintenance overlay (IMGUI, so it needs no prefab or EventSystem):
    /// state, presence, live sensor values, a 10 s graph of the readings, serial status,
    /// recent messages, plus Calibrate and +/- controls for the presence parameters.
    /// While it is open the device's per-measurement debug stream (DEBUG 1) is on,
    /// and the mouse cursor is shown.
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        public EnergyCornerController controller;
        public EnergyCornerSerial serial;
        public AttractVideoPlayer video;
        public bool IsVisible => visible;
        public bool BlocksInteraction => isActiveAndEnabled && visible;

        const float GraphSeconds = 10f;
        const float GraphMaxCm = 360f;     // bottom of the graph (sensor range is 3-350 cm)

        struct Sample
        {
            public float time;
            public int raw;        // -1 = no echo
            public int filtered;   // -1 = no valid reading in the window
            public bool person;    // raw reading counts as a person (same rule as the firmware)
        }

        readonly Queue<Sample> samples = new Queue<Sample>();

        bool visible;
        bool confirmCalibrate;
        GUIStyle label, plain, small, header, box, button;
        Texture2D white;

        // Graph colours
        static readonly Color ColBg = new Color(0.08f, 0.08f, 0.1f, 1f);
        static readonly Color ColGrid = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color ColZone = new Color(0.2f, 0.9f, 0.3f, 0.13f);
        static readonly Color ColBaseline = new Color(1f, 0.85f, 0.1f, 0.9f);
        static readonly Color ColEnter = new Color(1f, 0.5f, 0.1f, 0.9f);
        static readonly Color ColRaw = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color ColPerson = new Color(0.25f, 1f, 0.35f, 1f);
        static readonly Color ColFiltered = new Color(0.2f, 0.8f, 1f, 1f);
        static readonly Color ColNoEcho = new Color(1f, 0.25f, 0.25f, 0.8f);

        void Awake()
        {
            visible = EnergyCornerConfig.Current.debugOverlay;
        }

        void OnEnable()
        {
            SetVisible(visible);
            if (serial != null)
            {
                serial.OnConnectionChanged += OnConnection;
                serial.OnDebug += OnDebug;
            }
        }

        void OnDisable()
        {
            if (serial != null)
            {
                if (visible && serial.IsConnected) serial.SetDebugStream(false);
                serial.OnConnectionChanged -= OnConnection;
                serial.OnDebug -= OnDebug;
            }
            if (!Application.isEditor) Cursor.visible = !EnergyCornerConfig.Current.hideCursor;
        }

        void OnDestroy()
        {
            if (white) Destroy(white);
            if (box?.normal.background) Destroy(box.normal.background);
        }

        void OnConnection(bool connected)
        {
            if (connected && visible) serial.SetDebugStream(true);
        }

        void OnDebug(int raw, int filtered, int rate, int hits, int win, int baseline)
        {
            samples.Enqueue(new Sample
            {
                time = Time.unscaledTime,
                raw = raw,
                filtered = filtered,
                person = CountsAsPerson(raw, baseline)
            });
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) SetVisible(!visible);

            float oldest = Time.unscaledTime - GraphSeconds;
            while (samples.Count > 0 && samples.Peek().time < oldest) samples.Dequeue();
        }

        public void SetVisible(bool v)
        {
            visible = v;
            confirmCalibrate = false;
            if (!Application.isEditor) Cursor.visible = v || !EnergyCornerConfig.Current.hideCursor;
            if (serial != null && serial.IsConnected)
            {
                serial.SetDebugStream(v);
                if (v) serial.RequestSettings();
            }
        }

        // ======================================================================
        // Drawing
        // ======================================================================

        void InitStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true };
            label.normal.textColor = Color.white;
            plain = new GUIStyle(label) { richText = false, fontSize = 19 };
            small = new GUIStyle(label) { fontSize = 16 };
            header = new GUIStyle(label) { fontSize = 28, fontStyle = FontStyle.Bold };
            white = MakeTexture(Color.white);
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = MakeTexture(new Color(0f, 0f, 0f, 0.85f));
            button = new GUIStyle(GUI.skin.button) { fontSize = 22 };
        }

        static Texture2D MakeTexture(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (!visible) return;
            InitStyles();

            // Lay out in "design pixels" 1080 high and scale to the real screen height.
            var previousMatrix = GUI.matrix;
            float scale = Mathf.Min(Screen.height / 1080f, Screen.width / 1400f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUILayout.BeginArea(new Rect(30, 20, 1340, 1040), box);
            GUILayout.Label("ENERGY CORNER  -  debug (F1 to close)", header);

            DrawState();
            DrawSerial();
            DrawGraph();
            DrawControls();
            DrawRecent();

            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        void DrawState()
        {
            if (controller == null) return;
            string idle = controller.IdleRemaining >= 0f ? controller.IdleRemaining.ToString("0.0") + " s" : "-";
            GUILayout.Label("State: <b>" + controller.StateName + "</b>     Presence: " +
                            (controller.Presence ? "<color=lime>YES</color>" : "<color=orange>no</color>") +
                            "     Back to attract in: " + idle, label);
            GUILayout.Label("Presence source: <b>" + (controller.IsSimulatingPresence ?
                            "SIMULATION (P/S)" : "SENSOR (auto)") + "</b>     Sensor cached: " +
                            (controller.SensorPresence ? "present" : "absent") +
                            "     F2 = return to sensor", label);
            GUILayout.Label("Keys: 1.." + EnergyCornerConfig.Current.buttonCount +
                            " / numpad = product, Esc = close card, P/S = simulate presence", plain);
            if (video) GUILayout.Label("Video: " + video.Status + "   " + video.CurrentVideo, plain);
        }

        void DrawSerial()
        {
            if (serial == null) { GUILayout.Label("Serial: (no component)", label); return; }

            string conn = serial.IsConnected ? "<color=lime>connected</color>" : "<color=red>not connected</color>";
            float age = serial.LastLineTime < 0 ? -1 : Time.unscaledTime - serial.LastLineTime;
            GUILayout.Label("Serial: " + conn + "  port " + serial.PortName + "  (" + serial.Status + ")" +
                            (age >= 0 ? "   last rx " + age.ToString("0.0") + " s ago" : ""), label);

            string sensor = !serial.IsConnected ? "unknown (no link)" :
                serial.SensorFault ? "<color=red>FAULT (SIG stuck)</color>" : "<color=lime>OK</color>";
            string baseline = serial.Baseline > 0 ? serial.Baseline + " cm"
                : serial.Baseline == 0 ? "0 (empty = no echo)" : "-";
            GUILayout.Label("Sensor: " + sensor +
                            "   raw <b>" + Cm(serial.LastRaw) + "</b>   filtered <b>" + Cm(serial.LastFiltered) + "</b>" +
                            "   echo rate " + (serial.LastValidRate >= 0 ? serial.LastValidRate + " %" : "-") +
                            "   hits " + (serial.LastHits >= 0 ? serial.LastHits + "/" + serial.LastWin : "-") +
                            "   baseline " + baseline +
                            (Setting("baseRate") >= 0 ? " (echo rate at CAL " + Setting("baseRate") + " %)" : ""), label);
        }

        void DrawGraph()
        {
            Rect r = GUILayoutUtility.GetRect(1280, 330, GUILayout.ExpandWidth(false));
            if (Event.current.type != EventType.Repaint) return;

            const float axisW = 70f;
            var g = new Rect(r.x + axisW, r.y + 8, r.width - axisW - 10, r.height - 34);
            Fill(g, ColBg);

            // Distance grid every 50 cm (0 = sensor at the top, far = bottom)
            for (int cm = 0; cm <= 350; cm += 50)
            {
                float y = Y(g, cm);
                Fill(new Rect(g.x, y, g.width, 1), ColGrid);
                GUI.Label(new Rect(r.x, y - 11, axisW - 6, 22), cm + " cm", small);
            }

            // Person zone and thresholds
            int minCm = Setting("minCm"), maxCm = Setting("maxCm"), threshold = Setting("threshold");
            int baselineCm = serial != null ? serial.Baseline : -1;
            if (minCm > 0 && maxCm > 0)
            {
                float top = minCm;
                float bottom = maxCm;
                if (baselineCm > 0 && threshold > 0) bottom = Mathf.Min(bottom, baselineCm - threshold);
                if (bottom > top) Fill(Rect.MinMaxRect(g.x, Y(g, top), g.xMax, Y(g, bottom)), ColZone);
            }
            if (baselineCm > 0)
            {
                Fill(new Rect(g.x, Y(g, baselineCm) - 1, g.width, 2), ColBaseline);
                if (threshold > 0) Fill(new Rect(g.x, Y(g, baselineCm - threshold) - 1, g.width, 2), ColEnter);
            }

            // Samples
            float now = Time.unscaledTime;
            foreach (var s in samples)
            {
                float x = g.x + (s.time - (now - GraphSeconds)) / GraphSeconds * g.width;
                if (s.filtered >= 0) Fill(new Rect(x - 2, Y(g, s.filtered) - 2, 4, 4), ColFiltered);
                if (s.raw >= 0) Fill(new Rect(x - 4, Y(g, s.raw) - 4, 8, 8), s.person ? ColPerson : ColRaw);
                else Fill(new Rect(x - 1, g.yMax - 10, 2, 10), ColNoEcho);   // gap + tick: no echo
            }

            GUI.Label(new Rect(g.x, g.yMax + 2, g.width, 24),
                "last 10 s   <color=#ffffff>o raw</color>  <color=#40ff60>o person</color>  " +
                "<color=#33ccff>. filtered</color>  <color=#ffd81a>- baseline</color>  " +
                "<color=#ff8019>- baseline-threshold</color>  <color=#40ff60>green band = person zone</color>  " +
                "<color=#ff4040>| no echo</color>" + (samples.Count == 0 ? "   (no D lines: connected?)" : ""), small);
        }

        static float Y(Rect g, float cm) => g.y + Mathf.Clamp(cm, 0f, GraphMaxCm) / GraphMaxCm * g.height;

        void Fill(Rect rect, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(rect, white);
            GUI.color = old;
        }

        void DrawControls()
        {
            GUI.enabled = controller != null;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sensor / Auto (F2)", button, GUILayout.Height(40))) controller.UseSensorPresence();
            if (GUILayout.Button("Simulate present (P)", button, GUILayout.Height(40))) controller.SetSimulatedPresence(true);
            if (GUILayout.Button("Simulate absent (S)", button, GUILayout.Height(40))) controller.SetSimulatedPresence(false);
            GUILayout.EndHorizontal();

            bool canSend = serial != null && serial.IsConnected;
            GUI.enabled = canSend;

            GUILayout.BeginHorizontal();
            if (!confirmCalibrate)
            {
                if (GUILayout.Button("Calibrate (space must be EMPTY)", button, GUILayout.Height(44)))
                    confirmCalibrate = true;
            }
            else
            {
                if (GUILayout.Button("Yes, nobody is there: CAL", button, GUILayout.Height(44)))
                {
                    serial.Calibrate();
                    confirmCalibrate = false;
                }
                if (GUILayout.Button("Cancel", button, GUILayout.Height(44))) confirmCalibrate = false;
            }
            if (GUILayout.Button("Refresh (GET)", button, GUILayout.Height(44))) serial.RequestSettings();
            if (EnergyCornerConfig.Current.enableHardwareOutputs)
            {
                if (GUILayout.Button("LEDs on", button, GUILayout.Height(44))) serial.SetAllLeds(true);
                if (GUILayout.Button("LEDs off", button, GUILayout.Height(44))) serial.SetAllLeds(false);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ParamControl("threshold", 5, " cm");
            ParamControl("minCm", 5, " cm");
            ParamControl("maxCm", 10, " cm");
            ParamControl("hitsNeeded", 1, "/" + Setting("win"));
            GUILayout.EndHorizontal();

            GUI.enabled = true;
            GUILayout.Label("Also via SET: win " + Setting("win") + ", hitsRelease " + Setting("hitsRelease") +
                            ", enterMs " + Setting("enterMs") + ", exitMs " + Setting("exitMs") +
                            ", pingMs " + Setting("pingMs"), small);
        }

        /// <summary>"name: value [-] [+]". The device rejects out-of-range values (ERR SET).</summary>
        void ParamControl(string key, int step, string unit)
        {
            int value = Setting(key);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && value >= 0;
            GUILayout.Label(key + ": <b>" + (value >= 0 ? value + unit : "-") + "</b>", label, GUILayout.Width(190));
            if (GUILayout.Button("-", button, GUILayout.Width(50), GUILayout.Height(40))) SetAndRefresh(key, value - step);
            if (GUILayout.Button("+", button, GUILayout.Width(50), GUILayout.Height(40))) SetAndRefresh(key, value + step);
            GUILayout.Space(24);
            GUI.enabled = wasEnabled;
        }

        void DrawRecent()
        {
            GUILayout.Label("Recent messages  (< from device, > to device, newest first)", plain);
            if (serial == null) return;
            foreach (var line in serial.RecentMessages.Reverse().Take(9))
                GUILayout.Label(line, plain);
        }

        // ======================================================================

        void SetAndRefresh(string key, int value)
        {
            serial.SetParameter(key, value);
            serial.RequestSettings();
        }

        int Setting(string key)
        {
            if (serial != null && serial.DeviceSettings.TryGetValue(key, out var s) && int.TryParse(s, out int v))
                return v;
            return -1;
        }

        /// <summary>Same rule as countsAsPerson() in the firmware, for colouring the graph.</summary>
        bool CountsAsPerson(int d, int baseline)
        {
            int minCm = Setting("minCm"), maxCm = Setting("maxCm"), threshold = Setting("threshold");
            if (d < 0 || minCm < 0 || maxCm < 0) return false;
            if (d < minCm || d > maxCm) return false;
            if (baseline > 0 && threshold >= 0 && d >= baseline - threshold) return false;
            return true;
        }

        static string Cm(int v) => v >= 0 ? v + " cm" : "no echo";
    }
}
