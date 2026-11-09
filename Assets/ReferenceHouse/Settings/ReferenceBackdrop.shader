Shader "Reference House/Daylight Backdrop"
{
    Properties
    {
        _Floor ("Floor", Color) = (.72, .72, .69, 1)
        _Horizon ("Horizon", Color) = (.85, .86, .85, 1)
        _Sky ("Sky", Color) = (.59, .73, .88, 1)
        _BackdropTex ("Neighborhood photograph", 2D) = "white" {}
        _UseNeighborhood ("Show neighborhood photograph", Float) = 0
        _BackdropExposure ("Photograph exposure", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Floor, _Horizon, _Sky;
                float _UseNeighborhood, _BackdropExposure;
            CBUFFER_END
            TEXTURE2D(_BackdropTex);
            SAMPLER(sampler_BackdropTex);

            struct Input { float4 position : POSITION; };
            struct Output
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float4 photographScreenPosition : TEXCOORD1;
            };

            Output Vertex(Input i)
            {
                VertexPositionInputs v = GetVertexPositionInputs(i.position.xyz);
                Output o;
                o.position = v.positionCS;
                o.worldPosition = v.positionWS;
                o.photographScreenPosition = ComputeScreenPos(v.positionCS);
                return o;
            }

            half4 Fragment(Output i) : SV_Target
            {
                // A floor triangle spans several shadow cascades. Choose the cascade
                // at the receiving pixel so contact shadows land beneath the house.
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(i.worldPosition));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(i.worldPosition);
                #endif
                half shadow = GetMainLight(shadowCoord).shadowAttenuation;
                if (_UseNeighborhood > .5)
                {
                    float2 uv = i.photographScreenPosition.xy / i.photographScreenPosition.w;
                    half3 photograph = SAMPLE_TEXTURE2D(_BackdropTex, sampler_BackdropTex, uv).rgb;
                    half contact = lerp(1.0h, lerp(.84h, 1.0h, shadow), 1.0h - smoothstep(-2.0, -.8, i.worldPosition.y));
                    return half4(photograph * _BackdropExposure * contact, 1);
                }
                half3 floor = _Floor.rgb * lerp(.68h, 1.0h, shadow);
                half3 sky = lerp(_Horizon.rgb, _Sky.rgb, smoothstep(0.0, 13.0, i.worldPosition.y));
                return half4(lerp(floor, sky, smoothstep(-1.95, .25, i.worldPosition.y)), 1);
            }
            ENDHLSL
        }
    }
}
