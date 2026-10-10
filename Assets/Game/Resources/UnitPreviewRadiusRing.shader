Shader "Hidden/WarSandbox/UnitPreviewRadiusRing"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            // Dedicated opaque ink into the preview RT, then RawImage composites it.
            // Respect character depth; no screen-space ellipse or soft shadow reuse.
            Cull Off ZWrite Off ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 position : POSITION) : SV_POSITION { return UnityObjectToClipPos(position); }
            float4 frag() : SV_Target { return float4(.23, .065, .004, 1.0); }
            ENDHLSL
        }
    }
}
