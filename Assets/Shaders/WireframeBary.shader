// Barycentric wireframe -- WebGL2 / GLES3 safe (no geometry shader). The mesh
// has unwelded triangles whose vertex COLOR carries barycentric coordinates
// (see BMeshBoneExtensions.BuildWireframeMesh); the fragment stage draws the
// edges from the screen-space derivative of those coords and discards the
// interior. Runs on a SkinnedMeshRenderer -- Unity skins POSITION before the
// vertex stage, same as Custom/TriplanarCreature.
Shader "Custom/WireframeBary"
{
    Properties
    {
        _WireColor ("Wire Color", Color) = (0.35, 0.85, 1, 1)
        _Thickness ("Thickness", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _WireColor;
            float _Thickness;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 bary : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.bary = v.color.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // max() guards paths where fwidth resolves to 0 (which would make
                // the wire infinitely thin -> invisible).
                float3 fw = max(fwidth(i.bary), 0.0001);
                float3 e = smoothstep((float3)0.0, fw * _Thickness, i.bary);
                float wire = 1.0 - min(min(e.x, e.y), e.z);
                clip(wire - 0.02);
                return fixed4(_WireColor.rgb, wire * _WireColor.a);
            }
            ENDCG
        }
    }
    FallBack Off
}
