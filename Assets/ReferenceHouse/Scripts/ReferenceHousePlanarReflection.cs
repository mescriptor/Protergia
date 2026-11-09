using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ReferenceHouseTools
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class ReferenceHousePlanarReflection : MonoBehaviour
    {
        public float floorHeight = .65f;
        public int resolution = 1536;
        Camera reflectionCamera;
        RenderTexture reflectionTexture;
        bool rendering;
        static readonly int TextureId = Shader.PropertyToID("_HousePlanarReflectionTexture");
        static readonly int MatrixId = Shader.PropertyToID("_HousePlanarReflectionVP");
        static readonly int ValidId = Shader.PropertyToID("_HousePlanarReflectionValid");

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderReflection;
            RenderPipelineManager.beginCameraRendering += RenderReflection;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= RenderReflection;
            Shader.SetGlobalFloat(ValidId, 0);
            if (reflectionCamera) DestroyResource(reflectionCamera.gameObject);
            if (reflectionTexture) { reflectionTexture.Release(); DestroyResource(reflectionTexture); }
            reflectionCamera = null;
            reflectionTexture = null;
        }

        static void DestroyResource(Object obj)
        {
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }

        void RenderReflection(ScriptableRenderContext context, Camera source)
        {
            if (rendering || !isActiveAndEnabled || source == reflectionCamera || !source) return;
            if (source.cameraType != CameraType.Game && source.cameraType != CameraType.SceneView) return;
            if (source.transform.position.y <= floorHeight + .01f) return;
            rendering = true;
            bool oldInvert = GL.invertCulling;
            try
            {
                if (!reflectionCamera)
                {
                    var go = new GameObject("House floor reflection camera") { hideFlags = HideFlags.HideAndDontSave };
                    reflectionCamera = go.AddComponent<Camera>();
                    reflectionCamera.enabled = false;
                    go.AddComponent<UniversalAdditionalCameraData>();
                }
                int width = Mathf.Clamp(resolution, 256, 2048);
                int height = Mathf.Clamp(Mathf.RoundToInt(width / Mathf.Max(.5f, source.aspect)), 256, 2048);
                if (!reflectionTexture || reflectionTexture.width != width || reflectionTexture.height != height)
                {
                    if (reflectionTexture) { reflectionTexture.Release(); DestroyResource(reflectionTexture); }
                    reflectionTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    {
                        name = "House polished floor planar reflection", hideFlags = HideFlags.HideAndDontSave,
                        useMipMap = true, autoGenerateMips = false, antiAliasing = 1,
                        filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp
                    };
                    reflectionTexture.Create();
                }
                reflectionCamera.CopyFrom(source);
                reflectionCamera.enabled = false;
                reflectionCamera.cameraType = CameraType.Game;
                reflectionCamera.targetTexture = reflectionTexture;
                reflectionCamera.rect = new Rect(0, 0, 1, 1);
                reflectionCamera.allowHDR = true;
                reflectionCamera.allowMSAA = false;
                reflectionCamera.useOcclusionCulling = false;
                reflectionCamera.cullingMask = source.cullingMask & ~(1 << 29) & ~(1 << 30);
                reflectionCamera.clearFlags = CameraClearFlags.Skybox;
                var data = reflectionCamera.GetUniversalAdditionalCameraData();
                data.renderType = CameraRenderType.Base;
                data.renderPostProcessing = false;
                data.requiresColorTexture = false;
                data.requiresDepthTexture = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;
                data.allowXRRendering = false;

                Matrix4x4 mirror = Matrix4x4.identity;
                mirror.m11 = -1f;
                mirror.m13 = 2f * floorHeight;
                reflectionCamera.transform.position = mirror.MultiplyPoint(source.transform.position);
                reflectionCamera.transform.rotation = Quaternion.LookRotation(
                    mirror.MultiplyVector(source.transform.forward), mirror.MultiplyVector(source.transform.up));
                reflectionCamera.worldToCameraMatrix = source.worldToCameraMatrix * mirror;
                Vector3 clipPosition = reflectionCamera.worldToCameraMatrix.MultiplyPoint(new Vector3(0, floorHeight + .004f, 0));
                Vector3 clipNormal = reflectionCamera.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
                Vector4 clipPlane = new Vector4(clipNormal.x, clipNormal.y, clipNormal.z, -Vector3.Dot(clipPosition, clipNormal));
                reflectionCamera.projectionMatrix = source.projectionMatrix;
                reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
                GL.invertCulling = !oldInvert;
                Shader.SetGlobalFloat(ValidId, 0);
#pragma warning disable CS0618
                UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
                reflectionTexture.GenerateMips();
                Shader.SetGlobalTexture(TextureId, reflectionTexture);
                Shader.SetGlobalMatrix(MatrixId, GL.GetGPUProjectionMatrix(reflectionCamera.projectionMatrix, true) * reflectionCamera.worldToCameraMatrix);
                Shader.SetGlobalFloat(ValidId, 1);
            }
            finally
            {
                GL.invertCulling = oldInvert;
                rendering = false;
            }
        }
    }
}
