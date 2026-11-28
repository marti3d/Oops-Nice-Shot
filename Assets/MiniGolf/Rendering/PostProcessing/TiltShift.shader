
Shader "Custom/TiltShift"
{
    Properties
    {
        _FocusPosition ("Focus Position", Range(0,1)) = 0.5
        _FocusWidth ("Focus Width", Range(0.01,1)) = 0.12
        _BlurStrength ("Blur Strength", Range(0,10)) = 6
        _Transition ("Transition", Range(0.001,0.5)) = 0.35
        _Saturation ("Saturation", Range(0,2)) = 1.15
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "TiltShift"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FocusPosition;
            float _FocusWidth;
            float _BlurStrength;
            float _Transition;
            float _Saturation;

            half4 SampleScene(float2 uv)
            {
                uv = saturate(uv);

                return SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture,
                    sampler_LinearClamp,
                    uv,
                    0
                );
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord.xy;

                // ----------------------------------
                // 1. DISTANCIA A LA ZONA ENFOCADA
                // ----------------------------------

                float distanceToFocus =
                    abs(uv.y - _FocusPosition);

                float halfWidth = _FocusWidth * 0.5;

                // ----------------------------------
                // 2. TRANSICION SUAVE (SMOOTHERSTEP)
                // ----------------------------------

                float t = saturate(
                    (distanceToFocus - halfWidth) /
                    max(_Transition, 0.0001)
                );

                // Curva quintica:
                // derivadas suaves en ambos extremos
                float blurMask =
                    t * t * t *
                    (t * (t * 6.0 - 15.0) + 10.0);

                // ----------------------------------
                // 3. RADIO DE BLUR PROGRESIVO
                // ----------------------------------

                float radiusFactor =
                    pow(blurMask, 1.5);

                float blurRadius =
                    radiusFactor * _BlurStrength;

                float2 texelSize =
                    _BlitTexture_TexelSize.xy;

                // ----------------------------------
                // 4. COLOR ORIGINAL
                // ----------------------------------

                half4 original = SampleScene(uv);

                // ----------------------------------
                // 5. BLUR DE 9 MUESTRAS
                // ----------------------------------

                half4 blurred = 0;

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(-1, -1));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(0, -1));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(1, -1));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(-1, 0));

                blurred += SampleScene(uv);

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(1, 0));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(-1, 1));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(0, 1));

                blurred += SampleScene(
                    uv + texelSize * blurRadius *
                    float2(1, 1));

                blurred /= 9.0;

                // ----------------------------------
                // 6. MEZCLA SUAVE
                // ----------------------------------

                float blendFactor =
                    pow(blurMask, 1.3);

                half4 finalColor =
                    lerp(original, blurred, blendFactor);

                // ----------------------------------
                // 7. SATURACION
                // ----------------------------------

                float luminance = dot(
                    finalColor.rgb,
                    float3(0.2126, 0.7152, 0.0722)
                );

                finalColor.rgb = lerp(
                    luminance.xxx,
                    finalColor.rgb,
                    _Saturation
                );

                return finalColor;
            }

            ENDHLSL
        }
    }
}
