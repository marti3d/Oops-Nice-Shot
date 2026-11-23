Shader "Hidden/Custom/Pixelation"
{
    Properties
    {
        _TargetHeight ("Target Height", Range(90, 1080)) = 240
        _Saturation ("Saturation", Range(0, 2)) = 1.15
        _Contrast ("Contrast", Range(0.5, 2)) = 1.08
        _Brightness ("Brightness", Range(-0.5, 0.5)) = 0.02
        _ColorSteps ("Color Steps", Range(2, 64)) = 16
        _DitherStrength ("Dither Strength", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "ShortHikePostProcess"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _TargetHeight;
                float _Saturation;
                float _Contrast;
                float _Brightness;
                float _ColorSteps;
                float _DitherStrength;
            CBUFFER_END

            float Bayer4x4(int2 p)
            {
                int x = p.x & 3;
                int y = p.y & 3;

                if (y == 0)
                {
                    if (x == 0) return 0.0 / 16.0;
                    if (x == 1) return 8.0 / 16.0;
                    if (x == 2) return 2.0 / 16.0;
                    return 10.0 / 16.0;
                }
                else if (y == 1)
                {
                    if (x == 0) return 12.0 / 16.0;
                    if (x == 1) return 4.0 / 16.0;
                    if (x == 2) return 14.0 / 16.0;
                    return 6.0 / 16.0;
                }
                else if (y == 2)
                {
                    if (x == 0) return 3.0 / 16.0;
                    if (x == 1) return 11.0 / 16.0;
                    if (x == 2) return 1.0 / 16.0;
                    return 9.0 / 16.0;
                }
                else
                {
                    if (x == 0) return 15.0 / 16.0;
                    if (x == 1) return 7.0 / 16.0;
                    if (x == 2) return 13.0 / 16.0;
                    return 5.0 / 16.0;
                }
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 screenSize = _ScreenParams.xy;

                // Compute pixel scale from target vertical resolution
                float targetHeight = max(_TargetHeight, 1.0);
                float pixelScale = max(floor(screenSize.y / targetHeight), 1.0);

                float2 virtualResolution = floor(screenSize / pixelScale);

                // Snap UVs to low-res pixel grid
                float2 pixelUV =
                    (floor(uv * virtualResolution) + 0.5) / virtualResolution;

                half4 color = SAMPLE_TEXTURE2D_X(
                    _BlitTexture,
                    sampler_LinearClamp,
                    pixelUV
                );

                float3 rgb = color.rgb;

                // Saturation
                float luminance = dot(rgb, float3(0.2126, 0.7152, 0.0722));
                rgb = lerp(luminance.xxx, rgb, _Saturation);

                // Contrast
                rgb = (rgb - 0.5) * _Contrast + 0.5;

                // Brightness
                rgb += _Brightness;

                // Dither before posterization
                float2 pixelPos = floor(uv * screenSize);
                float dither = Bayer4x4(int2(pixelPos)) - 0.5;

                float steps = max(_ColorSteps, 2.0);
                rgb += dither * (_DitherStrength / steps);

                // Posterize
                rgb = floor(saturate(rgb) * steps) / steps;

                return half4(saturate(rgb), color.a);
            }

            ENDHLSL
        }
    }

    FallBack Off
}