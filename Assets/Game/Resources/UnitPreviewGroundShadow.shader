Shader "Hidden/WarSandbox/UnitPreviewGroundShadow"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" }
        Pass
        {
            // First draw into a cleared, transparent preview target. Write straight RGB
            // and alpha directly: RawImage performs the only alpha blend onto the UI.
            Cull Off ZWrite Off ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 position : POSITION; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Output vert(Input v) { Output o; o.position = UnityObjectToClipPos(v.position); o.uv = v.uv * 2 - 1; return o; }
            float4 frag(Output i) : SV_Target
            {
                float falloff = saturate(1 - dot(i.uv, i.uv));
                return float4(.18, .25, .29, .45 * falloff * falloff);
            }
            ENDHLSL
        }
    }
}
