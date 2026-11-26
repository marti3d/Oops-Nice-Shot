using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
[DefaultExecutionOrder(102)]
public class ShadowFollow : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Controller that supplies the ghost preview and ground hit. If empty, finds one in the scene.")]
    [SerializeField]
    private ObjectGizmoController gizmoController;

    [Header("Surface Projection")]
    [Tooltip("Depth of the decal projector. At runtime it is clamped between 0.001 and 0.05 world units.")]
    [Min(.001f)]
    [SerializeField]
    private float projectionDepth = .02f;
    [Tooltip("Offset the shadow along the ground normal, in world units.")]
    [SerializeField]
    private float verticalOffset = 0f;

    [Header("Shadow Footprint")]
    [Tooltip("Scale the shadow relative to the ghost's projected footprint.")]
    [SerializeField]
    private float sizeMultiplier = 1.15f;
    [Tooltip("Minimum shadow width and height in world units.")]
    [SerializeField]
    private float minimumSize = 0.2f;
    [Tooltip("Use the larger footprint dimension for both axes to keep the decal's aspect ratio square.")]
    [SerializeField]
    private bool forceCircular = false;

    private DecalProjector projector;
    private MeshRenderer[] renderers;
    private Transform lastPreview;

    private void Awake()
    {
        if (!gizmoController)
            gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        projector = GetComponent<DecalProjector>();
        projector.enabled = false;
    }

    private void LateUpdate()
    {
        Transform preview = gizmoController ? gizmoController.PreviewTransform : null;
        if (!preview || !gizmoController.HasGround)
        {
            projector.enabled = false;
            return;
        }
        if (lastPreview != preview)
        {
            lastPreview = preview;
            renderers = preview.GetComponentsInChildren<MeshRenderer>();
        }
        var hit = gizmoController.GroundHit;
        Vector3 up = Vector3.ProjectOnPlane(preview.forward, hit.normal);
        if (up.sqrMagnitude < .0001f)
            up = Vector3.ProjectOnPlane(preview.up, hit.normal);
        Quaternion rotation = Quaternion.LookRotation(-hit.normal, up);
        Quaternion inverse = Quaternion.Inverse(rotation);
        Bounds footprint = default;
        bool found = false;
        foreach (var renderer in renderers)
        {
            if (!renderer || !renderer.enabled)
                continue;
            Bounds local = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                var signs = new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1);
                var corner =
                    renderer.transform.TransformPoint(local.center + Vector3.Scale(local.extents, signs));
                var point = inverse * (corner - hit.point);
                if (found)
                    footprint.Encapsulate(point);
                else
                    footprint = new Bounds(point, Vector3.zero);
                found = true;
            }
        }
        float width = Mathf.Max(minimumSize, footprint.size.x * sizeMultiplier);
        float height = Mathf.Max(minimumSize, footprint.size.y * sizeMultiplier);
        if (forceCircular)
            width = height = Mathf.Max(width, height);
        projector.size = new Vector3(width, height, Mathf.Clamp(projectionDepth, .001f, .05f));
        projector.pivot = Vector3.zero;
        var center = new Vector3(footprint.center.x, footprint.center.y, 0);
        transform.SetPositionAndRotation(hit.point + rotation * center + hit.normal * verticalOffset,
                                         rotation);
        projector.enabled = found;
    }

    private void OnDisable()
    {
        if (projector)
            projector.enabled = false;
        lastPreview = null;
    }
}
