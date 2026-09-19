// 界面模糊 / A blurred backdrop for UI popups.
//
// URP 没有 GrabPass, 所以背景要自己截 / URP dropped GrabPass, so this shader cannot reach behind
// itself. UiBlurCapture renders the UI camera into a RenderTexture once when the popup opens and
// hands it over as _BlurTex; everything here does is blur that copy.
//
// _Size is the blur radius in pixels of the captured texture, which is what ItemInfoUI already
// animates from 0 to 160 when the panel fades in.
Shader "Arknights/UI/Blur"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _BlurTex ("Captured Backdrop", 2D) = "black" {}
        _Size ("Blur Radius (px)", Range(0, 300)) = 0
        _Color ("Tint", Color) = (1,1,1,1)

        // Canvas masks drive these; without them the popup ignores any Mask it sits inside.
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float4 screen : TEXCOORD0;
            };

            sampler2D _BlurTex;
            float4 _BlurTex_TexelSize;
            float _Size;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;

                // Screen UVs, not the quad's own: the backdrop has to line up with
                // where it was captured, whatever size the blur panel happens to be.
                o.screen = ComputeScreenPos(o.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.screen.xy / i.screen.w;

                // ItemInfoUI animates _Size from 0 to 160 in its own units, and the capture is
                // downsampled, so a texel is already several screen pixels wide. Taken at face
                // value that spreads 13 taps over a third of the screen and reads as ghosting
                // rather than blur; SPREAD maps the tween onto a radius that still looks soft.
                #define SPREAD 0.15
                float2 r = _BlurTex_TexelSize.xy * _Size * SPREAD;

                // 13 taps: centre plus two hexagonal rings, the outer one turned 30
                // degrees so the samples interleave instead of lining up into spokes.
                fixed4 sum = tex2D(_BlurTex, uv) * 0.2;

                sum += tex2D(_BlurTex, uv + float2( 0.500,  0.000) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2( 0.250,  0.433) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2(-0.250,  0.433) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2(-0.500,  0.000) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2(-0.250, -0.433) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2( 0.250, -0.433) * r) * 0.0667;

                sum += tex2D(_BlurTex, uv + float2( 0.866,  0.500) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2( 0.000,  1.000) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2(-0.866,  0.500) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2(-0.866, -0.500) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2( 0.000, -1.000) * r) * 0.0667;
                sum += tex2D(_BlurTex, uv + float2( 0.866, -0.500) * r) * 0.0667;

                // Alpha comes from the vertex colour so the CanvasGroup fade still
                // owns how the backdrop comes in; only the colour is ours.
                return fixed4(sum.rgb * i.color.rgb, i.color.a);
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
