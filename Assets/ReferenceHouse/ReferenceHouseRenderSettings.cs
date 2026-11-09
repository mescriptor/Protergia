using UnityEngine;
using UnityEngine.Rendering;

namespace ReferenceHouseTools
{
    [ExecuteAlways]
    public sealed class ReferenceHouseRenderSettings : MonoBehaviour
    {
        public RenderPipelineAsset pipeline;
        RenderPipelineAsset previous;
        bool applied;
        void OnEnable()
        {
            if(!pipeline)return;
            previous=QualitySettings.renderPipeline;
            QualitySettings.renderPipeline=pipeline;
            applied=true;
        }
        void OnDisable()
        {
            if(applied && QualitySettings.renderPipeline==pipeline && previous!=pipeline)
                QualitySettings.renderPipeline=previous;
            applied=false;
        }
    }
}
