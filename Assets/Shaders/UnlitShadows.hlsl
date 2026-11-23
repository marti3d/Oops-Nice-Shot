#ifndef UNLIT_SHADOWS_INCLUDED
#define UNLIT_SHADOWS_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void GetShadow_float(float3 WorldPos, out float Shadow)
{
#ifdef SHADERGRAPH_PREVIEW
        Shadow = 1.0;
#else
    float4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
    Shadow = MainLightRealtimeShadow(shadowCoord);
#endif
}

#endif
