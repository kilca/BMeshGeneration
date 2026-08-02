// Triplanar surface shader: blends the same tiling texture from 3 projections
// (X/Y/Z) weighted by the surface normal. Used for procedurally generated
// creatures because their mesh has no UVs (convex-hull junctions between
// limbs have no consistent parametrization to unwrap).
//
// Projects from a baked-in rest position (uv2/uv3, see BMesh.BakeRestPositionUVs)
// rather than the live worldPos: for an animated (skinned) creature, worldPos
// moves every frame as bones sway, which made the texture visibly slide/swim
// across the surface instead of staying glued to it. uv2/uv3 hold each
// vertex's own pre-skinning local position, which skinning never touches, so
// sampling from it stays stable regardless of the current pose.
Shader "Custom/TriplanarCreature"
{
    Properties
    {
        _MainTex ("Skin Texture", 2D) = "white" {}
        _TexScale ("Texture Scale", Float) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 0.3
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _BlendSharpness ("Triplanar Blend Sharpness", Range(1,16)) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        float _TexScale;
        half _Glossiness;
        half _Metallic;
        float _BlendSharpness;

        struct Input
        {
            float3 worldNormal;
            float3 restPos;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            // uv2 (TEXCOORD1) = restPos.xy, uv3 (TEXCOORD2).x = restPos.z --
            // split across two channels since the legacy Vector2 UV accessors
            // used to bake this only carry 2 components each.
            o.restPos = float3(v.texcoord1.xy, v.texcoord2.x);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 blend = pow(abs(IN.worldNormal), _BlendSharpness);
            blend /= max(blend.x + blend.y + blend.z, 1e-5);

            float3 p = IN.restPos * _TexScale;
            fixed4 col =
                tex2D(_MainTex, p.yz) * blend.x +
                tex2D(_MainTex, p.xz) * blend.y +
                tex2D(_MainTex, p.xy) * blend.z;

            o.Albedo = col.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = col.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
