Shader "Reference House/Polished stone planar reflection"
{
    Properties
    {
        _BaseOpacity ("Face-on reflection", Range(0,1)) = .13
        _GrazingOpacity ("Grazing reflection", Range(0,1)) = .30
        _ReflectionMip ("Polish softness", Range(0,6)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _BaseOpacity, _GrazingOpacity, _ReflectionMip;
            CBUFFER_END
            TEXTURE2D(_HousePlanarReflectionTexture);
            SAMPLER(sampler_HousePlanarReflectionTexture);
            float4x4 _HousePlanarReflectionVP;
            float _HousePlanarReflectionValid;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                clip(_HousePlanarReflectionValid - .5);
                float4 projected = mul(_HousePlanarReflectionVP, float4(i.positionWS, 1));
                clip(projected.w);
                float2 uv = projected.xy / projected.w * .5 + .5;
                #if UNITY_UV_STARTS_AT_TOP
                    uv.y = 1.0 - uv.y;
                #endif
                clip(min(min(uv.x, uv.y), min(1.0-uv.x, 1.0-uv.y)));
                half3 reflected = SAMPLE_TEXTURE2D_LOD(_HousePlanarReflectionTexture, sampler_HousePlanarReflectionTexture, uv, _ReflectionMip).rgb;
                half grazing = pow(1.0h - saturate(normalize(GetWorldSpaceViewDir(i.positionWS)).y), 3.0h);
                return half4(reflected, lerp(_BaseOpacity, _GrazingOpacity, grazing));
            }
            ENDHLSL
        }
    }
}
