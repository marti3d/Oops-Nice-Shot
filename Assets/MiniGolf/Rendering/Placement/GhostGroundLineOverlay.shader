Shader "Gizmo/Ghost Ground Line Overlay"
{
    Properties
    {
        _BaseMap ("Dash Pattern", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest ("Depth Test", Float) = 4
        _OccludedOpacity ("Occluded Opacity Multiplier", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float _DepthTest;
                float _OccludedOpacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * input.color;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = SampleSceneDepth(screenUV);
                #if UNITY_REVERSED_Z
                    bool occluded = input.positionCS.z < sceneDepth - 0.000001;
                #else
                    bool occluded = input.positionCS.z > sceneDepth + 0.000001;
                #endif

                // Match PlacementIndicator: Always bypasses fading, other modes fade covered pixels.
                float visibility = (occluded && _DepthTest < 7.5) ? _OccludedOpacity : 1.0;
                color.a *= visibility;
                return color;
            }
            ENDHLSL
        }
    }
}
