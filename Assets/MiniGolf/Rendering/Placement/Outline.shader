Shader "MiniGolf/Outline"
{
    Properties
    {
        [HideInInspector] _MaskMode ("Mask Mode", Float) = 0
        [HideInInspector] _Cull ("Cull", Float) = 1
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 6
        [HideInInspector] _StencilPass ("Stencil Pass", Float) = 0
        _OutlineColor ("Outline Color", Color) = (1, 0.85, 0, 1)
        [Toggle] _ScreenSpaceWidth ("Adaptive Screen Space Thickness", Float) = 1
        _OutlinePixels ("Outline Thickness (Pixels)", Range(0, 12)) = 3
        _OutlineWidth ("Outline Width (World Units)", Range(0, 0.2)) = 0.025
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay-1" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull [_Cull]
            ColorMask [_ColorMask]
            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 1
                Comp [_StencilComp]
                Pass [_StencilPass]
            }
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineWidth;
                float _MaskMode;
                float _ScreenSpaceWidth;
                float _OutlinePixels;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                // Convert pixel thickness to world units at the vertex depth.
                // Perspective uses view depth; orthographic projection does not.
                float depth = max(-TransformWorldToView(p).z, 0.0001);
                float projectionScale = max(abs(UNITY_MATRIX_P._m11), 0.0001);
                float pixelWorldSize = 2.0 * lerp(depth, 1.0, unity_OrthoParams.w)
                    / (projectionScale * max(_ScreenParams.y, 1.0));
                float width = lerp(_OutlineWidth, _OutlinePixels * pixelWorldSize,
                                   saturate(_ScreenSpaceWidth));
                p += TransformObjectToWorldNormal(v.normalOS) * width * (1.0 - _MaskMode);
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target { return half4(_OutlineColor.rgb, 1); }
            ENDHLSL
        }
    }
}
