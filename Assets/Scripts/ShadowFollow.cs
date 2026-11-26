using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
[DefaultExecutionOrder(102)]
public class ShadowFollow : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField]
    private ObjectGizmoController gizmoController;

    [Min(.001f)]
    [SerializeField]
    private float projectionDepth = .02f;

    [Header("Shadow Settings")]
    [SerializeField]
    private float verticalOffset = 0f;
    [SerializeField]
    private float sizeMultiplier = 1.15f;
    [SerializeField]
    private float minimumSize = 0.2f;
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
