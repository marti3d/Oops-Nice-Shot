using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(102)]
public sealed class PlacementIndicator : MonoBehaviour
{
    [SerializeField] ObjectGizmoController gizmoController;
    [SerializeField] GhostGroundLine groundLine;
    [SerializeField] Material indicatorMaterial;
    public enum IndicatorSizeMode { Adaptive, FixedWorldSize, ConstantScreenSize }
    [SerializeField] IndicatorSizeMode sizeMode = IndicatorSizeMode.FixedWorldSize;
    [SerializeField] bool alignToSurface = true, usePlacementColor = true, alwaysOnTop = false;
    [Min(.01f)][SerializeField] float indicatorSize = 1f, sizeMultiplier = 1.15f, minimumSize = .2f, maximumSize = 10f;
    [Min(.001f)][SerializeField] float surfaceOffset = .025f;
    [SerializeField] Camera targetCamera;
    [Min(1)][SerializeField] float screenPixels = 80;
    GameObject quad;
    MeshRenderer visual;
    MaterialPropertyBlock block;
    Rigidbody previous;
    float cachedSize;
    void Awake()
    {
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Placement Indicator Visual";
        quad.SetActive(false);
        var collider = quad.GetComponent<Collider>();
        if (collider) { collider.enabled = false; Destroy(collider); }
        visual = quad.GetComponent<MeshRenderer>();
        visual.shadowCastingMode = ShadowCastingMode.Off;
        visual.receiveShadows = false;
        visual.sharedMaterial = indicatorMaterial;
        block = new MaterialPropertyBlock();
    }
    void LateUpdate()
    {
        if (!gizmoController) gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        if (!groundLine) groundLine = FindAnyObjectByType<GhostGroundLine>();
        var original = gizmoController ? gizmoController.SelectedObject : null;
        var preview = gizmoController ? gizmoController.PreviewTransform : null;
        if (!original || !preview || !groundLine || !indicatorMaterial || !groundLine.TryGround(preview, original, out _, out var hit))
        { quad.SetActive(false); previous = null; return; }
        if (previous != original)
        {
            previous = original;
            Bounds bounds = default; bool found = false;
            foreach (var r in original.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (!found) { bounds = r.bounds; found = true; } else bounds.Encapsulate(r.bounds);
            }
            cachedSize = found ? Mathf.Max(bounds.size.x, bounds.size.z) : indicatorSize;
        }
        float size = sizeMode == IndicatorSizeMode.Adaptive
            ? Mathf.Clamp(cachedSize * sizeMultiplier, minimumSize, Mathf.Max(minimumSize, maximumSize))
            : Mathf.Max(.01f, indicatorSize);
        var cam = targetCamera ? targetCamera : Camera.main;
        if (sizeMode == IndicatorSizeMode.ConstantScreenSize && cam && cam.pixelHeight > 0)
        {
            float depth = Mathf.Max(cam.nearClipPlane, Vector3.Dot(hit.point - cam.transform.position, cam.transform.forward));
            size = screenPixels * (cam.orthographic ? 2f * cam.orthographicSize : 2f * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f)) / cam.pixelHeight;
        }
        Vector3 normal = alignToSurface ? hit.normal : Vector3.up;
        quad.transform.SetPositionAndRotation(hit.point + normal * surfaceOffset, Quaternion.FromToRotation(Vector3.forward, normal));
        quad.transform.localScale = new Vector3(size, size, 1);
        visual.sharedMaterial = indicatorMaterial;
        block.SetColor("_PlacementTint", usePlacementColor ? gizmoController.PreviewColor : Color.white);
        block.SetFloat("_DepthTest", alwaysOnTop ? 8 : 4);
        visual.SetPropertyBlock(block);
        quad.SetActive(true);
    }
    void OnDisable() { if (quad) quad.SetActive(false); previous = null; }
    void OnDestroy() { if (quad) Destroy(quad); }
}
