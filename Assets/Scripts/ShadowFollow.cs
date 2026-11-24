using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(DecalProjector))]
[DefaultExecutionOrder(101)]
public class ShadowFollow : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private ObjectGizmoController gizmoController;

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
        Rigidbody selected = gizmoController != null
            ? gizmoController.SelectedObject
            : null;

        if (selected == null)
        {
            projector.enabled = false;
            currentTarget = null;
            return;
        }

        // Calculate size only when selection changes.
        if (currentTarget != selected)
        {
            currentTarget = selected;
            CalculateShadowSize(selected);
        }

        Vector3 center = selected.transform.TransformPoint(localCenter);

        transform.SetPositionAndRotation(
            new Vector3(
                center.x,
                selected.position.y + verticalOffset,
                center.z
            ),
            Quaternion.Euler(90f, 0f, 0f)
        );

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
