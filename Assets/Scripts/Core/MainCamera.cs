using UnityEngine;

/// <summary>
/// 挂载在主摄像机上，驱动所有 Portal 的三阶段渲染管线。
/// 利用 OnPreCull（剔除前回调）在正常场景渲染前完成传送门视图渲染。
/// Drives the 3-phase portal render pipeline via OnPreCull (before Unity culling).
/// Renders all portal views to RenderTexture before the main camera renders the scene.
/// </summary>
public class MainCamera : MonoBehaviour {

    Portal[] portals;

    /// <summary>
    /// 查找场景中所有 Portal 组件。
    /// Find all Portal components in the scene.
    /// </summary>
    void Awake () {
        portals = FindObjectsOfType<Portal> ();
    }

    /// <summary>
    /// Unity 摄像机剔除前回调。按顺序驱动所有传送门的三阶段：
    ///   阶段一 PrePortalRender  → 更新 Slice Shader 参数
    ///   阶段二 Render            → 核心渲染（递归计算、渲染到 RT）
    ///   阶段三 PostPortalRender  → 清理和 screen 防穿模
    /// 三个阶段分别循环而非合并，确保所有 Portal 的同一阶段先完成再进入下一阶段。
    /// Unity callback before culling. Drives 3 phases sequentially across all portals:
    ///   Phase 1 PrePortalRender  → update slice shader params
    ///   Phase 2 Render            → core rendering (recursive positions, render to RT)
    ///   Phase 3 PostPortalRender  → cleanup & protect screen from clipping
    /// Separate loops ensure all portals finish each phase before the next begins.
    /// </summary>
    void OnPreCull () {

        for (int i = 0; i < portals.Length; i++) {
            portals[i].PrePortalRender ();
        }
        for (int i = 0; i < portals.Length; i++) {
            portals[i].Render ();
        }

        for (int i = 0; i < portals.Length; i++) {
            portals[i].PostPortalRender ();
        }

    }

}
