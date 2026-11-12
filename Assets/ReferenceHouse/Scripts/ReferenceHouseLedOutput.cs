using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ReferenceHouseTools
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ReferenceHouseLedOutput : MonoBehaviour
    {
        public const int OutputWidth = 1920, OutputHeight = 1080;
        public Canvas wallCanvas;

        Camera houseCamera;
        RenderTexture nativeFrame, previousTarget;
        Camera displayCamera, uiCamera;
        Canvas displayCanvas;
        RenderMode previousCanvasMode;
        Camera previousCanvasCamera;
        float previousPlaneDistance, previousCanvasScale;
        CanvasScaler scaler;
        bool scalerWasEnabled;

        void Awake()
        {
            Application.runInBackground = true;
#if UNITY_STANDALONE && !UNITY_EDITOR
            Screen.SetResolution(OutputWidth, OutputHeight, FullScreenMode.FullScreenWindow);
#endif
        }

        void OnEnable()
        {
            houseCamera = GetComponent<Camera>();
            UpdateOutput();
        }

        void Update() => UpdateOutput();

        void UpdateOutput()
        {
            // Native preview and captures use the original camera and screen-space overlay directly.
            bool nativeSize = Screen.width == ReferenceHouseLedWall.Width && Screen.height == ReferenceHouseLedWall.Height;
            if (nativeSize && nativeFrame) RestoreNativeOutput();
            if (!nativeSize && !nativeFrame) StartStretchedOutput();
        }

        void StartStretchedOutput()
        {
            previousTarget = houseCamera.targetTexture;
            var pipeline = UniversalRenderPipeline.asset;
            var format = houseCamera.allowHDR && pipeline.supportsHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            var descriptor = new RenderTextureDescriptor(ReferenceHouseLedWall.Width, ReferenceHouseLedWall.Height, format, 24)
            {
                msaaSamples = houseCamera.allowMSAA ? pipeline.msaaSampleCount : 1
            };
            descriptor.msaaSamples = SystemInfo.GetRenderTextureSupportedMSAASampleCount(descriptor);
            nativeFrame = new RenderTexture(descriptor)
            {
                name = "LED wall · native frame", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            nativeFrame.Create();
            houseCamera.targetTexture = nativeFrame;
            houseCamera.aspect = (float)nativeFrame.width / nativeFrame.height;

            displayCamera = CreateCamera("LED wall · display", 0);
            displayCamera.depth = houseCamera.depth + 1;
            displayCamera.targetDisplay = houseCamera.targetDisplay;
            if (wallCanvas) AddWallCanvasToFrame();

            var display = new GameObject("LED wall · fullscreen output", typeof(RectTransform), typeof(Canvas));
            displayCanvas = display.GetComponent<Canvas>();
            displayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            displayCanvas.targetDisplay = displayCamera.targetDisplay;
            var image = new GameObject("Native frame", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            image.rectTransform.SetParent(display.transform, false);
            image.rectTransform.anchorMin = Vector2.zero;
            image.rectTransform.anchorMax = Vector2.one;
            image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
            image.raycastTarget = false;
            image.texture = nativeFrame;
        }

        static Camera CreateCamera(string name, int cullingMask)
        {
            var camera = new GameObject(name, typeof(Camera)).GetComponent<Camera>();
            camera.cullingMask = cullingMask;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            return camera;
        }

        void AddWallCanvasToFrame()
        {
            // URP draws this camera over the processed house frame, with a fresh depth buffer.
            uiCamera = CreateCamera("LED wall · native overlay", 1 << wallCanvas.gameObject.layer);
            uiCamera.transform.position = new Vector3(0, -10000, 0);
            uiCamera.targetTexture = nativeFrame;
            uiCamera.aspect = houseCamera.aspect;
            var data = uiCamera.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            houseCamera.GetUniversalAdditionalCameraData().cameraStack.Add(uiCamera);

            previousCanvasMode = wallCanvas.renderMode;
            previousCanvasCamera = wallCanvas.worldCamera;
            previousPlaneDistance = wallCanvas.planeDistance;
            previousCanvasScale = wallCanvas.scaleFactor;
            scaler = wallCanvas.GetComponent<CanvasScaler>();
            scalerWasEnabled = scaler && scaler.enabled;
            if (scalerWasEnabled) scaler.enabled = false;
            wallCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            wallCanvas.worldCamera = uiCamera;
            wallCanvas.planeDistance = 1f;
            wallCanvas.scaleFactor = 1f;
            Canvas.ForceUpdateCanvases();
        }

        void OnDisable()
        {
            if (nativeFrame) RestoreNativeOutput();
        }

        void RestoreNativeOutput()
        {
            if (houseCamera)
            {
                houseCamera.targetTexture = previousTarget;
                houseCamera.ResetAspect();
            }
            if (uiCamera && houseCamera) houseCamera.GetUniversalAdditionalCameraData().cameraStack.Remove(uiCamera);
            if (wallCanvas)
            {
                wallCanvas.worldCamera = previousCanvasCamera;
                wallCanvas.renderMode = previousCanvasMode;
                wallCanvas.planeDistance = previousPlaneDistance;
                wallCanvas.scaleFactor = previousCanvasScale;
                if (scalerWasEnabled && scaler) scaler.enabled = true;
            }
            if (uiCamera)
            {
                uiCamera.enabled = false;
                uiCamera.targetTexture = null;
                Destroy(uiCamera.gameObject);
            }
            if (displayCamera)
            {
                displayCamera.enabled = false;
                Destroy(displayCamera.gameObject);
            }
            if (displayCanvas)
            {
                displayCanvas.enabled = false;
                Destroy(displayCanvas.gameObject);
            }
            nativeFrame.Release();
            Destroy(nativeFrame);
            nativeFrame = null;
        }
    }
}
