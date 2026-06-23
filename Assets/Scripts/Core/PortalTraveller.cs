using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可穿越传送门的对象基类。管理 graphicsClone（视觉克隆体）生命周期、材质收集、Slice Shader 参数设置。
/// 子类通过覆写 Teleport() 处理额外的物理量变换（速度、角速度等）。
/// Base class for objects that can travel through portals.
/// Manages the graphics clone lifecycle, material collection, and slice shader parameters.
/// Subclasses override Teleport() to handle additional physics quantities (velocity, angular velocity).
/// </summary>
public class PortalTraveller : MonoBehaviour {

    public GameObject graphicsObject;
    /// <summary>在传送门另一侧显示的视觉克隆体，配合 Slice Shader 实现"半身在门外"效果</summary>
    public GameObject graphicsClone { get; set; }
    /// <summary>上一帧相对传送门的偏移，用于穿越检测 / previous frame's offset from portal, used for crossing detection</summary>
    public Vector3 previousOffsetFromPortal { get; set; }

    public Material[] originalMaterials { get; set; }
    public Material[] cloneMaterials { get; set; }

    /// <summary>
    /// 传送该对象到目标位置和旋转。虚方法，子类可覆写以额外处理速度/角速度的空间变换。
    /// Teleport to target position/rotation. Virtual — override to transform velocity/angular velocity.
    /// </summary>
    public virtual void Teleport (Transform fromPortal, Transform toPortal, Vector3 pos, Quaternion rot) {
        transform.position = pos;
        transform.rotation = rot;
    }

    // Called when first touches portal
    /// <summary>
    /// 首次接触传送门时调用。创建 graphicsClone（或重新激活），收集本体和 Clone 的全部材质。
    /// Called when first touching a portal. Instantiates (or reactivates) graphicsClone and collects all materials.
    /// </summary>
    public virtual void EnterPortalThreshold () {
        if (graphicsClone == null) {
            graphicsClone = Instantiate (graphicsObject);
            graphicsClone.transform.parent = graphicsObject.transform.parent;
            graphicsClone.transform.localScale = graphicsObject.transform.localScale;
            originalMaterials = GetMaterials (graphicsObject);
            cloneMaterials = GetMaterials (graphicsClone);
        } else {
            graphicsClone.SetActive (true);
        }
    }

    // Called once no longer touching portal (excluding when teleporting)
    /// <summary>
    /// 离开传送门范围时调用。隐藏 Clone，并将所有本体材质的 sliceNormal 重置以禁用裁剪。
    /// Called when no longer touching any portal. Deactivates clone and resets sliceNormal to disable slicing.
    /// </summary>
    public virtual void ExitPortalThreshold () {
        graphicsClone.SetActive (false);
        // Disable slicing
        // 禁用 Slice Shader 裁剪效果
        for (int i = 0; i < originalMaterials.Length; i++) {
            originalMaterials[i].SetVector ("sliceNormal", Vector3.zero);
        }
    }

    /// <summary>
    /// 设置 Slice Shader 的裁剪偏移距离。
    /// Set the slice offset distance on clone or original materials.
    /// </summary>
    /// <param name="dst">偏移距离 — 正值使更多网格可见 / positive = more visible</param>
    /// <param name="clone">true=设 Clone 材质，false=设本体材质</param>
    public void SetSliceOffsetDst (float dst, bool clone) {
        for (int i = 0; i < originalMaterials.Length; i++) {
            if (clone) {
                cloneMaterials[i].SetFloat ("sliceOffsetDst", dst);
            } else {
                originalMaterials[i].SetFloat ("sliceOffsetDst", dst);
            }

        }
    }

    /// <summary>
    /// 递归收集 GameObject 及其子对象中所有 MeshRenderer 的 Material 实例。
    /// Collect all Material instances from every MeshRenderer in the GameObject hierarchy.
    /// </summary>
    Material[] GetMaterials (GameObject g) {
        var renderers = g.GetComponentsInChildren<MeshRenderer> ();
        var matList = new List<Material> ();
        foreach (var renderer in renderers) {
            foreach (var mat in renderer.materials) {
                matList.Add (mat);
            }
        }
        return matList.ToArray ();
    }
}
