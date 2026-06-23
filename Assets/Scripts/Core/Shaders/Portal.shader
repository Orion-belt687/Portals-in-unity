// ============================================================
// Portal.shader — 传送门屏幕着色器
// Portal screen shader.
//
// 将 Portal Camera 渲染的 RenderTexture 显示在传送门平面上。
// Displays the portal camera's RenderTexture on the portal screen plane.
// 通过 displayMask 控制显示 RT 画面还是兜底颜色，
// displayMask toggles between the rendered texture and fallback color,
// 防止 Portal Camera 渲染时看到自己的屏幕导致递归闪烁。
// to prevent the portal camera from seeing its own screen during recursive rendering.
// ============================================================
Shader "Custom/Portal"
{
    Properties
    {
        _InactiveColour ("Inactive Colour", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;   // 屏幕空间坐标，用于采样 RT
            };

            sampler2D _MainTex;                 // Portal Camera 渲染的 RenderTexture
            float4 _InactiveColour;             // displayMask=0 时的兜底颜色 / fallback color
            // set to 1 to display texture, otherwise will draw test colour
            // 设为 1 显示 RT 画面，否则显示 _InactiveColour
            int displayMask;


            /// 顶点着色器：标准 MVP 变换 + 计算屏幕空间坐标。
            /// Vertex shader: standard MVP transform + screen-space UV calculation.
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            /// 片元着色器：从屏幕坐标采样 RT，通过 displayMask 在 RT 与兜底颜色间切换。
            /// Fragment shader: sample RT at screen UV, toggle between RT and fallback color via displayMask.
            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                fixed4 portalCol = tex2D(_MainTex, uv);
                return portalCol * displayMask + _InactiveColour * (1-displayMask);
            }
            ENDCG
        }
    }
    Fallback "Standard" // for shadows
}
