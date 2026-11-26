using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public sealed partial class ObjectGizmoController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField]
    private LayerMask selectableLayers = ~0;
    [SerializeField]
    private Camera targetCamera;
    [SerializeField]
    private CameraController cameraController;
    [SerializeField]
    private LayerMask selectionRayLayers = Physics.DefaultRaycastLayers;
    [Header("Appearance")]
    [SerializeField]
    private Material outlineMaterial;
    [SerializeField]
    private Shader gizmoShader;
    [Min(.01f)]
    [SerializeField]
    private float gizmoWorldSize = 1f;
    [SerializeField]
    private bool autoSizeGizmo = true;
    [Min(.01f)]
    [SerializeField]
    private float minimumGizmoSize = .25f;
    private float selectedObjectSize = 1f;
    [Header("Gizmo Controls")]
    [SerializeField]
    private Key moveKey = Key.Q, rotateKey = Key.W, spaceKey = Key.E;
    [SerializeField]
    private bool rotationTool, localSpace;
    [Header("Gizmo Visuals")]
    [SerializeField]
    private bool limitScreenSize = true;
    public enum GizmoSizeMode
    {
        Adaptive,
        WorldSize,
        ConstantScreenSize,
        ConstantScreenSizePerObject
    }
    [SerializeField]
    private GizmoSizeMode sizeMode = GizmoSizeMode.Adaptive;
    [Min(1)]
    [SerializeField]
    private float constantScreenPixels = 85;
    [Min(.01f)]
    [SerializeField]
    private float referenceObjectSize = 1;
    [Range(1, 2)]
    [SerializeField]
    private float planeSizeMultiplier = 1.4f;
    [Range(.005f, .1f)]
    [SerializeField]
    private float planeBorder = .035f;
    [SerializeField]
    private bool facePlanesTowardsCamera = true;
    [SerializeField]
    private bool fullRingWhileDragging = true;
    private Mesh planeBorderMesh, fullRingMesh;
    private float drawnPlaneBorder;
    private Material planeBorderMaterial;
    private MaterialPropertyBlock planeBorderBlock;
    private float PlaneSize => planeSize * planeSizeMultiplier;
    private readonly Color[] visualColors = new Color[9];
    [Min(1)]
    [SerializeField]
    private float minimumGizmoPixels = 60, maximumGizmoPixels = 120;
    [Range(0, 1)]
    [SerializeField]
    private float idleOpacity = .55f, inactiveOpacity = .1f;
    [Min(.01f)]
    [SerializeField]
    private float fadeDuration = .12f;
    [Min(1)]
    [SerializeField]
    private float hoverScale = 1.1f;
    [Range(30, 360)]
    [SerializeField]
    private float arcDegrees = 180;
    [Range(.05f, .5f)]
    [SerializeField]
    private float planeOffset = .15f, planeSize = .23f;
    [SerializeField]
    private bool showReadout = true;
    [SerializeField]
    private bool showToolLabel = true, showSpaceLabel = true;
    [SerializeField]
    private bool showMovementDistance = true, showRotationAngle = true, showSnapIndicator = true;
    private readonly float[] visualAlpha = new float[9];
    private readonly float[] visualScale = new float[9];
    private Mesh planeMesh;
    private Vector3 planeU, planeV, planeGrab, planeTarget, planeApplied;
    private Quaternion dragFrame;
    private float drawnArc = -1;
    private float EffectiveGizmoSize
    {
        get
        {
            float size = autoSizeGizmo ? Mathf.Max(minimumGizmoSize, gizmoWorldSize * selectedObjectSize)
                                       : gizmoWorldSize;
            if (sizeMode == GizmoSizeMode.WorldSize || !cam || !ghost || cam.pixelHeight <= 0)
                return size;
            float depth =
                Mathf.Max(cam.nearClipPlane,
                          Vector3.Dot(ghost.Root.position - cam.transform.position, cam.transform.forward));
            float pixel = cam.orthographic ? 2 * cam.orthographicSize / cam.pixelHeight
                                           : 2 * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) /
                                                 cam.pixelHeight;
            if (sizeMode == GizmoSizeMode.ConstantScreenSize)
                return constantScreenPixels * pixel;
            if (sizeMode == GizmoSizeMode.ConstantScreenSizePerObject)
                return Mathf.Max(minimumGizmoSize, constantScreenPixels * selectedObjectSize /
                                                       Mathf.Max(.01f, referenceObjectSize)) *
                       pixel;
            if (!limitScreenSize)
                return size;
            return Mathf.Clamp(size, minimumGizmoPixels * pixel,
                               Mathf.Max(minimumGizmoPixels, maximumGizmoPixels) * pixel);
        }
    }
    [SerializeField]
    private bool showMove = true,
                 showRotate = true;
    [Min(.01f)]
    [SerializeField]
    private float arrowLength = 1.4f;
    [Min(.001f)]
    [SerializeField]
    private float arrowShaftThickness = .02f, arrowHeadWidth = .075f;
    [Range(.05f, .8f)]
    [SerializeField]
    private float arrowHeadLength = .22f;
    [Min(.01f)]
    [SerializeField]
    private float ringRadius = .85f;
    [Min(.001f)]
    [SerializeField]
    private float ringThickness = .025f;
    [Range(24, 192)]
    [SerializeField]
    private int ringSegments = 96;
    [SerializeField]
    private Color xColor = new(.94f, .47f, .47f), yColor = new(.55f, .83f, .49f),
                  zColor = new(.47f, .66f, .94f);
    [SerializeField]
    private Color hoverColor = Color.white, activeColor = Color.yellow;
    [Range(0, 1)]
    [SerializeField]
    private float gizmoOpacity = 1;
    [Min(2)]
    [SerializeField]
    private float arrowHitRadiusPixels = 10, ringHitRadiusPixels = 8;
    [Header("Adaptive Thickness")]
    [SerializeField]
    private bool adaptiveGizmoThickness = true;
    [Min(1)]
    [SerializeField]
    private float minimumArrowPixels = 3f;
    [Min(1)]
    [SerializeField]
    private float minimumHeadPixels = 9f;
    [Min(1)]
    [SerializeField]
    private float minimumRingPixels = 3f;
    [Range(1, 20)]
    [SerializeField]
    private float maximumThicknessMultiplier = 10f;
    [Header("Manipulation")]
    [Min(.01f)]
    [SerializeField]
    private float maxDragSpeed = 15, maxRotationSpeed = 360, rotationSensitivity = 1;
    [Min(.001f)]
    [SerializeField]
    private float moveSnap = .5f, rotationSnap = 15;
    [Header("Pickup Lift")]
    [Min(0f)]
    [SerializeField]
    private float pickupLiftHeight = 0.5f;
    [Min(0.01f)]
    [SerializeField]
    private float pickupLiftDuration = 0.25f;
    private float liftElapsed, liftTotal;
    private float remainingLift;
    [Header("Collision checks")]
    [SerializeField]
    private LayerMask obstacleLayers = ~0;
    [Min(0)]
    [SerializeField]
    private float collisionClearance = .005f;
    private readonly Dictionary<Rigidbody, Pose> spawnPoses = new();
    private readonly Dictionary<MeshFilter, Mesh> outlines = new();
    private readonly Dictionary<Mesh, Mesh> sharedOutlines = new();
    private readonly List<Mesh> temporaryOutlines = new();
    private static readonly Vector3[] AxisDirs = { Vector3.right, Vector3.up, Vector3.forward };
    private readonly Material[] materials = new Material[9];
    private Camera cam;
    private Material outlineMask, outlineOverlay;
    private Rigidbody selected;
    public Rigidbody SelectedObject => selected;
    public Transform PreviewTransform => ghost ? ghost.Root : null;
    public bool PreviewValid { get; private set; }
    [Header("Ghost Placement")]
    [SerializeField]
    private Shader ghostShader;
    [SerializeField]
    private Color validColor = Color.white, invalidColor = Color.red, noGroundColor = Color.yellow;
    [Range(0, 1)]
    [SerializeField]
    private float ghostOpacity = .35f;
    [Min(.01f)]
    [SerializeField]
    private float transferSpeed = 25f;
    [Min(0)]
    [SerializeField]
    private float releaseDelay = .5f;
    [Min(0)]
    [SerializeField]
    private float rejectDuration = .25f, rejectShake = .06f;

    private GhostPreview ghost;
    private float rejectedUntil;
    public bool HasGround { get; private set; }
    public Bounds PreviewBounds { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public Vector3 GroundStart { get; private set; }
    private readonly PlacementProbe probe = new();
    private MeshRenderer[] previewRenderers;
    [SerializeField]
    private LayerMask groundLayers = ~0;
    [Tooltip("Surfaces such as water that forbid placement and hide the indicator, even above valid ground.")]
    [SerializeField] private LayerMask forbiddenSurfaceLayers;
    private bool overForbiddenSurface;
    [Min(.01f)]
    [SerializeField]
    private float groundDistance = 100f;
    private bool confirmRequested;
    public Color PreviewColor => overForbiddenSurface ? noGroundColor
                                 : Time.time < rejectedUntil || !PreviewValid ? invalidColor
                                 : HasGround ? validColor
                                                                            : noGroundColor;
    private readonly List<Transfer> transfers = new();
    private sealed class Transfer
    {
        public Rigidbody body;
        public GhostPreview preview;
        public Pose start, end;
        public Collider[] shapes;
        public float elapsed, duration, hold;
        public bool arrived, gravity, collisions;
        public CollisionDetectionMode ccd;
        public RigidbodyInterpolation interpolation;
    }
    private Collider[] shapes;
    private Mesh arrowMesh, ringMesh;
    private float drawnShaft = -1, drawnHead = -1, drawnRing = -1;
    private float drawnHeadLength = -1, drawnRadius = -1;
    private int drawnSegments = -1;
    private int hover = -1, drag = -1;
    private bool savedGravity, savedCameraInput;
    private CollisionDetectionMode savedCcd;
    private RigidbodyInterpolation savedInterpolation;
    private Vector3 origin, axis, startPosition, lastRadial;
    private Quaternion startRotation;
    private float grab, angle, target, applied;
    private bool Snap => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed ||
                                                      Keyboard.current.rightShiftKey.isPressed);
    private void Reset()
    {
        targetCamera = Camera.main;
        gizmoShader = Shader.Find("MiniGolf/GizmoAlwaysOnTopSoft");
    }
    private void Awake()
    {
        if (!ghostShader)
        {
            Debug.LogError("Assign the Ghost shader.", this);
            enabled = false;
            return;
        }
        planeBorderBlock = new MaterialPropertyBlock();
        cam = targetCamera != null ? targetCamera : Camera.main;
        if (!cam)
        {
            enabled = false;
            return;
        }
        if (!cameraController)
            cam.TryGetComponent(out cameraController);
        Shader shader = gizmoShader;
        if (!shader)
        {
            Debug.LogError("Assign gizmo shader", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < 9; i++)
            materials[i] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        planeMesh = new Mesh { name = "GizmoPlane" };
        planeMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.right + Vector3.up };
        planeMesh.triangles = new[] { 0, 1, 2, 2, 1, 3, 2, 1, 0, 3, 1, 2 };
        planeMesh.RecalculateBounds();
        planeBorderMesh = BuildPlaneBorder();
        drawnPlaneBorder = planeBorder;
        planeBorderMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        for (int i = 0; i < 9; i++)
        {
            visualAlpha[i] = idleOpacity;
            visualScale[i] = 1;
            visualColors[i] = i % 3 == 0 ? xColor : i % 3 == 1 ? yColor : zColor;
        }
        RefreshGizmoMeshes();
    }
    private void Start()
    {
        foreach (var rb in FindObjectsByType<Rigidbody>())
            RegisterSpawn(rb);
    }
    private void OnDisable()
    {
        ClearSelection();
        foreach (var t in transfers)
            FinishTransfer(t);
        transfers.Clear();
    }
    private void OnApplicationFocus(bool focus)
    {
        if (!focus)
            ClearSelection();
    }
    private void OnDestroy()
    {
        ClearSelection();
        foreach (Mesh outline in sharedOutlines.Values)
            Destroy(outline);
        if (outlineMask)
            Destroy(outlineMask);
        if (outlineOverlay)
            Destroy(outlineOverlay);
        foreach (var m in materials)
            if (m)
                Destroy(m);
        if (arrowMesh)
            Destroy(arrowMesh);
        if (ringMesh)
            Destroy(ringMesh);
        if (planeMesh)
            Destroy(planeMesh);
        if (planeBorderMesh)
            Destroy(planeBorderMesh);
        if (fullRingMesh)
            Destroy(fullRingMesh);
        if (planeBorderMaterial)
            Destroy(planeBorderMaterial);
    }
    private void Update()
    {
        if (!selected && (drag >= 0 || outlines.Count > 0))
            ClearSelection();
        if (selected && !selected.gameObject.activeInHierarchy)
            ClearSelection();
        UpdateTransfers();
        var keyboard = Keyboard.current;
        if (keyboard != null && drag < 0)
        {
            if (moveKey != Key.None && keyboard[moveKey].wasPressedThisFrame)
            {
                rotationTool = false;
                hover = -1;
            }
            if (rotateKey != Key.None && keyboard[rotateKey].wasPressedThisFrame)
            {
                rotationTool = true;
                hover = -1;
            }
            if (spaceKey != Key.None && keyboard[spaceKey].wasPressedThisFrame)
            {
                localSpace = !localSpace;
                hover = -1;
            }
        }
        if (selected && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            confirmRequested = true;
            return;
        }
        var mouse = Mouse.current;
        if (mouse == null || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
        {
            ClearSelection();
            return;
        }
        Vector2 pos = mouse.position.ReadValue();
        if (drag >= 0)
        {
            if (!mouse.leftButton.isPressed)
                EndDrag();
            else
                UpdateTarget(pos);
            return;
        }
        bool ui = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        hover = selected && !ui ? Pick(pos) : -1;
        if (!ui && mouse.leftButton.wasPressedThisFrame)
        {
            if (hover >= 0)
            {
                BeginDrag(hover, pos);
                return;
            }
            // Keep the current preview when clicking outside; Escape cancels.
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, Mathf.Infinity, selectionRayLayers,
                                QueryTriggerInteraction.Ignore) &&
                hit.rigidbody &&
                (hit.rigidbody == selected ||
                 (!hit.rigidbody.isKinematic &&
                  (selectableLayers.value & (1 << hit.rigidbody.gameObject.layer)) != 0)))
                Select(hit.rigidbody);
        }
    }
    private void Select(Rigidbody rb)
    {
        if (rb == selected)
            return;
        ClearSelection();
        RegisterSpawn(rb);
        selected = rb;
        savedGravity = rb.useGravity;
        savedCcd = rb.collisionDetectionMode;
        savedInterpolation = rb.interpolation;
        shapes = rb.GetComponentsInChildren<Collider>();
        selectedObjectSize = CalculateObjectSize(rb);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.isKinematic = true;
        ghost = GhostPreview.Create(rb, ghostShader, ghostOpacity);
        if (!ghost)
        {
            ClearSelection();
            return;
        }
        previewRenderers = ghost.GetComponentsInChildren<MeshRenderer>();
        HasGround = false;
        rejectedUntil = 0;
        remainingLift = liftTotal = pickupLiftHeight;
        liftElapsed = 0f;
        if (outlineMaterial == null)
            return;
        foreach (MeshFilter f in ghost.Root.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || !f.TryGetComponent<MeshRenderer>(out var renderer))
                continue;
            if (!f.sharedMesh.isReadable)
            {
                Debug.LogWarning("Enable Read/Write on this mesh for a welded outline.", f);
                continue;
            }
            if (sharedOutlines.TryGetValue(f.sharedMesh, out Mesh cached))
            {
                outlines.Add(f, cached);
                continue;
            }
            Mesh mesh = Instantiate(f.sharedMesh);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals.Length != vertices.Length)
            {
                mesh.RecalculateNormals();
                normals = mesh.normals;
            }
            var sums = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i] * 100000f;
                keys[i] = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                sums.TryGetValue(keys[i], out Vector3 sum);
                sums[keys[i]] = sum + normals[i];
            }
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 n = sums[keys[i]];
                if (n.sqrMagnitude > 0.000001f)
                    normals[i] = n.normalized;
            }
            mesh.normals = normals;
            if (ghost.IsBakedMesh(f.sharedMesh))
                temporaryOutlines.Add(mesh);
            else
                sharedOutlines.Add(f.sharedMesh, mesh);
            outlines.Add(f, mesh);
        }
    }

    // Measure in a rotation-neutral frame, retaining world scale.
    private static float CalculateObjectSize(Rigidbody rb)
    {
        Quaternion inverseRotation = Quaternion.Inverse(rb.transform.rotation);
        Bounds combined = default;
        bool found = false;
        foreach (Renderer renderer in rb.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                continue;
            Bounds local = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner =
                    local.center +
                    Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1,
                                                             (i & 4) == 0 ? -1 : 1));
                Vector3 point =
                    inverseRotation * (renderer.transform.TransformPoint(corner) - rb.transform.position);
                if (!found)
                {
                    combined = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    combined.Encapsulate(point);
            }
        }
        if (!found)
            return 1f;
        Vector3 size = combined.size;
        return Mathf.Max(.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
    }

    private void ClearSelection()
    {
        confirmRequested = false;
        PreviewValid = HasGround = false;
        EndDrag();
        if (ghost)
            Destroy(ghost.gameObject);
        ghost = null;
        remainingLift = 0f;
        selectedObjectSize = 1f;
        if (selected)
        {
            selected.isKinematic = false;
            selected.collisionDetectionMode = savedCcd;
            selected.interpolation = savedInterpolation;
            selected.useGravity = savedGravity;
            selected.linearVelocity = Vector3.zero;
            selected.angularVelocity = Vector3.zero;
            selected.WakeUp();
        }
        ReleaseSelectionOutlines();
        selected = null;
        shapes = null;
        hover = -1;
    }
    private void ReleaseSelectionOutlines()
    {
        foreach (Mesh mesh in temporaryOutlines)
            Destroy(mesh);
        temporaryOutlines.Clear();
        outlines.Clear();
    }
    private Quaternion Frame => drag >= 0 ? dragFrame
                                : localSpace && ghost ? ghost.Root.rotation
                                                      : Quaternion.identity;
    private Vector3 Direction(int i) => Frame * AxisDirs[i];
    private Quaternion AxisRotation(int i) => Frame * Quaternion.FromToRotation(Vector3.forward, AxisDirs[i]);
    private void LateUpdate()
    {
        if (!selected || !ghost)
            return;
        if (drag < 0)
            UpdatePickupLift();
        else
            UpdateDrag();
        Physics.SyncTransforms();
        PreviewBounds = PlacementProbe.Measure(previewRenderers, ghost.Root.position);
        HasGround = probe.FindGround(PreviewBounds, selected, groundLayers, forbiddenSurfaceLayers,
                                     groundDistance, out var start, out var hit, out overForbiddenSurface);
        GroundStart = start;
        GroundHit = hit;
        PreviewValid = probe.CanPlace(selected, shapes, ghost.Root.position, ghost.Root.rotation,
                                      obstacleLayers, collisionClearance);
        if (confirmRequested)
        {
            confirmRequested = false;
            Confirm();
            if (!ghost)
                return;
        }
        ghost.SetAppearance(PreviewColor,
                            Time.time < rejectedUntil ? Mathf.Sin(Time.time * 90f) * rejectShake : 0f);
        DrawHandlesAndOutline();
    }
    private void UpdatePickupLift()
    {
        if (remainingLift <= 0f)
            return;
        liftElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(liftElapsed / Mathf.Max(.01f, pickupLiftDuration));
        // Smoothstep: gentle acceleration and deceleration.
        float eased = t * t * (3f - 2f * t);
        float distance = Mathf.Clamp(liftTotal * eased - (liftTotal - remainingLift), 0f, remainingLift);
        if (t >= 1f)
            distance = remainingLift;
        if (distance <= 0f)
            return;
        ghost.Root.position += Vector3.up * distance;
        remainingLift = t >= 1f ? 0f : Mathf.Max(0f, remainingLift - distance);
    }

    private void UpdateDrag()
    {
        if (!selected || !ghost)
            return;
        if (drag < 0)
            return;
        if (drag >= 6)
        {
            planeApplied = Vector3.MoveTowards(planeApplied, planeTarget, maxDragSpeed * Time.deltaTime);
            ghost.Root.position = startPosition + planeApplied;
            return;
        }
        bool move = drag < 3;
        float end =
            Mathf.MoveTowards(applied, target, (move ? maxDragSpeed : maxRotationSpeed) * Time.deltaTime);
        applied = end;
        if (move)
            ghost.Root.position = startPosition + axis * end;
        else
            ghost.Root.rotation = Quaternion.AngleAxis(end, axis) * startRotation;
    }
}
