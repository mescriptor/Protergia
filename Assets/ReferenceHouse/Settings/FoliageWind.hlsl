#ifndef REFERENCE_HOUSE_FOLIAGE_WIND_INCLUDED
#define REFERENCE_HOUSE_FOLIAGE_WIND_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Smoothness;
    half _Metallic;
    half _BumpScale;
    half _Cutoff;
    half _Surface;
    float _WindResponse;
    float _WindMaskMode;
CBUFFER_END

TEXTURE2D(_MetallicGlossMap);
SAMPLER(sampler_MetallicGlossMap);

// World XZ direction, strength multiplier and speed multiplier.
float4 _HouseFoliageWind;

float3 BendFoliage(float3 positionOS, half bladeWeight)
{
    float3 positionWS = GetAbsolutePositionWS(TransformObjectToWorld(positionOS));
    float height = max(0.0, positionWS.y - unity_RendererBounds_Min.y);
    float weight;
    if (_WindMaskMode < 0.5)
        weight = saturate(height / max(0.001, unity_RendererBounds_Max.y - unity_RendererBounds_Min.y));
    else if (_WindMaskMode < 1.5)
        weight = saturate(height / 0.65);
    else if (_WindMaskMode < 2.5)
        weight = 1.0;
    else
        weight = saturate(bladeWeight);

    float2 direction = _HouseFoliageWind.xy;
    float time = _Time.y * _HouseFoliageWind.w;
    float distanceDownwind = dot(positionWS.xz, direction);
    float gust = sin(time * 1.45 - distanceDownwind * 0.72);
    float ripple = sin(time * 3.1 - distanceDownwind * 2.2 + positionWS.y * 1.6);
    float sway = 0.28 + 0.54 * gust + 0.18 * ripple;
    float2 offset = direction * (sway * weight * _WindResponse * _HouseFoliageWind.z);
    return positionOS + TransformWorldToObjectDir(float3(offset.x, 0.0, offset.y), false);
}

// Supply the metallic URP Lit inputs used by the foliage materials.
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surface)
{
    surface = (SurfaceData)0;
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    surface.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
    surface.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    surface.normalTS = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    surface.occlusion = 1.0h;
    surface.smoothness = _Smoothness;
    surface.metallic = _Metallic;
    #ifdef _METALLICSPECGLOSSMAP
        half4 metallicGloss = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
        surface.metallic = metallicGloss.r;
        surface.smoothness *= metallicGloss.a;
    #endif
}

#endif
