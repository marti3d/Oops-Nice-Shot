using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
[DefaultExecutionOrder(102)]
public class ShadowFollow : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private ObjectGizmoController gizmoController;

    [SerializeField] private GhostGroundLine groundLine;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float rayDistance = 100f;
    [Min(.001f)][SerializeField] private float projectionDepth = .02f;

    [Header("Shadow Settings")]
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private float sizeMultiplier = 1.15f;
    [SerializeField] private float minimumSize = 0.2f;
    [SerializeField] private bool forceCircular = false;

    private DecalProjector projector;
    private Rigidbody currentTarget;
    private Vector3 localCenter;

    private void Awake()
    {
        projector = GetComponent<DecalProjector>();
        projector.enabled = false;
    }

    private void LateUpdate()
    {
        if (!gizmoController) gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        if (!groundLine) groundLine = FindAnyObjectByType<GhostGroundLine>();
        Rigidbody selected = gizmoController != null
            ? gizmoController.SelectedObject
            : null;

        Transform preview = gizmoController != null ? gizmoController.PreviewTransform : null;
        if (selected == null || preview == null)
        {
            projector.enabled = false;
            if (!selected || !preview) currentTarget = null;
            return;
        }

        // Calculate size only when selection changes.
        if (currentTarget != selected)
        {
            currentTarget = selected;
            CalculateShadowSize(selected);
        }

        Vector3 start;
        RaycastHit hit;
        bool found = groundLine && groundLine.isActiveAndEnabled
            ? groundLine.TryGround(preview, selected, out start, out hit)
            : GhostGroundLine.TryGround(preview, selected, groundLayers, rayDistance, out start, out hit);
        if (!found) { projector.enabled = false; return; }
        float depth = Mathf.Clamp(projectionDepth, .001f, .05f);
        projector.size = new Vector3(projector.size.x, projector.size.y, depth);
        projector.pivot = Vector3.zero;
        transform.SetPositionAndRotation(hit.point + hit.normal * verticalOffset, Quaternion.LookRotation(-hit.normal, Vector3.forward));

        projector.enabled = true;
    }

    private void CalculateShadowSize(Rigidbody target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>();

        Bounds bounds = new Bounds(
            target.position, Vector3.zero);

        bool hasBounds = false;

        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled ||
                !r.gameObject.activeInHierarchy)
                continue;

            if (!(r is MeshRenderer ||
                  r is SkinnedMeshRenderer))
                continue;

            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        float width = Mathf.Max(
            minimumSize, bounds.size.x * sizeMultiplier);

        float height = Mathf.Max(
            minimumSize, bounds.size.z * sizeMultiplier);

        if (forceCircular)
        {
            width = height = Mathf.Max(width, height);
        }

        projector.size = new Vector3(
            width,
            height,
            projector.size.z
        );

        localCenter = target.transform.InverseTransformPoint(
            bounds.center);
    }

    private void OnDisable()
    {
        if (projector != null)
            projector.enabled = false;

        currentTarget = null;
    }
}
