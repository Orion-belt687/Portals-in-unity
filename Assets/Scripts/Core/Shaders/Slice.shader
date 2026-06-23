// ============================================================
// Slice.shader — 世界空间平面裁剪着色器
// World-space plane-slicing shader.
//
// 基于世界坐标的平面裁剪，用于物体穿越传送门时的"半身裁剪"效果。
// World-space clipping for the "half-body slice" effect when objects pass through portals.
// 由 Portal.cs 每帧更新 sliceNormal、sliceCentre、sliceOffsetDst，
// These three params are updated every frame by Portal.cs,
// 使裁剪平面一侧的像素被 clip() 丢弃，另一侧正常渲染。
// pixels on one side of the slice plane are discarded by clip().
// ============================================================
Shader "Custom/Slice"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        // 以下三个参数由 Portal.cs 每帧动态设置 / set dynamically by Portal.cs each frame:
        // World space normal of slice, anything along this direction from centre will be invisible
        // 裁剪平面法线（世界空间），沿此方向从中心向外的像素将被丢弃
        sliceNormal("normal", Vector) = (0,0,0,0)
        // World space centre of slice
        // 裁剪平面中心（世界空间）
        sliceCentre ("centre", Vector) = (0,0,0,0)
        // Increasing makes more of the mesh visible, decreasing makes less of the mesh visible
        // 偏移距离 — 正值使更多网格可见，负值使更少可见
        sliceOffsetDst("offset", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "IgnoreProjector" = "True"  "RenderType"="Geometry" }
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        // 基于物理的标准光照模型，启用所有灯光类型的阴影
        #pragma surface surf Standard addshadow
        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        sampler2D _MainTex;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;    // 世界空间坐标，用于裁剪平面判断
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        // World space normal of slice, anything along this direction from centre will be invisible
        float3 sliceNormal;
        // World space centre of slice
        float3 sliceCentre;
        // Increasing makes more of the mesh visible, decreasing makes less of the mesh visible
        float sliceOffsetDst;

        /// 表面着色器：在标准 PBR 之前通过 clip() 实现世界空间平面裁剪。
        /// Surface shader: world-space plane clipping via clip() before standard PBR.
        /// 裁剪逻辑：dot(worldPos - adjustedCentre, sliceNormal) < 0 则丢弃该像素。
        /// Clipping logic: pixels behind the plane (negative dot) are discarded.
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // 应用偏移量后的裁剪中心
            float3 adjustedCentre = sliceCentre + sliceNormal * sliceOffsetDst;
            // 像素到裁剪平面的有向向量
            float3 offsetToSliceCentre = adjustedCentre - IN.worldPos;
            // 核心裁剪：dot 为负表示像素在平面后方，丢弃该像素
            clip (dot(offsetToSliceCentre, sliceNormal));

            // Albedo comes from a texture tinted by color
            // 标准 PBR：Albedo 来自纹理 × 颜色
            fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;

            // Metallic and smoothness come from slider variables
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "VertexLit"
}
