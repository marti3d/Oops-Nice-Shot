Shader "MiniGolf/Water"
{
    Properties
    {
        [Header(Appearance)]
        _DeepColor ("Deep Color", Color) = (0.05, 0.4, 0.66, 1)
        _ShallowColor ("Shallow Color", Color) = (0.19, 0.78, 0.8, 1)
        _FoamColor ("Foam Color", Color) = (0.96, 1, 1, 1)
        _WaterOpacity ("Water Opacity", Range(0, 1)) = 0.87

        [Header(Waves)]
        _PatternScale ("Pattern Scale", Range(0.1, 30)) = 3.2
        _WaveSpeed ("Wave Speed", Range(0, 4)) = 1.05
        _WaveHeight ("Wave Height", Range(0, 2)) = 0.14
        _WaveDirection ("Wave Direction", Range(-180, 180)) = 0
        _PrimaryWaveSize ("Primary Wave Size", Range(0.2, 4)) = 1
        _RippleSize ("Ripple Size", Range(0.2, 4)) = 1
        _RippleSpeed ("Ripple Speed", Range(0, 4)) = 1
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35

        [Header(Foam)]
        _FoamWidth ("Foam Width", Range(0, 4)) = 0.32
        _SurfaceFoam ("Surface Foam", Range(0, 1)) = 0.83
        _FoamScale ("Foam Scale", Range(0.1, 4)) = 1
        _FoamSpeed ("Foam Speed", Range(0, 4)) = 1.05
        _FoamAmount ("Foam Amount", Range(0, 3)) = 1
        _FoamCutoff ("Foam Cutoff", Range(0, 1)) = 0
        _FoamRandomness ("Foam Randomness", Range(0, 1)) = 0.7
        _FoamCellScatter ("Cell Scatter", Range(0, 1)) = 0.65
        _FoamEdgeWaviness ("Edge Waviness", Range(0, 2)) = 0.8
        _FoamCellMovement ("Cell Movement", Range(0, 1.8)) = 0.6
        _FoamCellRotation ("Cell Rotation", Range(-180, 180)) = 0
        _FoamCellStretch ("Cell Stretch", Range(0.5, 2)) = 1
        _FoamRibbonThickness ("Ribbon Thickness", Range(0.2, 2)) = 1
        _FoamThicknessVariation ("Thickness Variation", Range(0, 1)) = 0.75
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Name "Water"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DeepColor;
                float4 _ShallowColor;
                float4 _FoamColor;
                float _WaterOpacity;
                float _PatternScale;
                float _WaveSpeed;
                float _WaveHeight;
                float _WaveDirection;
                float _PrimaryWaveSize;
                float _RippleSize;
                float _RippleSpeed;
                float _RippleStrength;
                float _FoamWidth;
                float _SurfaceFoam;
                float _FoamScale;
                float _FoamSpeed;
                float _FoamAmount;
                float _FoamCutoff;
                float _FoamRandomness;
                float _FoamCellScatter;
                float _FoamEdgeWaviness;
                float _FoamCellMovement;
                float _FoamCellRotation;
                float _FoamCellStretch;
                float _FoamRibbonThickness;
                float _FoamThicknessVariation;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float CellHash(float2 cell)
            {
                return frac(sin(dot(cell, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 f = frac(position);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(CellHash(cell), CellHash(cell + float2(1, 0)), f.x),
                    lerp(CellHash(cell + float2(0, 1)), CellHash(cell + 1), f.x),
                    f.y);
            }

            float3 ApplyWaves(float3 world)
            {
                float2 p = world.xz / max(_PatternScale, 0.05);
                float t = _Time.y * _WaveSpeed;
                float angle = radians(_WaveDirection);
                float2 waveP = float2(dot(p, float2(cos(angle), -sin(angle))),
                                      dot(p, float2(sin(angle), cos(angle))));
                float longWave = ValueNoise(waveP * (0.75 / max(_PrimaryWaveSize, 0.05)) + t * float2(0.36, 0.14));
                float shortWave = ValueNoise(waveP * (1.55 / max(_RippleSize, 0.05)) + t * _RippleSpeed * float2(-0.21, 0.27));
                float wave = lerp(longWave, shortWave, saturate(_RippleStrength));
                world.y += (wave * 2.0 - 1.0) * _WaveHeight;
                return world;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = ApplyWaves(TransformObjectToWorld(input.positionOS));
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 ShadeWater(float3 world, float2 uv)
            {
                float2 p = world.xz / max(_PatternScale, 0.05);
                float t = _Time.y * _WaveSpeed;
                float noise = ValueNoise(p * 1.67 + float2(t * 0.43, -t * 0.21));
                // Foam has its own coordinates and clock. The base water tint noise above stays independent.
                float2 foamP = p * _FoamScale;
                float foamTime = _Time.y * _FoamSpeed;
                float foamNoise = ValueNoise(foamP * 1.67 + float2(foamTime * 0.43, -foamTime * 0.21));
                float warpY = ValueNoise(foamP * 1.53 + float2(7.3, foamTime * 0.37));
                float randomness = saturate(_FoamRandomness);
                // A gentle rotation and stronger noise warp break up world-axis alignment.
                float angle = randomness * 0.47 + radians(_FoamCellRotation);
                float2 rotatedFoam = float2(dot(foamP, float2(cos(angle), -sin(angle))),
                                            dot(foamP, float2(sin(angle), cos(angle))));
                rotatedFoam.x /= max(_FoamCellStretch, 0.05);
                float2 warpedPosition = rotatedFoam + (float2(foamNoise, warpY) - 0.5)
                                                     * lerp(0.36, 0.96, randomness) * _FoamEdgeWaviness;
                float2 cell = floor(warpedPosition);
                float2 local = frac(warpedPosition);
                float nearest = 10.0;
                float second = 10.0;

                [unroll] for (int y = -1; y <= 1; y++)
                {
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 offset = float2(x, y);
                        float2 hashCell = cell + offset;
                        float2 seed = frac(sin(float2(dot(hashCell, float2(127.1, 311.7)),
                                                      dot(hashCell, float2(269.5, 183.3)))) * 43758.5453);
                        float2 orbit = sin(seed * 6.283185 + foamTime * float2(0.8, 1.1));
                        float2 scatter = (seed - 0.5) * 0.68 * randomness * _FoamCellScatter;
                        float2 motion = orbit * lerp(0.28, 0.12, randomness) * _FoamCellMovement;
                        // Keep centers inside their cells as scatter and movement are increased.
                        float2 jitter = clamp(scatter + motion, -0.48, 0.48);
                        float2 center = offset + 0.5 + jitter;
                        float2 delta = center - local;
                        float distanceSquared = dot(delta, delta);
                        second = min(second, max(nearest, distanceSquared));
                        nearest = min(nearest, distanceSquared);
                    }
                }

                float border = sqrt(second) - sqrt(nearest);
                float thicknessNoise = lerp(0.5, foamNoise, _FoamThicknessVariation);
                float lineWidth = lerp(0.025, 0.13, thicknessNoise) * _FoamRibbonThickness;
                float aa = max(fwidth(border) * 0.85, 0.005);
                float crests = 1.0 - smoothstep(max(0.0, lineWidth - aa), lineWidth + aa, border);

                float shore = 0.0;
                float shallows = 0.0;
                float rawDepth = SampleSceneDepth(uv);
#if UNITY_REVERSED_Z
                bool hasGeometry = rawDepth > 0.000001;
                float deviceDepth = rawDepth;
#else
                bool hasGeometry = rawDepth < 0.999999;
                float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
#endif
                if (hasGeometry)
                {
                    float3 submerged = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
                    float depth = max(0.0, world.y - submerged.y);
                    float width = max(_FoamWidth, 0.001);
                    float surge = ValueNoise(foamP * 2.15 + float2(-foamTime * 0.68, foamTime * 0.48));
                    float foamReach = max(0.0, _FoamWidth * _FoamAmount);
                    float foamEdge = foamReach * lerp(0.35, 1.30, surge);
                    float shoreAA = max(fwidth(depth), max(0.001, foamReach * 0.025));
                    float shoreBand = 1.0 - smoothstep(max(0.0, foamEdge - shoreAA), foamEdge + shoreAA, depth);
                    float edgeBreak = smoothstep(_FoamCutoff - 0.06, _FoamCutoff + 0.06, surge);
                    // Keep the waterline connected as the outer edge breaks up.
                    float nearContact = 1.0 - smoothstep(0.0, max(foamReach * 0.45, 0.001), depth);
                    float breakup = lerp(1.0, max(nearContact, edgeBreak), smoothstep(0.0, 0.12, _FoamCutoff));
                    shore = shoreBand * breakup * step(0.001, foamReach);
                    shallows = 1.0 - smoothstep(0.0, width * 5.0, depth);
                }

                float waterTint = saturate(shallows * 0.82 + smoothstep(0.18, 0.86, noise) * 0.24);
                float3 waterColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, waterTint);
                float foam = saturate(max(shore, crests * _SurfaceFoam) * _FoamColor.a);
                return half4(lerp(waterColor, _FoamColor.rgb, foam), lerp(saturate(_WaterOpacity), 1.0, foam));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                return ShadeWater(input.positionWS, uv);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
