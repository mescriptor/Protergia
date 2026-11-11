using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace ReferenceHouseTools
{
    /// <summary>Pixel-calibrated LED test UI, with a label following a scene object.</summary>
    [ExecuteAlways]
    public sealed class ReferenceHouseLedWall : MonoBehaviour
    {
        public const int Width = 1872, Height = 1560;
        public Camera sceneCamera;
        public Transform target;
        public Vector3 targetLocalPoint;
        public Canvas canvas;
        public RectTransform label, connector, marker;
        public Vector2 labelOffset = new Vector2(-230, 100);

        void Awake()
        {
            if (!Application.isPlaying) return;
            Application.runInBackground = true;
#if !UNITY_EDITOR
            Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
#endif
        }

        void LateUpdate()
        {
            if (!sceneCamera || !target || !canvas || !label || !connector || !marker) return;
            var viewport = sceneCamera.WorldToViewportPoint(target.TransformPoint(targetLocalPoint));
            bool visible = viewport.z > 0 && viewport.x >= 0 && viewport.x <= 1 && viewport.y >= 0 && viewport.y <= 1;
            label.gameObject.SetActive(visible);
            connector.gameObject.SetActive(visible);
            marker.gameObject.SetActive(visible);
            if (!visible) return;
            var canvasRect = (RectTransform)canvas.transform;
            var point = new Vector2(viewport.x * canvasRect.rect.width, viewport.y * canvasRect.rect.height);
            var labelPoint = point + labelOffset;
            labelPoint.x = Mathf.Clamp(labelPoint.x, label.rect.width * .5f + 32, canvasRect.rect.width - label.rect.width * .5f - 32);
            labelPoint.y = Mathf.Clamp(labelPoint.y, 330, canvasRect.rect.height - 180);
            label.anchoredPosition = labelPoint;
            marker.anchoredPosition = point;
            var end = labelPoint - new Vector2(0, label.rect.height * .5f);
            var delta = end - point;
            connector.anchoredPosition = point;
            connector.sizeDelta = new Vector2(delta.magnitude, 4);
            connector.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

#if UNITY_EDITOR
        public void CapturePair(string folder) { StartCoroutine(CaptureFrames(folder)); }

        IEnumerator CaptureFrames(string folder)
        {
            Directory.CreateDirectory(folder);
            // Settle the game view and temporal rendering before recording either frame.
            for (int i = 0; i < 12; i++) yield return null;
            bool wasEnabled = canvas.enabled;
            try
            {
                canvas.enabled = false;
                yield return new WaitForEndOfFrame();
                WriteFrame(Path.Combine(folder, "House_1872x1560.png"));
                canvas.enabled = true;
                Canvas.ForceUpdateCanvases();
                yield return null;
                yield return new WaitForEndOfFrame();
                WriteFrame(Path.Combine(folder, "House_LED_Panels_1872x1560.png"));
                var text = canvas.GetComponentsInChildren<Text>();
                var report = new CaptureReport
                {
                    width = Screen.width, height = Screen.height, playing = Application.isPlaying,
                    canvasScale = canvas.scaleFactor,
                    targetName = target.name,
                    labelPosition = label.anchoredPosition,
                    samples = text.Where(t => t.name == "Sample text").Select(t => new SampleReport
                    {
                        fontSize = t.fontSize, bestFit = t.resizeTextForBestFit,
                        box = t.rectTransform.rect.size,
                        preferred = new Vector2(t.preferredWidth, t.preferredHeight),
                        missingCharacters = new string(t.text.Where(c => !char.IsWhiteSpace(c) && !t.font.HasCharacter(c)).Distinct().ToArray())
                    }).ToArray()
                };
                File.WriteAllText(Path.Combine(folder, "verification.json"), JsonUtility.ToJson(report, true));
                Debug.Log("LED_WALL_CAPTURE_COMPLETE 1872x1560");
            }
            finally { canvas.enabled = wasEnabled; }
        }

        static void WriteFrame(string path)
        {
            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                if (texture.width != Width || texture.height != Height)
                    throw new InvalidOperationException("Unexpected Game view resolution: " + texture.width + "x" + texture.height);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { Destroy(texture); }
        }

        [Serializable] class CaptureReport
        {
            public int width, height;
            public bool playing;
            public float canvasScale;
            public string targetName;
            public Vector2 labelPosition;
            public SampleReport[] samples;
        }
        [Serializable] class SampleReport
        {
            public int fontSize;
            public bool bestFit;
            public Vector2 box, preferred;
            public string missingCharacters;
        }
#endif
    }
}
