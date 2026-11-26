Shader "MiniGolf/Ghost"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,0.35)
        _Cutoff("Source Cutoff", Range(0,1)) = 0
        _SourceAlpha("Source Alpha", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Name "GhostForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _Cutoff, _SourceAlpha;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half sourceAlpha = texel.a * _SourceAlpha;
                clip(sourceAlpha - _Cutoff);
                // Repeating 4x4 Bayer pattern, evaluated in screen pixels.
                uint2 pixel = (uint2)input.positionCS.xy;
                uint2 low = pixel & 1u;
                uint2 high = (pixel >> 1u) & 1u;
                uint a = ((low.x ^ low.y) << 1u) + low.y;
                uint b = ((high.x ^ high.y) << 1u) + high.y;
                half threshold = (4u * a + b + 0.5h) / 16.0h;
                clip(sourceAlpha * _BaseColor.a - threshold);
                return half4(texel.rgb * _BaseColor.rgb, 1);

            }
            ENDHLSL
        }
    }
    FallBack Off
}
