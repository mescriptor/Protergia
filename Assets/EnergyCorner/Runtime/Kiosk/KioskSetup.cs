using UnityEngine;

namespace EnergyCorner
{
    /// <summary>
    /// Kiosk basics at startup: borderless fullscreen, cursor hidden, keep running without
    /// focus. ReferenceHouseLedOutput is the single owner of output resolution,
    /// including optional screenWidth/screenHeight config overrides.
    /// </summary>
    public class KioskSetup : MonoBehaviour
    {
        void Awake()
        {
            var cfg = EnergyCornerConfig.Current;

            Application.runInBackground = true;      // keep the serial link and videos running without focus
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 1;

            if (!Application.isEditor)
            {
                // ReferenceHouseLedOutput owns the transport resolution and any config override.
                Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
                Cursor.visible = !cfg.hideCursor;
                Cursor.lockState = CursorLockMode.None;
            }

            string mode = cfg.screenWidth > 0 && cfg.screenHeight > 0
                ? "forced " + cfg.screenWidth + "x" + cfg.screenHeight
                : "1920x1080 LED transport (native composition 1872x1560)";
            Debug.Log("[EC] Kiosk: resolution " + mode + ", log at " + FileLogger.LogPath);
        }
    }
}
