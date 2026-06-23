using UnityEngine;

/// <summary>
/// 静态工具类，提供摄像机空间计算的辅助方法：视锥可见性检测、屏幕空间包围盒、屏幕空间重叠判断。
/// 主要用于 Portal 渲染管线中的视锥剔除和递归终止判断。
/// Static utility for camera-space math: frustum visibility, screen-space bounds, overlap tests.
/// Used by the portal rendering pipeline for frustum culling and recursion termination.
/// </summary>
public static class CameraUtility {
    /// <summary>包围盒 8 个角点的偏移量（相对中心）/ corner offsets from a cube's center</summary>
    static readonly Vector3[] cubeCornerOffsets = {
        new Vector3 (1, 1, 1),
        new Vector3 (-1, 1, 1),
        new Vector3 (-1, -1, 1),
        new Vector3 (-1, -1, -1),
        new Vector3 (-1, 1, -1),
        new Vector3 (1, -1, -1),
        new Vector3 (1, 1, -1),
        new Vector3 (1, -1, 1),
    };

    // http://wiki.unity3d.com/index.php/IsVisibleFrom
    /// <summary>
    /// 检测 Renderer 是否在指定摄像机的视锥体内。
    /// Returns true if the renderer is within the camera's frustum planes.
    /// </summary>
    public static bool VisibleFromCamera (Renderer renderer, Camera camera) {
        Plane[] frustumPlanes = GeometryUtility.CalculateFrustumPlanes (camera);
        return GeometryUtility.TestPlanesAABB (frustumPlanes, renderer.bounds);
    }

    /// <summary>
    /// 判断两个 MeshFilter 在屏幕空间是否重叠，且 farObject 确实比 nearObject 更远。
    /// 用于递归渲染时判断 linkedPortal 是否透过本 Portal 可见（不可见则停止递归）。
    /// Returns true if the two mesh filters overlap on screen AND farObject is behind nearObject.
    /// Used to decide whether to continue recursive rendering.
    /// </summary>
    public static bool BoundsOverlap (MeshFilter nearObject, MeshFilter farObject, Camera camera) {

        var near = GetScreenRectFromBounds (nearObject, camera);
        var far = GetScreenRectFromBounds (farObject, camera);

        // ensure far object is indeed further away than near object
        // 确保 far 确实比 near 更远离摄像机
        if (far.zMax > near.zMin) {
            // Doesn't overlap on x axis — X 轴不重叠
            if (far.xMax < near.xMin || far.xMin > near.xMax) {
                return false;
            }
            // Doesn't overlap on y axis — Y 轴不重叠
            if (far.yMax < near.yMin || far.yMin > near.yMax) {
                return false;
            }
            // Overlaps — 屏幕上重叠
            return true;
        }
        return false;
    }

    // With thanks to http://www.turiyaware.com/a-solution-to-unitys-camera-worldtoscreenpoint-causing-ui-elements-to-display-when-object-is-behind-the-camera/
    /// <summary>
    /// 计算 MeshFilter 在摄像机屏幕空间的包围矩形（视口坐标）。
    /// 正确处理了摄像机背后的角点（clamp 到屏幕对面边缘）。
    /// Computes the screen-space bounding rectangle of a mesh filter in viewport coordinates.
    /// Correctly handles corners behind the camera by clamping to the opposite screen edge.
    /// </summary>
    public static MinMax3D GetScreenRectFromBounds (MeshFilter renderer, Camera mainCamera) {
        MinMax3D minMax = new MinMax3D (float.MaxValue, float.MinValue);

        Vector3[] screenBoundsExtents = new Vector3[8];
        var localBounds = renderer.sharedMesh.bounds;
        bool anyPointIsInFrontOfCamera = false;

        for (int i = 0; i < 8; i++) {
            Vector3 localSpaceCorner = localBounds.center + Vector3.Scale (localBounds.extents, cubeCornerOffsets[i]);
            Vector3 worldSpaceCorner = renderer.transform.TransformPoint (localSpaceCorner);
            Vector3 viewportSpaceCorner = mainCamera.WorldToViewportPoint (worldSpaceCorner);

            if (viewportSpaceCorner.z > 0) {
                anyPointIsInFrontOfCamera = true;
            } else {
                // If point is behind camera, it gets flipped to the opposite side
                // So clamp to opposite edge to correct for this
                // 角点在摄像机背后时，Unity 的 WorldToViewportPoint 会将其翻转到对面
                // 因此 clamp 到对面边缘以修正
                viewportSpaceCorner.x = (viewportSpaceCorner.x <= 0.5f) ? 1 : 0;
                viewportSpaceCorner.y = (viewportSpaceCorner.y <= 0.5f) ? 1 : 0;
            }

            // Update bounds with new corner point
            // 用新角点扩展包围盒
            minMax.AddPoint (viewportSpaceCorner);
        }

        // All points are behind camera so just return empty bounds
        // 所有角点都在摄像机背后，返回空包围盒
        if (!anyPointIsInFrontOfCamera) {
            return new MinMax3D ();
        }

        return minMax;
    }

    /// <summary>
    /// 三维包围盒结构体，在视口空间中累积物体的屏幕范围。
    /// 3D bounding box struct for accumulating screen-space bounds in viewport coordinates.
    /// </summary>
    public struct MinMax3D {
        public float xMin;
        public float xMax;
        public float yMin;
        public float yMax;
        public float zMin;
        public float zMax;

        public MinMax3D (float min, float max) {
            this.xMin = min;
            this.xMax = max;
            this.yMin = min;
            this.yMax = max;
            this.zMin = min;
            this.zMax = max;
        }

        /// <summary>
        /// 扩展包围盒以包含新点。Expands bounds to include the given point.
        /// </summary>
        public void AddPoint (Vector3 point) {
            xMin = Mathf.Min (xMin, point.x);
            xMax = Mathf.Max (xMax, point.x);
            yMin = Mathf.Min (yMin, point.y);
            yMax = Mathf.Max (yMax, point.y);
            zMin = Mathf.Min (zMin, point.z);
            zMax = Mathf.Max (zMax, point.z);
        }
    }

}
