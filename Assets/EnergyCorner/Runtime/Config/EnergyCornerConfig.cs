using System;
using System.IO;
using UnityEngine;

namespace EnergyCorner
{
    /// <summary>
    /// Settings loaded from StreamingAssets/config.json at startup.
    /// Every field has a default, so a missing key (or a missing file) is fine.
    /// Edit the JSON on site and restart the app; no rebuild needed.
    /// See README.md "config.json reference".
    /// </summary>
    [Serializable]
    public class EnergyCornerConfig
    {
        // --- Serial link ---
        public string comPort = "auto";        // "auto", "none" (keyboard only) or e.g. "COM5"
        public int baudRate = 115200;

        // --- Buttons / products ---
        public int buttonCount = 5;
        public int physicalButtonCount = 4;
        public string[] productNames = { "Smart Meter", "Heat Pump", "EV Charger", "PV Panels", "Battery" };

        // --- State machine ---
        public float idleTimeoutSec = 15f;     // no presence AND no press for this long -> Attract
        public float popupTimeoutSec = 30f;    // Product -> Interactive after this long (0 = never)
        public bool buttonWakesFromAttract = true; // a press in Attract opens that product directly

        // --- Outputs ---
        // The installation powers button LEDs independently; spare outputs are not wired.
        public bool enableHardwareOutputs = false;
        public LedConfig leds = new LedConfig();
        public FloorOutputConfig floorOutput = new FloorOutputConfig();

        // --- Video ---
        public string videoFolder = "Videos";  // relative to StreamingAssets, or an absolute path
        public float videoFadeSec = 0.6f;
        public bool videoAudio = false;
        public int videoWidth = 1872;
        public int videoHeight = 1560;

        // --- Kiosk / debug ---
        public bool debugOverlay = false;      // overlay visible at startup (F1 toggles)
        public bool keyboardInput = true;      // P / S / 1..N dev keys
        // Output resolution. 0 / 0 (default) = leave it alone: fullscreen at the display's
        // own resolution, as set in Windows. Fill in only to force a specific mode.
        public int screenWidth = 0;
        public int screenHeight = 0;
        public bool hideCursor = true;
        public int logMaxKB = 5120;            // rotate the log file at this size
        public int logFilesToKeep = 5;

        public string ProductName(int n)
        {
            if (productNames != null && n >= 1 && n <= productNames.Length) return productNames[n - 1];
            return "Product " + n;
        }

        // ------------------------------------------------------------------
        static EnergyCornerConfig current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => current = null;

        /// <summary>The loaded config (loaded on first access).</summary>
        public static EnergyCornerConfig Current
        {
            get
            {
                if (current == null) current = Load();
                return current;
            }
        }

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, "config.json");

        static EnergyCornerConfig Load()
        {
            var cfg = new EnergyCornerConfig();
            try
            {
                if (File.Exists(FilePath))
                {
                    // FromJsonOverwrite keeps the defaults for keys that are not in the file.
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), cfg);
                    Debug.Log("[EC] Config loaded from " + FilePath);
                }
                else
                {
                    Debug.LogWarning("[EC] No config.json at " + FilePath + ", using defaults.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[EC] config.json could not be read, using defaults: " + e.Message);
                cfg = new EnergyCornerConfig();
            }

            // The integrated house has exactly five mapped product cards.
            cfg.buttonCount = Mathf.Clamp(cfg.buttonCount, 1, 5);
            cfg.physicalButtonCount = Mathf.Clamp(cfg.physicalButtonCount, 0, cfg.buttonCount);
            cfg.baudRate = Mathf.Clamp(cfg.baudRate, 1200, 2000000);
            cfg.idleTimeoutSec = Finite(cfg.idleTimeoutSec, 15f, 1f, 3600f);
            cfg.popupTimeoutSec = Finite(cfg.popupTimeoutSec, 30f, 0f, 3600f);
            cfg.videoFadeSec = Finite(cfg.videoFadeSec, 0.6f, 0.01f, 10f);
            cfg.videoWidth = Mathf.Clamp(cfg.videoWidth, 16, 8192);
            cfg.videoHeight = Mathf.Clamp(cfg.videoHeight, 16, 8192);
            cfg.logMaxKB = Mathf.Clamp(cfg.logMaxKB, 64, 102400);
            cfg.logFilesToKeep = Mathf.Clamp(cfg.logFilesToKeep, 1, 20);
            cfg.leds = cfg.leds ?? new LedConfig();
            cfg.floorOutput = cfg.floorOutput ?? new FloorOutputConfig();
            return cfg;
        }

        static float Finite(float value, float fallback, float min, float max) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
    }

    /// <summary>
    /// Button LED behaviour per state. Values: "off", "all", "selected", "others".
    /// "selected" = only the open product's LED, "others" = all except the open product.
    /// </summary>
    [Serializable]
    public class LedConfig
    {
        public string attract = "off";
        public string interactive = "all";
        public string product = "selected";
    }

    /// <summary>Spare output (floor light tubes) per state. Product uses "interactive".</summary>
    [Serializable]
    public class FloorOutputConfig
    {
        public bool attract = false;
        public bool interactive = false;
    }
}
