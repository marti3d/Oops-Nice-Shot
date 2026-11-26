Shader "Gizmo/Ghost Ground Line Overlay"
{
    Properties
    {
        _BaseMap ("Dash Pattern", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CompareFunction)] _DepthTest ("Depth Test", Float) = 8
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest [_DepthTest]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;

            struct Vertex
            {
                float4 position : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct Fragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            Fragment vert(Vertex input)
            {
                Fragment output;
                output.position = UnityObjectToClipPos(input.position);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            fixed4 frag(Fragment input) : SV_Target
            {
                return input.color * tex2D(_BaseMap, input.uv);
            }
            ENDCG
        }
    }
}
