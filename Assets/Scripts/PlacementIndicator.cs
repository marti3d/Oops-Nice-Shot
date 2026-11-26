using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(102)]
public sealed class PlacementIndicator : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Controller that supplies the preview and ground hit. If empty, finds one in the scene.")]
    [SerializeField]
    ObjectGizmoController gizmoController;
    [Tooltip("Camera used for constant screen sizing. Uses Main Camera if empty.")]
    [SerializeField]
    Camera targetCamera;

    [Header("Appearance")]
    [Tooltip("Material used to render the ground marker.")]
    [SerializeField]
    Material indicatorMaterial;
    [Tooltip("Tint the marker with the gizmo's current placement color.")]
    [SerializeField]
    bool usePlacementColor = true;
    [Tooltip("Ask the marker material to draw over other geometry.")]
    [SerializeField]
    bool alwaysOnTop = false;

    [Header("Surface Placement")]
    [Tooltip("Tilt the marker to match the ground. Turn off to keep it horizontal.")]
    [SerializeField]
    bool alignToSurface = true;
    [Tooltip("Distance to lift the marker off the surface to avoid flickering.")]
    [Min(.001f)]
    [SerializeField]
    float surfaceOffset = .025f;

    [Header("Indicator Size")]
    [Tooltip("Size the marker in world units, from the selected object, or in screen pixels.")]
    [SerializeField]
    IndicatorSizeMode sizeMode = IndicatorSizeMode.FixedWorldSize;
    [Tooltip("Width in Fixed World Size mode and the fallback when a measured size is unavailable.")]
    [Min(.01f)]
    [SerializeField]
    float indicatorSize = 1f;
    [Tooltip("Multiply the object's measured width in Adaptive mode.")]
    [Min(.01f)]
    [SerializeField]
    float sizeMultiplier = 1.15f;
    [Tooltip("Smallest marker width in Adaptive mode, in world units.")]
    [Min(.01f)]
    [SerializeField]
    float minimumSize = .2f;
    [Tooltip("Largest marker width in Adaptive mode, in world units.")]
    [Min(.01f)]
    [SerializeField]
    float maximumSize = 10f;
    [Tooltip("Target marker width in Constant Screen Size mode, in pixels.")]
    [Min(1)]
    [SerializeField]
    float screenPixels = 80;

    public enum IndicatorSizeMode
    {
        Adaptive,
        FixedWorldSize,
        ConstantScreenSize
    }
    GameObject quad;
    MeshRenderer visual;
    MaterialPropertyBlock block;
    Rigidbody previous;
    float cachedSize;
    void Awake()
    {
        if (!gizmoController)
            gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Placement Indicator Visual";
        quad.SetActive(false);
        var collider = quad.GetComponent<Collider>();
        if (collider)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        visual = quad.GetComponent<MeshRenderer>();
        visual.shadowCastingMode = ShadowCastingMode.Off;
        visual.receiveShadows = false;
        visual.sharedMaterial = indicatorMaterial;
        block = new MaterialPropertyBlock();
    }
    void LateUpdate()
    {
        var original = gizmoController ? gizmoController.SelectedObject : null;
        var preview = gizmoController ? gizmoController.PreviewTransform : null;
        if (!original || !preview || !indicatorMaterial || !gizmoController.HasGround)
        {
            quad.SetActive(false);
            previous = null;
            return;
        }
        var hit = gizmoController.GroundHit;
        if (previous != original)
        {
            previous = original;
            Bounds bounds = default;
            bool found = false;
            foreach (var r in original.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer))
                    continue;
                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }
            cachedSize = found ? Mathf.Max(bounds.size.x, bounds.size.z) : indicatorSize;
        }
        float size =
            sizeMode == IndicatorSizeMode.Adaptive
                ? Mathf.Clamp(cachedSize * sizeMultiplier, minimumSize, Mathf.Max(minimumSize, maximumSize))
                : Mathf.Max(.01f, indicatorSize);
        var cam = targetCamera ? targetCamera : Camera.main;
        if (sizeMode == IndicatorSizeMode.ConstantScreenSize && cam && cam.pixelHeight > 0)
        {
            float depth = Mathf.Max(cam.nearClipPlane,
                                    Vector3.Dot(hit.point - cam.transform.position, cam.transform.forward));
            size = screenPixels *
                   (cam.orthographic ? 2f * cam.orthographicSize
                                     : 2f * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f)) /
                   cam.pixelHeight;
        }
        Vector3 normal = alignToSurface ? hit.normal : Vector3.up;
        quad.transform.SetPositionAndRotation(hit.point + normal * surfaceOffset,
                                              Quaternion.FromToRotation(Vector3.forward, normal));
        quad.transform.localScale = new Vector3(size, size, 1);
        visual.sharedMaterial = indicatorMaterial;
        block.SetColor("_PlacementTint", usePlacementColor ? gizmoController.PreviewColor : Color.white);
        block.SetFloat("_DepthTest", alwaysOnTop ? 8 : 4);
        visual.SetPropertyBlock(block);
        quad.SetActive(true);
    }
    void OnDisable()
    {
        if (quad)
            quad.SetActive(false);
        previous = null;
    }
    void OnDestroy()
    {
        if (quad)
            Destroy(quad);
    }
}
