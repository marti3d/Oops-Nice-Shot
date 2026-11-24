Shader "MiniGolf/PlacementIndicator"
{
 Properties
 {
  _BaseColor("Color", Color) = (1,1,1,1)
  _OccludedOpacity("Occluded Opacity Multiplier", Range(0,1)) = .25
  _Opacity("Opacity", Range(0,1)) = .85
  _ArrowCount("Arrow Count", Range(2,8)) = 4
  _Speed("Animation Speed", Float) = 1.5
  _ArrowSize("Arrow Size", Range(.02,.2)) = .10
  _InnerRadius("Inner Radius", Range(.05,.4)) = .16
  _OuterRadius("Outer Radius", Range(.1,.48)) = .39
  _Softness("Edge Softness", Range(.001,.03)) = .005
  _Pulse("Pulse Strength", Range(0,1)) = .2
  [Toggle] _CenterDot("Center Dot", Float) = 1
  [Toggle] _Ring("Outer Ring", Float) = 0
  [HideInInspector] _PlacementTint("Placement Tint", Color) = (1,1,1,1)
  [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest("Depth Test", Float) = 4
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest Always
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor, _PlacementTint;
   float _Opacity,_ArrowCount,_Speed,_ArrowSize,_InnerRadius,_OuterRadius,_Softness,_Pulse,_CenterDot,_Ring,_DepthTest,_OccludedOpacity;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
   Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
   half4 frag(Varyings i):SV_Target
   {
    float2 p=i.uv-.5;
    float n=max(2,round(_ArrowCount));
    float sector=6.2831853/n;
    float a=atan2(p.y,p.x);
    float angle=round(a/sector)*sector;
    float2 dir=float2(cos(angle),sin(angle));
    float t=frac(_Time.y*_Speed);
    float radius=lerp(_OuterRadius,_InnerRadius,t);
    float x=dot(p,dir)-radius;
    float y=abs(dot(p,float2(-dir.y,dir.x)));
    float s=max(.001,_ArrowSize);
    // Triangle tip points inward; short rectangular tail points outward.
    float tri=max(max(-x-s*.5,x),y-(x+s*.5)*1.05);
    float tail=max(max(-x,x-s*.45),y-s*.18);
    float aa=max(_Softness,fwidth(x));
    float arrow=(1-smoothstep(-aa,aa,min(tri,tail)))*smoothstep(0,.12,t)*(1-smoothstep(.78,1,t));
    float r=length(p);
    float dotMask=(1-smoothstep(.018,.018+aa,r))*_CenterDot;
    float ring=(1-smoothstep(.005,.005+aa,abs(r-min(.46,_OuterRadius+.06))))*_Ring;
    float mask=max(arrow,max(dotMask,ring*.65));
    float pulse=1-_Pulse*.5+_Pulse*.5*sin(_Time.y*5);
    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
    float sceneDepth = SampleSceneDepth(screenUV);
    #if UNITY_REVERSED_Z
      bool occluded = i.positionCS.z < sceneDepth - 0.000001;
    #else
      bool occluded = i.positionCS.z > sceneDepth + 0.000001;
    #endif
    float visibility = (occluded && _DepthTest < 7.5) ? _OccludedOpacity : 1.0;
    return half4(_BaseColor.rgb*_PlacementTint.rgb, saturate(mask*_Opacity*_BaseColor.a*_PlacementTint.a*pulse*visibility));
   }
   ENDHLSL
  }
 }
}
