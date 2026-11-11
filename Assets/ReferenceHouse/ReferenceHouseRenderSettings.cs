using UnityEngine;
using UnityEngine.Rendering;

namespace ReferenceHouseTools
{
    [ExecuteAlways]
    public sealed class ReferenceHouseRenderSettings : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;

        [Header("Foliage wind")]
        [Tooltip("Direction of travel in world space. 0° is +Z, 90° is +X.")]
        [Range(0f, 360f)] public float windDirection = 65f;
        [Tooltip("Displacement multiplier. Set to zero for still foliage.")]
        [Range(0f, 2f)] public float windStrength = 1f;
        [Tooltip("Speed of the gusts moving through the plants.")]
        [Range(0f, 3f)] public float windSpeed = 1f;

        static readonly int FoliageWind = Shader.PropertyToID("_HouseFoliageWind");
        Vector3 appliedWind;
        RenderPipelineAsset previous;
        bool applied;
        void OnEnable()
        {
            ApplyWind();
            if(!pipeline)return;
            previous=QualitySettings.renderPipeline;
            QualitySettings.renderPipeline=pipeline;
            applied=true;
        }

        void Update()
        {
            if (windDirection != appliedWind.x || windStrength != appliedWind.y || windSpeed != appliedWind.z)
                ApplyWind();
        }

        void ApplyWind()
        {
            float angle = windDirection * Mathf.Deg2Rad;
            Shader.SetGlobalVector(FoliageWind, new Vector4(Mathf.Sin(angle), Mathf.Cos(angle), windStrength, windSpeed));
            appliedWind = new Vector3(windDirection, windStrength, windSpeed);
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(FoliageWind, Vector4.zero);
            if(applied && QualitySettings.renderPipeline==pipeline && previous!=pipeline)
                QualitySettings.renderPipeline=previous;
            applied=false;
        }
    }
}
