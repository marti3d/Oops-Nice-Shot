using UnityEngine;

[DefaultExecutionOrder(101)]
[RequireComponent(typeof(LineRenderer))]
public sealed class GhostGroundLine : MonoBehaviour
{
    [SerializeField] private ObjectGizmoController gizmoController;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask groundLayers = ~0;
    [Min(.01f)][SerializeField] private float rayDistance = 100;
    [SerializeField] private Material lineMaterial;
    [SerializeField] private Color lineColor = Color.white;
    [SerializeField] private bool usePlacementColor = true, adaptiveWidth = true, dashed = true;
    [Range(0, 1)][SerializeField] private float opacity = .8f;
    [Min(.001f)][SerializeField] private float width = .025f, dashLength = .2f;
    [Min(1)][SerializeField] private float minimumPixels = 2;
    private LineRenderer line;
    private Material runtimeMaterial;
    private Texture2D dashTexture;
    private void Awake()
    {
        line = GetComponent<LineRenderer>(); line.enabled = false; line.useWorldSpace = true; line.positionCount = 2;
        var shader = Shader.Find("MiniGolf/GhostGroundLine");
        if (!shader) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (lineMaterial) runtimeMaterial = new Material(lineMaterial); else if (shader) runtimeMaterial = new Material(shader);
        if (runtimeMaterial)
        {
            line.sharedMaterial = runtimeMaterial;
            if (!lineMaterial) { runtimeMaterial.SetFloat("_Surface", 1); runtimeMaterial.SetFloat("_SrcBlend", 5); runtimeMaterial.SetFloat("_DstBlend", 10); runtimeMaterial.SetFloat("_ZWrite", 0); runtimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); runtimeMaterial.renderQueue = 3000; }
            if (dashed) { dashTexture = new Texture2D(2, 1, TextureFormat.RGBA32, false); dashTexture.SetPixels(new[] { Color.white, new Color(1, 1, 1, 0) }); dashTexture.filterMode = FilterMode.Point; dashTexture.wrapMode = TextureWrapMode.Repeat; dashTexture.Apply(); runtimeMaterial.SetTexture("_BaseMap", dashTexture); line.textureMode = LineTextureMode.Tile; }
        }
    }
    private void LateUpdate()
    {
        if (!gizmoController) gizmoController = FindAnyObjectByType<ObjectGizmoController>(); ;
        var root = gizmoController ? gizmoController.PreviewTransform : null;
        if (!root) { line.enabled = false; return; }
        bool hitFound = TryGround(root, gizmoController.SelectedObject, groundLayers, rayDistance, out Vector3 start, out RaycastHit hit);
        Vector3 end = hitFound ? hit.point : start;
        gizmoController.HasGround = hitFound; line.enabled = hitFound && runtimeMaterial;
        if (!line.enabled) return;
        line.SetPosition(0, start); line.SetPosition(1, end);
        Color c = usePlacementColor ? gizmoController.PreviewColor : lineColor; c.a *= opacity; line.startColor = line.endColor = c;
        if (runtimeMaterial.HasProperty("_BaseColor")) runtimeMaterial.SetColor("_BaseColor", Color.white);
        float w = width; var cam = targetCamera ? targetCamera : Camera.main;
        if (adaptiveWidth && cam && cam.pixelHeight > 0) { float depth = Mathf.Max(cam.nearClipPlane, Vector3.Dot(start - cam.transform.position, cam.transform.forward)); float units = cam.orthographic ? 2 * cam.orthographicSize / cam.pixelHeight : 2 * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) / cam.pixelHeight; w = Mathf.Max(w, units * minimumPixels); }
        line.widthMultiplier = 1f; line.startWidth = line.endWidth = w;
        if (dashed && runtimeMaterial.HasProperty("_BaseMap")) runtimeMaterial.SetTextureScale("_BaseMap", new Vector2(1 / Mathf.Max(.001f, dashLength), 1));
    }
    public static bool TryGround(Transform root, Rigidbody original, LayerMask layers, float distance, out Vector3 start, out RaycastHit best)
    {
        Bounds b = new Bounds(root.position, Vector3.zero);
        bool found = false;
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled) continue;
            if (!found) { b = r.bounds; found = true; } else b.Encapsulate(r.bounds);
        }
        start = new Vector3(b.center.x, b.min.y, b.center.z);
        best = default;
        float nearest = float.PositiveInfinity;
        // Synchronize objects moved by the placement controller before querying.
        Physics.SyncTransforms();
        // Start above the preview bounds, not inside a potential support collider.
        float lift = Mathf.Max(.05f, b.size.y + .05f);
        Vector3 rayOrigin = start + Vector3.up * lift;
        foreach (var h in Physics.RaycastAll(rayOrigin, Vector3.down, distance + lift, layers, QueryTriggerInteraction.Ignore))
        {
            if (!h.collider || h.distance >= nearest || Vector3.Dot(h.normal, Vector3.up) < .5f) continue;
            if (original && (h.rigidbody == original || h.collider.transform.IsChildOf(original.transform))) continue;
            if (h.collider.transform.IsChildOf(root)) continue;
            // Do not treat surfaces above the preview base as valid ground.
            if (h.point.y > start.y + .05f) continue;
            nearest = h.distance; best = h;
        }
        return nearest < float.PositiveInfinity;
    }
    public bool TryGround(Transform root, Rigidbody original, out Vector3 start, out RaycastHit hit)
        => TryGround(root, original, groundLayers, rayDistance, out start, out hit);
    private void OnDisable() { if (line) line.enabled = false; }
    private void OnDestroy() { if (runtimeMaterial) Destroy(runtimeMaterial); if (dashTexture) Destroy(dashTexture); }
}
