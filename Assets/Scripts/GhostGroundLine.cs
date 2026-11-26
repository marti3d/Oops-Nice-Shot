using UnityEngine;

[DefaultExecutionOrder(101)]
[RequireComponent(typeof(LineRenderer))]
public sealed class GhostGroundLine : MonoBehaviour
{
    [SerializeField]
    private ObjectGizmoController gizmoController;
    [SerializeField]
    private Camera targetCamera;
    [SerializeField]
    private Material lineMaterial;
    [SerializeField]
    private Color lineColor = Color.white;
    [SerializeField]
    private bool usePlacementColor = true, adaptiveWidth = true, dashed = true;
    [Range(0, 1)]
    [SerializeField]
    private float opacity = .8f;
    [Min(.001f)]
    [SerializeField]
    private float width = .025f, dashLength = .2f;
    [Min(1)]
    [SerializeField]
    private float minimumPixels = 2;
    private LineRenderer line;
    private Material runtimeMaterial;
    private Texture2D dashTexture;
    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.enabled = false;
        line.useWorldSpace = true;
        line.positionCount = 2;
        if (!gizmoController)
            gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        if (!lineMaterial)
        {
            Debug.LogError("Assign the Ground Line material.", this);
            enabled = false;
            return;
        }
        runtimeMaterial = new Material(lineMaterial);
        if (runtimeMaterial)
        {
            line.sharedMaterial = runtimeMaterial;
            if (dashed)
            {
                dashTexture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
                dashTexture.SetPixels(new[] { Color.white, new Color(1, 1, 1, 0) });
                dashTexture.filterMode = FilterMode.Point;
                dashTexture.wrapMode = TextureWrapMode.Repeat;
                dashTexture.Apply();
                runtimeMaterial.SetTexture("_BaseMap", dashTexture);
                line.textureMode = LineTextureMode.Tile;
            }
        }
    }
    private void LateUpdate()
    {
        line.enabled = gizmoController && gizmoController.PreviewTransform && gizmoController.HasGround;
        if (!line.enabled)
            return;
        Vector3 start = gizmoController.GroundStart;
        Vector3 end = gizmoController.GroundHit.point;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        Color c = usePlacementColor ? gizmoController.PreviewColor : lineColor;
        c.a *= opacity;
        line.startColor = line.endColor = c;
        if (runtimeMaterial.HasProperty("_BaseColor"))
            runtimeMaterial.SetColor("_BaseColor", Color.white);
        float w = width;
        var cam = targetCamera ? targetCamera : Camera.main;
        if (adaptiveWidth && cam && cam.pixelHeight > 0)
        {
            float depth = Mathf.Max(cam.nearClipPlane,
                                    Vector3.Dot(start - cam.transform.position, cam.transform.forward));
            float units = cam.orthographic ? 2 * cam.orthographicSize / cam.pixelHeight
                                           : 2 * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) /
                                                 cam.pixelHeight;
            w = Mathf.Max(w, units * minimumPixels);
        }
        line.widthMultiplier = 1f;
        line.startWidth = line.endWidth = w;
        if (dashed && runtimeMaterial.HasProperty("_BaseMap"))
            runtimeMaterial.SetTextureScale("_BaseMap", new Vector2(1 / Mathf.Max(.001f, dashLength), 1));
    }
    private void OnDisable()
    {
        if (line)
            line.enabled = false;
    }
    private void OnDestroy()
    {
        if (runtimeMaterial)
            Destroy(runtimeMaterial);
        if (dashTexture)
            Destroy(dashTexture);
    }
}