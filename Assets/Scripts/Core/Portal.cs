using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 传送门核心组件。挂载在每个传送门 GameObject 上。
/// 负责：穿越检测与传送、递归渲染（recursive rendering）、斜视锥体裁剪（oblique near clip plane）、Slice Shader 参数管理。
/// Core portal component. Handles traveller detection/teleportation, recursive rendering,
/// oblique near-clip-plane projection, and Slice Shader parameter updates.
/// </summary>
public class Portal : MonoBehaviour {
    [Header ("Main Settings")]
    public Portal linkedPortal;
    public MeshRenderer screen;
    public int recursionLimit = 5;

    [Header ("Advanced Settings")]
    public float nearClipOffset = 0.05f;
    public float nearClipLimit = 0.2f;

    // Private variables
    RenderTexture viewTexture;
    Camera portalCam;
    Camera playerCam;
    Material firstRecursionMat;
    List<PortalTraveller> trackedTravellers;
    MeshFilter screenMeshFilter;

    /// <summary>
    /// 初始化引用：主摄像机、Portal 子摄像机、screen 的 MeshFilter，设置 displayMask=1。
    /// Initialize references: player camera, portal child camera, screen mesh filter, set displayMask=1.
    /// </summary>
    void Awake () {
        playerCam = Camera.main;
        portalCam = GetComponentInChildren<Camera> ();
        portalCam.enabled = false;
        trackedTravellers = new List<PortalTraveller> ();
        screenMeshFilter = screen.GetComponent<MeshFilter> ();
        screen.material.SetInt ("displayMask", 1);
    }

    /// <summary>
    /// 每帧 LateUpdate 中检测被追踪的 Traveller 是否穿越了传送门平面。
    /// Each frame, check whether any tracked traveller has crossed the portal plane.
    /// </summary>
    void LateUpdate () {
        HandleTravellers ();
    }

    /// <summary>
    /// 遍历所有 trackedTravellers，通过比较前后帧偏移向量与 forward 的点积符号判断穿越。
    /// 若穿越：执行 Teleport、更新 graphicsClone 位置、通知 linkedPortal 接管追踪。
    /// 若未穿越：更新 graphicsClone 位置使其始终出现在传送门另一侧。
    /// Iterate tracked travellers. Detect crossing by comparing the sign of dot(offset, forward)
    /// between current and previous frame. If crossed: teleport, update clone, hand off to linked portal.
    /// Otherwise: keep clone positioned on the other side of the portal.
    /// </summary>
    void HandleTravellers () {

        for (int i = 0; i < trackedTravellers.Count; i++) {
            PortalTraveller traveller = trackedTravellers[i];
            Transform travellerT = traveller.transform;
            // 计算 Traveller 在 linkedPortal 空间中的对应变换矩阵，用于放置 graphicsClone
            var m = linkedPortal.transform.localToWorldMatrix * transform.worldToLocalMatrix * travellerT.localToWorldMatrix;

            Vector3 offsetFromPortal = travellerT.position - transform.position;
            int portalSide = System.Math.Sign (Vector3.Dot (offsetFromPortal, transform.forward));
            int portalSideOld = System.Math.Sign (Vector3.Dot (traveller.previousOffsetFromPortal, transform.forward));
            // Teleport the traveller if it has crossed from one side of the portal to the other
            // 通过比较当前帧与上一帧的侧边符号判断是否穿越了传送门平面
            if (portalSide != portalSideOld) {
                var positionOld = travellerT.position;
                var rotOld = travellerT.rotation;
                traveller.Teleport (transform, linkedPortal.transform, m.GetColumn (3), m.rotation);
                traveller.graphicsClone.transform.SetPositionAndRotation (positionOld, rotOld);
                // Can't rely on OnTriggerEnter/Exit to be called next frame since it depends on when FixedUpdate runs
                // 不能依赖 OnTriggerEnter/Exit 在下一帧被调用，因为 FixedUpdate 的执行时机不确定
                linkedPortal.OnTravellerEnterPortal (traveller);
                trackedTravellers.RemoveAt (i);
                i--;

            } else {
                // 未穿越时持续更新 graphicsClone 到 linkedPortal 另一侧的正确位置
                traveller.graphicsClone.transform.SetPositionAndRotation (m.GetColumn (3), m.rotation);
                //UpdateSliceParams (traveller);
                traveller.previousOffsetFromPortal = offsetFromPortal;
            }
        }
    }

    // Called before any portal cameras are rendered for the current frame
    /// <summary>
    /// [渲染阶段一] 在当前帧所有 Portal Camera 渲染之前调用，更新 Slice Shader 裁剪参数。
    /// Phase 1 — called before any portal cameras render this frame. Updates slice shader params.
    /// </summary>
    public void PrePortalRender () {
        foreach (var traveller in trackedTravellers) {
            UpdateSliceParams (traveller);
        }
    }

    // Manually render the camera attached to this portal
    // Called after PrePortalRender, and before PostPortalRender
    /// <summary>
    /// [渲染阶段二] Portal 核心渲染。phase 2 — manually render the portal camera.
    /// 1. 跳过视野外的 linkedPortal  2. 创建 RenderTexture  3. 递归计算摄像机位置
    /// 4. 隐藏 screen，从最深层次反向渲染  5. 恢复 screen
    /// Steps: skip if not visible → create view texture → recursively compute virtual cam positions
    /// → hide screen → render from deepest recursion outward → restore screen.
    /// </summary>
    public void Render () {

        // Skip rendering the view from this portal if player is not looking at the linked portal
        // 如果 linkedPortal 屏幕不在玩家视野中，跳过渲染以节省性能
        if (!CameraUtility.VisibleFromCamera (linkedPortal.screen, playerCam)) {
            return;
        }

        CreateViewTexture ();

        var localToWorldMatrix = playerCam.transform.localToWorldMatrix;
        var renderPositions = new Vector3[recursionLimit];
        var renderRotations = new Quaternion[recursionLimit];

        int startIndex = 0;
        portalCam.projectionMatrix = playerCam.projectionMatrix;
        // 逐层累积变换矩阵，模拟光线在传送门间的多次反弹
        for (int i = 0; i < recursionLimit; i++) {
            if (i > 0) {
                // No need for recursive rendering if linked portal is not visible through this portal
                // 当前层看不到 linkedPortal 的 screen 时停止递归
                if (!CameraUtility.BoundsOverlap (screenMeshFilter, linkedPortal.screenMeshFilter, portalCam)) {
                    break;
                }
            }
            localToWorldMatrix = transform.localToWorldMatrix * linkedPortal.transform.worldToLocalMatrix * localToWorldMatrix;
            int renderOrderIndex = recursionLimit - i - 1;
            renderPositions[renderOrderIndex] = localToWorldMatrix.GetColumn (3);
            renderRotations[renderOrderIndex] = localToWorldMatrix.rotation;

            portalCam.transform.SetPositionAndRotation (renderPositions[renderOrderIndex], renderRotations[renderOrderIndex]);
            startIndex = renderOrderIndex;
        }

        // Hide screen so that camera can see through portal
        // 隐藏 screen 阴影并临时关闭 linkedPortal 画面（防止摄像机看到自己）
        screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        linkedPortal.screen.material.SetInt ("displayMask", 0);

        // 从最深层次反向渲染（内层画面先绘制到 RT）
        for (int i = startIndex; i < recursionLimit; i++) {
            portalCam.transform.SetPositionAndRotation (renderPositions[i], renderRotations[i]);
            SetNearClipPlane ();
            HandleClipping ();
            portalCam.Render ();

            if (i == startIndex) {
                linkedPortal.screen.material.SetInt ("displayMask", 1);
            }
        }

        // Unhide objects hidden at start of render
        // 恢复 screen 阴影投射
        screen.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
    }

    /// <summary>
    /// 处理 Slice Shader 裁剪时的两个视觉瑕疵，通过动态调节 sliceOffsetDst 修复。
    /// There are two main graphical issues when slicing travellers:
    /// 1. Tiny sliver of mesh drawn on backside of portal — 传送门背面的微小网格残片
    ///    Ideally the oblique clip plane would sort this out, but even with 0 offset, tiny sliver still visible
    /// 2. Tiny seam between the sliced mesh, and the rest of the model drawn onto the portal screen — 裁剪与非裁剪间的接缝
    /// This function tries to address these issues by modifying the slice parameters when rendering the view from the portal.
    /// Would be great if this could be fixed more elegantly, but this is the best I can figure out for now.
    /// </summary>
    void HandleClipping () {
        const float hideDst = -1000;
        const float showDst = 1000;
        float screenThickness = linkedPortal.ProtectScreenFromClipping (portalCam.transform.position);

        foreach (var traveller in trackedTravellers) {
            if (SameSideOfPortal (traveller.transform.position, portalCamPos)) {
                // Addresses issue 1 — 修复问题 1：隐藏本体背面残片
                traveller.SetSliceOffsetDst (hideDst, false);
            } else {
                // Addresses issue 2 — 修复问题 2：消除接缝
                traveller.SetSliceOffsetDst (showDst, false);
            }

            // Ensure clone is properly sliced, in case it's visible through this portal:
            // 确保 Clone 通过此传送门可见时也被正确裁剪
            int cloneSideOfLinkedPortal = -SideOfPortal (traveller.transform.position);
            bool camSameSideAsClone = linkedPortal.SideOfPortal (portalCamPos) == cloneSideOfLinkedPortal;
            if (camSameSideAsClone) {
                traveller.SetSliceOffsetDst (screenThickness, true);
            } else {
                traveller.SetSliceOffsetDst (-screenThickness, true);
            }
        }

        var offsetFromPortalToCam = portalCamPos - transform.position;
        // 处理 linkedPortal 中 Traveller 通过此传送门的显示
        foreach (var linkedTraveller in linkedPortal.trackedTravellers) {
            var travellerPos = linkedTraveller.graphicsObject.transform.position;
            var clonePos = linkedTraveller.graphicsClone.transform.position;
            // Handle clone of linked portal coming through this portal:
            // 处理 linkedPortal 的 Clone 通过此传送门的裁剪
            bool cloneOnSameSideAsCam = linkedPortal.SideOfPortal (travellerPos) != SideOfPortal (portalCamPos);
            if (cloneOnSameSideAsCam) {
                // Addresses issue 1
                linkedTraveller.SetSliceOffsetDst (hideDst, true);
            } else {
                // Addresses issue 2
                linkedTraveller.SetSliceOffsetDst (showDst, true);
            }

            // Ensure traveller of linked portal is properly sliced, in case it's visible through this portal:
            // 确保 linkedPortal 的 Traveller 本体通过此传送门可见时也被正确裁剪
            bool camSameSideAsTraveller = linkedPortal.SameSideOfPortal (linkedTraveller.transform.position, portalCamPos);
            if (camSameSideAsTraveller) {
                linkedTraveller.SetSliceOffsetDst (screenThickness, false);
            } else {
                linkedTraveller.SetSliceOffsetDst (-screenThickness, false);
            }
        }
    }

    // Called once all portals have been rendered, but before the player camera renders
    /// <summary>
    /// [渲染阶段三] 所有 Portal 渲染完成后、玩家摄像机渲染前调用。
    /// 再次更新 Slice 参数 + 调整 screen 厚度防止近裁剪面穿模。
    /// Phase 3 — called after all portals rendered, before player cam. Update slice params & protect screen.
    /// </summary>
    public void PostPortalRender () {
        foreach (var traveller in trackedTravellers) {
            UpdateSliceParams (traveller);
        }
        ProtectScreenFromClipping (playerCam.transform.position);
    }

    /// <summary>
    /// 创建或重建 Portal Camera 的 RenderTexture（分辨率变化时自动重建）。
    /// 将 Portal Camera 渲染目标绑定到 RT，并设为 linkedPortal.screen 的 _MainTex。
    /// Create/recreate view texture when screen resolution changes.
    /// Bind portal camera output to RT, display RT on linked portal's screen material.
    /// </summary>
    void CreateViewTexture () {
        if (viewTexture == null || viewTexture.width != Screen.width || viewTexture.height != Screen.height) {
            if (viewTexture != null) {
                viewTexture.Release ();
            }
            viewTexture = new RenderTexture (Screen.width, Screen.height, 0);
            // Render the view from the portal camera to the view texture
            // 将 Portal Camera 渲染输出到 RT
            portalCam.targetTexture = viewTexture;
            // Display the view texture on the screen of the linked portal
            // 将 RT 显示在配对传送门的屏幕上
            linkedPortal.screen.material.SetTexture ("_MainTex", viewTexture);
        }
    }

    // Sets the thickness of the portal screen so as not to clip with camera near plane when player goes through
    /// <summary>
    /// 动态调整传送门 screen 的厚度和位置，防止摄像机近裁剪面穿模。
    /// 返回 screen 厚度值供 HandleClipping 使用。
    /// Sets screen thickness so the camera near plane doesn't clip through when the player goes through the portal.
    /// </summary>
    float ProtectScreenFromClipping (Vector3 viewPoint) {
        float halfHeight = playerCam.nearClipPlane * Mathf.Tan (playerCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * playerCam.aspect;
        float dstToNearClipPlaneCorner = new Vector3 (halfWidth, halfHeight, playerCam.nearClipPlane).magnitude;
        float screenThickness = dstToNearClipPlaneCorner;

        Transform screenT = screen.transform;
        bool camFacingSameDirAsPortal = Vector3.Dot (transform.forward, transform.position - viewPoint) > 0;
        screenT.localScale = new Vector3 (screenT.localScale.x, screenT.localScale.y, screenThickness);
        screenT.localPosition = Vector3.forward * screenThickness * ((camFacingSameDirAsPortal) ? 0.5f : -0.5f);
        return screenThickness;
    }

    /// <summary>
    /// 根据玩家和 Traveller 的相对位置，计算并设置 Slice Shader 的 sliceNormal、sliceCentre、sliceOffsetDst。
    /// 同时更新本体材质和 Clone 材质。
    /// Calculate and apply slice shader parameters for both original and clone materials.
    /// </summary>
    void UpdateSliceParams (PortalTraveller traveller) {
        // Calculate slice normal
        // 计算本体裁剪法线（指向传送门前方一侧）
        int side = SideOfPortal (traveller.transform.position);
        Vector3 sliceNormal = transform.forward * -side;
        // 计算 Clone 裁剪法线（指向 linkedPortal 前方一侧）
        Vector3 cloneSliceNormal = linkedPortal.transform.forward * side;

        // Calculate slice centre
        // 裁剪平面中心分别位于各自的传送门位置
        Vector3 slicePos = transform.position;
        Vector3 cloneSlicePos = linkedPortal.transform.position;

        // Adjust slice offset so that when player standing on other side of portal to the object, the slice doesn't clip through
        // 根据玩家与 Traveller 的相对侧别调整裁剪偏移量
        float sliceOffsetDst = 0;
        float cloneSliceOffsetDst = 0;
        float screenThickness = screen.transform.localScale.z;

        bool playerSameSideAsTraveller = SameSideOfPortal (playerCam.transform.position, traveller.transform.position);
        if (!playerSameSideAsTraveller) {
            sliceOffsetDst = -screenThickness;
        }
        bool playerSameSideAsCloneAppearing = side != linkedPortal.SideOfPortal (playerCam.transform.position);
        if (!playerSameSideAsCloneAppearing) {
            cloneSliceOffsetDst = -screenThickness;
        }

        // Apply parameters
        // 将参数写入所有本体和 Clone 的材质
        for (int i = 0; i < traveller.originalMaterials.Length; i++) {
            traveller.originalMaterials[i].SetVector ("sliceCentre", slicePos);
            traveller.originalMaterials[i].SetVector ("sliceNormal", sliceNormal);
            traveller.originalMaterials[i].SetFloat ("sliceOffsetDst", sliceOffsetDst);

            traveller.cloneMaterials[i].SetVector ("sliceCentre", cloneSlicePos);
            traveller.cloneMaterials[i].SetVector ("sliceNormal", cloneSliceNormal);
            traveller.cloneMaterials[i].SetFloat ("sliceOffsetDst", cloneSliceOffsetDst);

        }

    }

    // Use custom projection matrix to align portal camera's near clip plane with the surface of the portal
    // Note that this affects precision of the depth buffer, which can cause issues with effects like screenspace AO
    /// <summary>
    /// 设置 Portal Camera 的斜视锥体裁剪矩阵（Oblique Near Clip Plane）。
    /// 使近裁剪面与传送门表面重合，避免渲染传送门背后的物体。
    /// 距离过近时（&lt; nearClipLimit）停用以避免深度精度问题导致的视觉瑕疵。
    /// Sets oblique near-clip-plane so the portal camera doesn't render objects behind the portal surface.
    /// Note: affects depth buffer precision — can cause issues with effects like screenspace AO.
    /// </summary>
    void SetNearClipPlane () {
        // Learning resource:
        // http://www.terathon.com/lengyel/Lengyel-Oblique.pdf
        Transform clipPlane = transform;
        int dot = System.Math.Sign (Vector3.Dot (clipPlane.forward, transform.position - portalCam.transform.position));

        Vector3 camSpacePos = portalCam.worldToCameraMatrix.MultiplyPoint (clipPlane.position);
        Vector3 camSpaceNormal = portalCam.worldToCameraMatrix.MultiplyVector (clipPlane.forward) * dot;
        float camSpaceDst = -Vector3.Dot (camSpacePos, camSpaceNormal) + nearClipOffset;

        // Don't use oblique clip plane if very close to portal as it seems this can cause some visual artifacts
        // 距离太近时不使用斜视锥体，避免精度问题导致视觉瑕疵
        if (Mathf.Abs (camSpaceDst) > nearClipLimit) {
            Vector4 clipPlaneCameraSpace = new Vector4 (camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDst);

            // Update projection based on new clip plane
            // Calculate matrix with player cam so that player camera settings (fov, etc) are used
            // 使用玩家摄像机的参数（FOV 等）计算投影矩阵
            portalCam.projectionMatrix = playerCam.CalculateObliqueMatrix (clipPlaneCameraSpace);
        } else {
            portalCam.projectionMatrix = playerCam.projectionMatrix;
        }
    }

    /// <summary>
    /// Traveller 进入传送门触发器范围时调用：创建 graphicsClone、记录初始偏移、加入追踪列表。
    /// Called when traveller first touches the portal threshold.
    /// </summary>
    void OnTravellerEnterPortal (PortalTraveller traveller) {
        if (!trackedTravellers.Contains (traveller)) {
            traveller.EnterPortalThreshold ();
            traveller.previousOffsetFromPortal = traveller.transform.position - transform.position;
            trackedTravellers.Add (traveller);
        }
    }

    /// <summary>
    /// Unity 触发器回调：碰撞体进入传送门范围。
    /// </summary>
    void OnTriggerEnter (Collider other) {
        var traveller = other.GetComponent<PortalTraveller> ();
        if (traveller) {
            OnTravellerEnterPortal (traveller);
        }
    }

    /// <summary>
    /// Unity 触发器回调：碰撞体离开传送门范围。清理 Clone、从追踪列表移除。
    /// </summary>
    void OnTriggerExit (Collider other) {
        var traveller = other.GetComponent<PortalTraveller> ();
        if (traveller && trackedTravellers.Contains (traveller)) {
            traveller.ExitPortalThreshold ();
            trackedTravellers.Remove (traveller);
        }
    }

    /*
     ** Some helper/convenience stuff:
     ** 辅助/便捷方法：
     */

    /// <summary>
    /// 判断世界坐标在传送门的哪一侧。返回 +1（前侧 forward side）或 -1（后侧 back side）。
    /// </summary>
    int SideOfPortal (Vector3 pos) {
        return System.Math.Sign (Vector3.Dot (pos - transform.position, transform.forward));
    }

    /// <summary>
    /// 判断两个世界坐标是否在传送门的同一侧。
    /// </summary>
    bool SameSideOfPortal (Vector3 posA, Vector3 posB) {
        return SideOfPortal (posA) == SideOfPortal (posB);
    }

    /// <summary>
    /// Portal Camera 的世界坐标（便捷属性）。
    /// </summary>
    Vector3 portalCamPos {
        get {
            return portalCam.transform.position;
        }
    }

    /// <summary>
    /// 编辑器校验：确保 linkedPortal 双向链接（A→B 则 B→A）。
    /// Editor-only: ensure bidirectional portal linking.
    /// </summary>
    void OnValidate () {
        if (linkedPortal != null) {
            linkedPortal.linkedPortal = this;
        }
    }
}
