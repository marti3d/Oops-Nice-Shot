Shader "MiniGolf/GizmoAlwaysOnTopSoft" {
 Properties { _BaseColor("Color", Color) = (1,1,1,1) }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+100" "RenderType"="Transparent" }
 Pass {
 ZTest Always ZWrite Off Cull Back Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;
 CBUFFER_END
 struct A { float4 positionOS : POSITION; };
 struct V { float4 positionCS : SV_POSITION; };
 V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); return o; }
 half4 frag(V i) : SV_Target { return _BaseColor; }
 ENDHLSL
 }
 }
}
