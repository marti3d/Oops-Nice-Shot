using UnityEngine;

[DefaultExecutionOrder(101)]
[RequireComponent(typeof(LineRenderer))]
public sealed class GhostGroundLine : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Controller that supplies the preview and ground hit. If empty, finds one in the scene.")]
    [SerializeField]
    private ObjectGizmoController gizmoController;
    [Tooltip("Camera used for adaptive line width. Uses Main Camera if empty.")]
    [SerializeField]
    private Camera targetCamera;

    [Header("Line Appearance")]
    [Tooltip("Material used to render the line from the ghost to the ground.")]
    [SerializeField]
    private Material lineMaterial;
    [Tooltip("Use the gizmo's current placement color for the line.")]
    [SerializeField]
    private bool usePlacementColor = true;
    [Tooltip("Line color used when Use Placement Color is off.")]
    [SerializeField]
    private Color lineColor = Color.white;
    [Tooltip("Multiplier for the line color's opacity.")]
    [Range(0, 1)]
    [SerializeField]
    private float opacity = .8f;

    [Tooltip("Assign GhostGroundLineOverlay.shader to enable the depth controls. The shader can be anywhere under Assets.")]
    [SerializeField]
    private Shader overlayShader;

    [Tooltip("Draw the guide over terrain, the original object, and other scene geometry.")]
    [SerializeField]
    private bool alwaysVisible = true;

    [Header("Line Width")]
    [Tooltip("Base line width in world units.")]
    [Min(.001f)]
    [SerializeField]
    private float width = .025f;
    [Tooltip("Thicken the line when needed to maintain a minimum width on screen.")]
    [SerializeField]
    private bool adaptiveWidth = true;
    [Tooltip("Minimum width in screen pixels when Adaptive Width is enabled.")]
    [Min(1)]
    [SerializeField]
    private float minimumPixels = 2;

    [Header("Dash Pattern")]
    [Tooltip("Create a repeating dash pattern when the component starts.")]
    [SerializeField]
    private bool dashed = true;
    [Tooltip("Length of one dash-and-gap repeat in world units.")]
    [Min(.001f)]
    [SerializeField]
    private float dashLength = .2f;

    private LineRenderer line;
    private Material runtimeMaterial;
    private Texture2D dashTexture;
    private bool hasOverlayShader;
    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.enabled = false;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
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
        hasOverlayShader = overlayShader != null;
        if (hasOverlayShader)
        {
            runtimeMaterial.shader = overlayShader;
            runtimeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
        }
        else
        {
            Debug.LogWarning("Assign GhostGroundLineOverlay.shader to Overlay Shader to enable Always Visible.", this);
        }
        UpdateDepthTest();
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
        UpdateDepthTest();
        Vector3 start = gizmoController.GroundStart;
        Vector3 end = gizmoController.GroundHit.point;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        Color c = usePlacementColor ? gizmoController.PreviewColor : lineColor;
        c.a *= opacity;
        line.startColor = line.endColor = c;
        // The overlay uses vertex colors, including the placement tint and opacity.
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
    private void UpdateDepthTest()
    {
        if (hasOverlayShader)
        {
            runtimeMaterial.SetFloat("_DepthTest", alwaysVisible
                ? (float)UnityEngine.Rendering.CompareFunction.Always
                : (float)UnityEngine.Rendering.CompareFunction.LessEqual);
        }
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