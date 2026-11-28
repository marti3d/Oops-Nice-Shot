using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public sealed partial class ObjectGizmoController : MonoBehaviour
{
    [MiniGolfFoldout("Scene References")]
    [Tooltip("Camera used to draw the gizmo and read pointer positions. Uses Main Camera if left blank.")]
    [SerializeField]
    private Camera targetCamera;
    [Tooltip("Camera controller whose input is paused while a handle is dragged.")]
    [SerializeField]
    private CameraController cameraController;
    [Tooltip("Shader used to draw the gizmo handles.")]
    [SerializeField]
    private Shader gizmoShader;
    [Tooltip("Material used to outline the selected object.")]
    [SerializeField]
    private Material outlineMaterial;

    [MiniGolfFoldout("Object Selection")]
    [Tooltip("Layers containing rigidbodies that can be selected.")]
    [SerializeField]
    private LayerMask selectableLayers = ~0;
    [Tooltip("Layers the click ray can hit while looking for a selectable object.")]
    [SerializeField]
    private LayerMask selectionRayLayers = Physics.DefaultRaycastLayers;

    [MiniGolfFoldout("Starting Mode")]
    [Tooltip("Start in Rotate mode when checked. Otherwise start in Move mode.")]
    [InspectorName("Start in Rotate Mode")]
    [SerializeField]
    private bool rotationTool;
    [Tooltip("Start with axes aligned to the selected object instead of the world.")]
    [InspectorName("Start in Local Space")]
    [SerializeField]
    private bool localSpace;

    [MiniGolfFoldout("Gizmo Size")]
    [Tooltip("Choose world units, adaptive sizing, or a fixed screen size with optional object scaling.")]
    [SerializeField]
    private GizmoSizeMode sizeMode = GizmoSizeMode.Adaptive;
    [Tooltip("Base gizmo size in world units.")]
    [Min(.01f)]
    [SerializeField]
    private float gizmoWorldSize = 1f;
    [Tooltip("Scale the base gizmo size with the selected object's size.")]
    [SerializeField]
    private bool autoSizeGizmo = true;
    [Tooltip("Minimum size value when automatic object scaling is active.")]
    [Min(.01f)]
    [SerializeField]
    private float minimumGizmoSize = .25f;
    [Tooltip("Clamp the gizmo's screen size in Adaptive mode.")]
    [SerializeField]
    private bool limitScreenSize = true;
    [Tooltip("Smallest screen size allowed in Adaptive mode, in pixels.")]
    [Min(1)]
    [SerializeField]
    private float minimumGizmoPixels = 60;
    [Tooltip("Largest screen size allowed in Adaptive mode, in pixels.")]
    [Min(1)]
    [SerializeField]
    private float maximumGizmoPixels = 120;
    [Tooltip("Target size in pixels for the constant screen size modes.")]
    [Min(1)]
    [SerializeField]
    private float constantScreenPixels = 85;
    [Tooltip("Object size that matches the target pixel size in Constant Screen Size Per Object mode.")]
    [Min(.01f)]
    [SerializeField]
    private float referenceObjectSize = 1;

    [MiniGolfFoldout("Handle Colors and Feedback")]
    [Tooltip("Color of the X-axis handles and the YZ plane.")]
    [SerializeField]
    private Color xColor = new(.94f, .47f, .47f);
    [Tooltip("Color of the Y-axis handles and the XZ plane.")]
    [SerializeField]
    private Color yColor = new(.55f, .83f, .49f);
    [Tooltip("Color of the Z-axis handles and the XY plane.")]
    [SerializeField]
    private Color zColor = new(.47f, .66f, .94f);
    [Tooltip("Color blended into a move arrow or rotation arc when the pointer is over it.")]
    [SerializeField]
    private Color hoverColor = Color.white;
    [Tooltip("Color of a move arrow or rotation arc while it is being dragged.")]
    [SerializeField]
    private Color activeColor = Color.yellow;
    [Tooltip("Overall opacity multiplier for the gizmo.")]
    [Range(0, 1)]
    [SerializeField]
    private float gizmoOpacity = 1;
    [Tooltip("Handle opacity when the pointer is elsewhere and nothing is being dragged.")]
    [Range(0, 1)]
    [SerializeField]
    private float idleOpacity = .55f;
    [Tooltip("Handle opacity while a different handle is being dragged.")]
    [Range(0, 1)]
    [SerializeField]
    private float inactiveOpacity = .1f;
    [Tooltip("Controls how quickly handle color, opacity, and hover size respond.")]
    [Min(.01f)]
    [SerializeField]
    private float fadeDuration = .12f;
    [Tooltip("Size multiplier for arrows and rotation arcs on hover.")]
    [Min(1)]
    [SerializeField]
    private float hoverScale = 1.1f;

    [MiniGolfFoldout("Move Arrows")]
    [Tooltip("Show the arrows and plane handles in Move mode.")]
    [SerializeField]
    private bool showMove = true;
    [Tooltip("Arrow length relative to the gizmo size.")]
    [Min(.01f)]
    [SerializeField]
    private float arrowLength = 1.4f;
    [Tooltip("Base thickness of the painted move stroke.")]
    [Min(.001f)]
    [SerializeField]
    private float arrowShaftThickness = .02f;
    [Tooltip("Sideways spread of the open move arrowhead.")]
    [Min(.001f)]
    [SerializeField]
    private float arrowHeadWidth = .075f;
    [Tooltip("Length of the move arrowhead as a fraction of the arrow.")]
    [Range(.05f, .8f)]
    [SerializeField]
    private float arrowHeadLength = .22f;
    [Tooltip("Pointer tolerance around the move strokes, in pixels.")]
    [Min(2)]
    [SerializeField]
    private float arrowHitRadiusPixels = 10;

    [MiniGolfFoldout("Rotation Arcs")]
    [Tooltip("Show the rotation handles in Rotate mode.")]
    [SerializeField]
    private bool showRotate = true;
    [Tooltip("Radius of the rotation arcs relative to the gizmo size.")]
    [Min(.01f)]
    [SerializeField]
    private float ringRadius = .85f;
    [Tooltip("Base thickness of the painted rotation stroke.")]
    [Min(.001f)]
    [SerializeField]
    private float ringThickness = .025f;
    [Tooltip("Number of segments used to draw each rotation arc or ring.")]
    [Range(24, 192)]
    [SerializeField]
    private int ringSegments = 96;
    [Tooltip("How much of the circle is visible, in degrees. Set to 360 for a full ring.")]
    [Range(30, 360)]
    [SerializeField]
    private float arcDegrees = 180;
    [Tooltip("Show a full ring for the active axis while rotating.")]
    [SerializeField]
    private bool fullRingWhileDragging = true;
    [Tooltip("Pointer tolerance around the rotation strokes, in pixels.")]
    [Min(2)]
    [SerializeField]
    private float ringHitRadiusPixels = 8;

    [MiniGolfFoldout("Painted Arrows and Arcs")]
    [Tooltip("Amount of painted waviness in hovered move arrows and rotation arcs.")]
    [SerializeField, Range(0f, .12f)]
    private float brushWobble = .045f;
    [Tooltip("How much the brushstroke thickness varies along each stroke.")]
    [SerializeField, Range(0f, .65f)]
    private float brushWidthVariation = .3f;
    [Tooltip("How strongly the Hover Color blends into arrows and arcs on hover.")]
    [SerializeField, Range(0f, 1f)]
    private float brushHoverTint = .4f;

    [MiniGolfFoldout("Move Arrow Animation")]
    [Tooltip("Sideways bend at full hover. Negative values bend in the opposite direction.")]
    [SerializeField, Range(-.25f, .25f)]
    private float arrowCurvature = .065f;
    [Tooltip("Enable the traveling wave when a move arrow is hovered or dragged.")]
    [InspectorName("Animate Move Arrows")]
    [SerializeField]
    private bool animateArrows = true;
    [Tooltip("Sideways wave strength relative to the arrow length.")]
    [SerializeField, Range(0f, .2f)]
    private float arrowWaveAmplitude = .055f;
    [Tooltip("Wave cycles per second. Zero freezes the wave in place.")]
    [SerializeField, Range(0f, 6f)]
    private float arrowWaveSpeed = 1.1f;
    [Tooltip("Number of waves along each move arrow.")]
    [SerializeField, Range(.5f, 4f)]
    private float arrowWaveCount = 1.5f;
    [Tooltip("Time for the move arrow animation to ease in on hover, in seconds.")]
    [SerializeField, Range(.01f, 1f)]
    [InspectorName("Move Hover Fade In")]
    private float arrowHoverTransition = .2f;
    [Tooltip("Time for a move arrow to ease back to its resting shape, in seconds.")]
    [SerializeField, Range(.01f, 2f)]
    private float arrowHoverFadeOut = .25f;
    [Tooltip("Transition easing. Zero is linear, one softens the start and end.")]
    [SerializeField, Range(0f, 1f)]
    private float arrowHoverEasing = 1f;
    [Tooltip("How much wave speed, spacing, and strength vary. Zero gives a regular repeating wave.")]
    [SerializeField, Range(0f, 1f)]
    private float arrowWaveRandomness = .8f;

    [MiniGolfFoldout("Rotate Arrow Animation")]
    [Tooltip("Amount of inward and outward bend at full hover.")]
    [SerializeField, Range(-.25f, .25f)]
    private float rotateCurvature = .035f;
    [Tooltip("Enable the traveling wave when a rotation arc is hovered or dragged.")]
    [InspectorName("Animate Rotate Arcs")]
    [SerializeField]
    private bool animateRotationArrows = true;
    [Tooltip("Radial wave strength relative to the ring radius.")]
    [SerializeField, Range(0f, .2f)]
    private float rotateWaveAmplitude = .045f;
    [Tooltip("Wave cycles per second. Zero freezes the wave in place.")]
    [SerializeField, Range(0f, 6f)]
    private float rotateWaveSpeed = .9f;
    [Tooltip("Number of waves per full revolution.")]
    [SerializeField, Range(1, 12)]
    private int rotateWaveCount = 5;
    [Tooltip("Time for the rotation arc animation to ease in on hover, in seconds.")]
    [SerializeField, Range(.01f, 2f)]
    private float rotateHoverFadeIn = .2f;
    [Tooltip("Time for a rotation arc to ease back to its resting shape, in seconds.")]
    [SerializeField, Range(.01f, 2f)]
    private float rotateHoverFadeOut = .25f;
    [Tooltip("Transition easing. Zero is linear, one softens the start and end.")]
    [SerializeField, Range(0f, 1f)]
    private float rotateHoverEasing = 1f;
    [Tooltip("How much rotation wave speed and strength vary. Zero gives a regular repeating wave.")]
    [SerializeField, Range(0f, 1f)]
    private float rotateWaveRandomness = .8f;

    [MiniGolfFoldout("Plane Handle Layout")]
    [Tooltip("Distance from the gizmo center to each tile along its two axes.")]
    [Range(.05f, .5f)]
    [SerializeField]
    private float planeOffset = .15f;
    [Tooltip("Base size of each plane tile relative to the gizmo.")]
    [Range(.05f, .5f)]
    [SerializeField]
    private float planeSize = .23f;
    [Tooltip("Extra size multiplier applied to the three plane tiles.")]
    [Range(1, 2)]
    [SerializeField]
    private float planeSizeMultiplier = 1.4f;
    [Tooltip("Place the tiles on the side of the gizmo facing the camera.")]
    [SerializeField]
    private bool facePlanesTowardsCamera = true;

    [MiniGolfFoldout("Painted Plane Handles")]
    [Tooltip("How rounded the corners of the plane tiles are.")]
    [SerializeField, Range(.02f, .45f)]
    private float planeCornerRoundness = .23f;
    [Tooltip("Amount of static irregularity along the tile edge.")]
    [InspectorName("Edge Wobble")]
    [SerializeField, Range(0f, .045f)]
    private float planeBrushRoughness = .018f;
    [Tooltip("Thickness of the painted edge. Zero hides the edge.")]
    [InspectorName("Edge Thickness")]
    [SerializeField, Range(0f, .1f)]
    private float planeBorder = .035f;
    [Tooltip("Opacity multiplier for the tile border.")]
    [SerializeField, Range(0f, 1f)]
    private float planeEdgeOpacity = 1f;
    [Tooltip("Opacity multiplier for the tile interior.")]
    [SerializeField, Range(0f, 1f)]
    private float planeFillOpacity = .36f;

    [MiniGolfFoldout("Plane Handle Animation")]
    [Tooltip("Enable the traveling ripple when a plane tile is hovered or dragged.")]
    [InspectorName("Animate Plane Tiles")]
    [SerializeField]
    private bool animatePlaneHandles = true;
    [Tooltip("Ripple strength relative to the tile size.")]
    [SerializeField, Range(0f, .2f)]
    private float planeWaveAmplitude = .065f;
    [Tooltip("Ripple cycles per second. Zero freezes the ripple in place.")]
    [SerializeField, Range(0f, 6f)]
    private float planeWaveSpeed = .85f;
    [Tooltip("Time for the tile ripple to ease in on hover, in seconds.")]
    [SerializeField, Range(.01f, 2f)]
    private float planeHoverFadeIn = .2f;
    [Tooltip("Time for a tile to ease back to its resting shape, in seconds.")]
    [SerializeField, Range(.01f, 2f)]
    private float planeHoverFadeOut = .25f;
    [Tooltip("Transition easing. Zero is linear, one softens the start and end.")]
    [SerializeField, Range(0f, 1f)]
    private float planeHoverEasing = 1f;
    [Tooltip("How much ripple speed and strength vary. Zero gives a regular repeating ripple.")]
    [SerializeField, Range(0f, 1f)]
    private float planeWaveRandomness = .8f;

    [MiniGolfFoldout("Adaptive Stroke Thickness")]
    [Tooltip("Thicken thin arrows and arcs on screen outside World Size mode.")]
    [SerializeField]
    private bool adaptiveGizmoThickness = true;
    [Tooltip("Target minimum width of the move arrow shaft, in pixels.")]
    [Min(1)]
    [SerializeField]
    private float minimumArrowPixels = 3f;
    [Tooltip("Target minimum width of the move arrowhead, in pixels.")]
    [Min(1)]
    [SerializeField]
    private float minimumHeadPixels = 9f;
    [Tooltip("Target minimum thickness of rotation strokes, in pixels.")]
    [Min(1)]
    [SerializeField]
    private float minimumRingPixels = 3f;
    [Tooltip("Cap on how much adaptive sizing can thicken a stroke.")]
    [Range(1, 20)]
    [SerializeField]
    private float maximumThicknessMultiplier = 10f;

    [MiniGolfFoldout("Dragging")]
    [Tooltip("Maximum speed at which the preview follows a move drag, in world units per second.")]
    [Min(.01f)]
    [SerializeField]
    private float maxDragSpeed = 15;
    [Tooltip("Maximum speed at which the preview follows rotation input, in degrees per second.")]
    [Min(.01f)]
    [SerializeField]
    private float maxRotationSpeed = 360;
    [Tooltip("Multiplier for the rotation produced by pointer movement.")]
    [Min(.01f)]
    [SerializeField]
    private float rotationSensitivity = 1;
    [MiniGolfFoldout("On-Screen Readout")]
    [Tooltip("Show the text readout next to the gizmo.")]
    [SerializeField]
    private bool showReadout = true;
    [Tooltip("Show the current Move or Rotate mode in the readout.")]
    [SerializeField]
    private bool showToolLabel = true;
    [Tooltip("Show whether the gizmo uses local or world axes.")]
    [SerializeField]
    private bool showSpaceLabel = true;
    [Tooltip("Show displacement while dragging a move handle.")]
    [SerializeField]
    private bool showMovementDistance = true;
    [Tooltip("Show the angle while dragging a rotation handle.")]
    [SerializeField]
    private bool showRotationAngle = true;
    [MiniGolfFoldout("Pickup Lift")]
    [Tooltip("How far the preview lifts after selection. Zero disables the lift.")]
    [Min(0f)]
    [SerializeField]
    private float pickupLiftHeight = 0.5f;
    [Tooltip("Time in seconds for the pickup lift.")]
    [Min(0.01f)]
    [SerializeField]
    private float pickupLiftDuration = 0.25f;

    [MiniGolfFoldout("Placement Rules")]
    [Tooltip("Layers checked for obstacles that block placement.")]
    [SerializeField]
    private LayerMask obstacleLayers = ~0;
    [Tooltip("Extra clearance required around the object during placement checks.")]
    [Min(0)]
    [SerializeField]
    private float collisionClearance = .005f;
    [Tooltip("Layers checked for ground below the preview.")]
    [SerializeField]
    private LayerMask groundLayers = ~0;
    [Tooltip("Surfaces that block placement even when valid ground is below them.")]
    [SerializeField]
    private LayerMask forbiddenSurfaceLayers;
    [Tooltip("Maximum distance checked for ground below the preview.")]
    [Min(.01f)]
    [SerializeField]
    private float groundDistance = 100f;

    [MiniGolfFoldout("Ghost Preview")]
    [Tooltip("Shader used to draw the placement preview.")]
    [SerializeField]
    private Shader ghostShader;
    [Tooltip("Opacity of the ghost preview.")]
    [Range(0, 1)]
    [SerializeField]
    private float ghostOpacity = .35f;
    [Tooltip("Preview color for a clear placement with ground below it.")]
    [SerializeField]
    private Color validColor = Color.white;
    [Tooltip("Preview color for a blocked placement or rejected confirmation.")]
    [SerializeField]
    private Color invalidColor = Color.red;
    [Tooltip("Preview color when ground is missing or a forbidden surface is detected.")]
    [SerializeField]
    private Color noGroundColor = Color.yellow;
    [Tooltip("Duration of the rejection feedback, in seconds.")]
    [Min(0)]
    [SerializeField]
    private float rejectDuration = .25f;
    [Tooltip("Strength of the preview shake after a rejected confirmation.")]
    [Min(0)]
    [SerializeField]
    private float rejectShake = .06f;

    [MiniGolfFoldout("Confirmed Placement")]
    [Tooltip("Target speed for moving the original object to the confirmed preview.")]
    [Min(.01f)]
    [SerializeField]
    private float transferSpeed = 25f;
    [Tooltip("Time the object stays held after arrival before physics resumes, in seconds.")]
    [Min(0)]
    [SerializeField]
    private float releaseDelay = .5f;

    private float selectedObjectSize = 1f;
    public enum GizmoSizeMode
    {
        Adaptive,
        WorldSize,
        ConstantScreenSize,
        ConstantScreenSizePerObject
    }
    private Mesh fullRingMesh;
    private float drawnPlaneBorder;
    private Material planeBorderMaterial;
    private MaterialPropertyBlock planeBorderBlock;
    private float PlaneSize => planeSize * planeSizeMultiplier;
    private readonly Color[] visualColors = new Color[9];
    private readonly float[] visualAlpha = new float[9];
    private readonly float[] visualScale = new float[9];
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
    private float liftElapsed, liftTotal;
    private float remainingLift;
    private readonly Dictionary<Rigidbody, Pose> spawnPoses = new();
    private readonly Dictionary<MeshFilter, Mesh> outlines = new();
    private readonly Dictionary<Mesh, Mesh> sharedOutlines = new();
    private readonly List<Mesh> temporaryOutlines = new();
    private static readonly Vector3[] AxisDirs = { Vector3.right, Vector3.up, Vector3.forward };
    private readonly Material[] materials = new Material[9];
    private Camera cam;
    private Material outlineMask, outlineOverlay;
    private Rigidbody selected;
    public Camera ViewCamera => cam;
    public Rigidbody SelectedObject => selected;
    public Transform PreviewTransform => ghost ? ghost.Root : null;
    public bool PreviewValid { get; private set; }
    public bool IsRotationTool => rotationTool;
    public bool IsLocalSpace => localSpace;
    public bool IsOverForbiddenSurface => overForbiddenSurface;

    private bool paused;

    public void SetPaused(bool value)
    {
        paused = value;
        if (!value)
            return;

        EndDrag();
        confirmRequested = false;
        remainingLift = 0f;
        hover = -1;
    }

    private GhostPreview ghost;
    private float rejectedUntil;
    public bool HasGround { get; private set; }
    public Bounds PreviewBounds { get; private set; }
    public RaycastHit GroundHit { get; private set; }
    public Vector3 GroundStart { get; private set; }
    private readonly PlacementProbe probe = new();
    private MeshRenderer[] previewRenderers;
    private bool overForbiddenSurface;
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
    private float drawnShaft = -1, drawnHead = -1, drawnRing = -1;
    private float drawnHeadLength = -1, drawnRadius = -1;
    private int drawnSegments = -1;
    private int hover = -1, drag = -1;
    private bool savedGravity;
    private CollisionDetectionMode savedCcd;
    private RigidbodyInterpolation savedInterpolation;
    private Vector3 origin, axis, startPosition, lastRadial;
    private Quaternion startRotation;
    private float grab, angle, target, applied;
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
        if (!focus && !paused)
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
        DestroyArrowMeshes();
        DestroyRotationMeshes();
        DestroyPlaneMeshes();
        if (planeBorderMaterial)
            Destroy(planeBorderMaterial);
    }
    public void ToggleAxisSpace()
    {
        if (!isActiveAndEnabled || paused || Time.timeScale == 0f || drag >= 0)
            return;

        localSpace = !localSpace;
        hover = -1;
    }

    public void UseMoveTool() => SetRotationTool(false);

    public void UseRotateTool() => SetRotationTool(true);

    private void SetRotationTool(bool rotate)
    {
        if (!isActiveAndEnabled || paused || Time.timeScale == 0f || drag >= 0)
            return;

        rotationTool = rotate;
        hover = -1;
    }

    public void RequestPlacement()
    {
        if (isActiveAndEnabled && !paused && Time.timeScale > 0f && selected && ghost)
            confirmRequested = true;
    }

    public void CancelPlacement()
    {
        if (isActiveAndEnabled && !paused && Time.timeScale > 0f)
            ClearSelection();
    }

    private void Update()
    {
        if (paused || Time.timeScale == 0f)
            return;

        if (!selected && (drag >= 0 || outlines.Count > 0))
            ClearSelection();
        if (selected && !selected.gameObject.activeInHierarchy)
            ClearSelection();
        UpdateTransfers();
        var mouse = Mouse.current;
        if (mouse == null)
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
            // Keep the current preview when clicking outside. Use the Cancel button to cancel.
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
        if (paused || Time.timeScale == 0f)
        {
            DrawHandlesAndOutline();
            return;
        }
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
        if (confirmRequested && Time.timeScale > 0f)
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
