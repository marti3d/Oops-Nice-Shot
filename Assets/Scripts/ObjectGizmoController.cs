using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(100)]
public sealed class ObjectGizmoController : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private LayerMask selectionRayLayers = Physics.DefaultRaycastLayers;
    [Header("Appearance")]
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private Shader gizmoShader;
    [Min(.01f)][SerializeField] private float gizmoWorldSize = 1f;
    [SerializeField] private bool autoSizeGizmo = true;
    [Min(.01f)][SerializeField] private float minimumGizmoSize = .25f;
    private float selectedObjectSize = 1f;
    private float EffectiveGizmoSize => autoSizeGizmo
        ? Mathf.Max(minimumGizmoSize, gizmoWorldSize * selectedObjectSize)
        : gizmoWorldSize;
    [SerializeField] private bool showMove = true, showRotate = true;
    [Min(.01f)][SerializeField] private float arrowLength = 1.4f;
    [Min(.001f)][SerializeField] private float arrowShaftThickness = .02f, arrowHeadWidth = .075f;
    [Range(.05f, .8f)][SerializeField] private float arrowHeadLength = .22f;
    [Min(.01f)][SerializeField] private float ringRadius = .85f;
    [Min(.001f)][SerializeField] private float ringThickness = .025f;
    [Range(24, 192)][SerializeField] private int ringSegments = 96;
    [SerializeField] private Color xColor = new(.95f, .2f, .2f), yColor = new(.3f, .9f, .25f), zColor = new(.25f, .45f, 1f);
    [SerializeField] private Color hoverColor = Color.white, activeColor = Color.yellow;
    [Range(0, 1)][SerializeField] private float gizmoOpacity = 1;
    [Min(2)][SerializeField] private float arrowHitRadiusPixels = 10, ringHitRadiusPixels = 8;
    [Header("Adaptive Thickness")]
    [SerializeField] private bool adaptiveGizmoThickness = true;
    [Min(1)][SerializeField] private float minimumArrowPixels = 3f;
    [Min(1)][SerializeField] private float minimumHeadPixels = 9f;
    [Min(1)][SerializeField] private float minimumRingPixels = 3f;
    [Range(1, 20)][SerializeField] private float maximumThicknessMultiplier = 10f;
    [Header("Manipulation")]
    [Min(.01f)][SerializeField] private float maxDragSpeed = 15, maxRotationSpeed = 360, rotationSensitivity = 1;
    [Min(.001f)][SerializeField] private float moveSnap = .5f, rotationSnap = 15;
    [Header("Pickup Lift")]
    [Min(0f)][SerializeField] private float pickupLiftHeight = 0.5f;
    [Min(0.01f)][SerializeField] private float pickupLiftDuration = 0.25f;
    private float liftElapsed, liftTotal;
    private float remainingLift;
    [Header("Collision checks")]
    [SerializeField] private LayerMask obstacleLayers = ~0;
    [Min(0)][SerializeField] private float collisionClearance = .005f;
    [Min(.001f)][SerializeField] private float translationStep = .025f;
    [Range(.1f, 5)][SerializeField] private float rotationStep = 1;
    private readonly Dictionary<Rigidbody, Pose> spawnPoses = new();
    private readonly Dictionary<MeshFilter, Mesh> outlines = new();
    private static readonly Vector3[] AxisDirs = { Vector3.right, Vector3.up, Vector3.forward };
    private readonly Material[] materials = new Material[6];
    private Camera cam;
    private Material outlineMask, outlineOverlay;
    private Rigidbody selected;
    public Rigidbody SelectedObject => selected;
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
    private bool Snap => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    private void Reset() { targetCamera = Camera.main; gizmoShader = Shader.Find("MiniGolf/GizmoAlwaysOnTop"); }
    private void Awake()
    {
        cam = targetCamera != null ? targetCamera : Camera.main;
        if (!cam) { enabled = false; return; }
        if (!cameraController) cam.TryGetComponent(out cameraController);
        Shader shader = gizmoShader ? gizmoShader : Shader.Find("MiniGolf/GizmoAlwaysOnTop");
        if (!shader) { Debug.LogError("Assign gizmo shader", this); enabled = false; return; }
        for (int i = 0; i < 6; i++) materials[i] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        RefreshGizmoMeshes();
    }
    private void Start() { foreach (var rb in FindObjectsByType<Rigidbody>()) RegisterSpawn(rb); }
    private void OnDisable() => ClearSelection();
    private void OnApplicationFocus(bool focus) { if (!focus) ClearSelection(); }
    private void OnDestroy() { ClearSelection(); if (outlineMask) Destroy(outlineMask); if (outlineOverlay) Destroy(outlineOverlay); foreach (var m in materials) if (m) Destroy(m); if (arrowMesh) Destroy(arrowMesh); if (ringMesh) Destroy(ringMesh); }
    private void Update()
    {
        if (!selected && (drag >= 0 || outlines.Count > 0)) ClearSelection();
        if (selected && !selected.gameObject.activeInHierarchy) ClearSelection();
        var mouse = Mouse.current;
        if (mouse == null || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) { ClearSelection(); return; }
        Vector2 pos = mouse.position.ReadValue();
        if (drag >= 0) { if (!mouse.leftButton.isPressed) EndDrag(); else UpdateTarget(pos); return; }
        bool ui = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        hover = selected && !ui ? Pick(pos) : -1;
        if (!ui && mouse.leftButton.wasPressedThisFrame)
        {
            if (hover >= 0) { BeginDrag(hover, pos); return; }
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, Mathf.Infinity, selectionRayLayers, QueryTriggerInteraction.Ignore) && hit.rigidbody && (hit.rigidbody == selected || (!hit.rigidbody.isKinematic && (selectableLayers.value & (1 << hit.rigidbody.gameObject.layer)) != 0))) Select(hit.rigidbody); else ClearSelection();
        }
    }
    private void Select(Rigidbody rb)
    {
        if (rb == selected) return;
        ClearSelection(); RegisterSpawn(rb);
        selected = rb; savedGravity = rb.useGravity; savedCcd = rb.collisionDetectionMode; savedInterpolation = rb.interpolation;
        shapes = rb.GetComponentsInChildren<Collider>();
        selectedObjectSize = CalculateObjectSize(rb);
        rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete; rb.interpolation = RigidbodyInterpolation.None; rb.isKinematic = true;
        remainingLift = liftTotal = pickupLiftHeight;
        liftElapsed = 0f;
        if (outlineMaterial == null) return;
        foreach (MeshFilter f in rb.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || !f.TryGetComponent<MeshRenderer>(out var renderer)) continue;
            if (!f.sharedMesh.isReadable)
            {
                Debug.LogWarning("Enable Read/Write on this mesh for a welded outline.", f);
                continue;
            }
            Mesh mesh = Instantiate(f.sharedMesh);
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            if (normals.Length != vertices.Length) { mesh.RecalculateNormals(); normals = mesh.normals; }
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
                if (n.sqrMagnitude > 0.000001f) normals[i] = n.normalized;
            }
            mesh.normals = normals;
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
            if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
            Bounds local = renderer.localBounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1,
                                (i & 2) == 0 ? -1 : 1,
                                (i & 4) == 0 ? -1 : 1));
                Vector3 point = inverseRotation * (renderer.transform.TransformPoint(corner) - rb.transform.position);
                if (!found) { combined = new Bounds(point, Vector3.zero); found = true; }
                else combined.Encapsulate(point);
            }
        }
        if (!found) return 1f;
        Vector3 size = combined.size;
        return Mathf.Max(.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
    }

    private void ClearSelection()
    {
        EndDrag();
        remainingLift = 0f;
        selectedObjectSize = 1f;
        if (selected) { selected.isKinematic = false; selected.collisionDetectionMode = savedCcd; selected.interpolation = savedInterpolation; selected.useGravity = savedGravity; selected.linearVelocity = Vector3.zero; selected.angularVelocity = Vector3.zero; selected.WakeUp(); }
        foreach (var m in outlines.Values) if (m) Destroy(m); outlines.Clear(); selected = null; shapes = null; hover = -1;
    }
    private Quaternion AxisRotation(int i) => Quaternion.FromToRotation(Vector3.forward, AxisDirs[i]);
    private void LateUpdate()
    {
        if (!selected) return;
        if (drag < 0) UpdatePickupLift();
        RefreshGizmoMeshes();
        if (outlineMaterial && outlines.Count > 0)
        {
            if (!outlineMask || outlineMask.shader != outlineMaterial.shader)
            {
                if (outlineMask) Destroy(outlineMask);
                if (outlineOverlay) Destroy(outlineOverlay);
                outlineMask = new Material(outlineMaterial) { hideFlags = HideFlags.HideAndDontSave };
                outlineOverlay = new Material(outlineMaterial) { hideFlags = HideFlags.HideAndDontSave };
            }
            outlineMask.CopyPropertiesFromMaterial(outlineMaterial);
            outlineOverlay.CopyPropertiesFromMaterial(outlineMaterial);
            outlineMask.SetFloat("_MaskMode", 1);
            outlineMask.SetFloat("_Cull", 0);
            outlineMask.SetFloat("_ColorMask", 0);
            outlineMask.SetFloat("_StencilComp", 8);
            outlineMask.SetFloat("_StencilPass", 2);
            outlineMask.renderQueue = 3998;
            outlineOverlay.SetFloat("_MaskMode", 0);
            outlineOverlay.SetFloat("_Cull", 1);
            outlineOverlay.SetFloat("_ColorMask", 15);
            outlineOverlay.SetFloat("_StencilComp", 6);
            outlineOverlay.SetFloat("_StencilPass", 0);
            outlineOverlay.renderQueue = 3999;
            foreach (var pair in outlines)
                if (pair.Key && pair.Key.gameObject.activeInHierarchy && pair.Key.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
                    for (int sub = 0; sub < pair.Value.subMeshCount; sub++)
                    {
                        Graphics.DrawMesh(pair.Value, pair.Key.transform.localToWorldMatrix, outlineMask, pair.Key.gameObject.layer, cam, sub, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                        Graphics.DrawMesh(pair.Value, pair.Key.transform.localToWorldMatrix, outlineOverlay, pair.Key.gameObject.layer, cam, sub, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
                    }
        }
        for (int i = 0; i < 6; i++)
        {
            if (i < 3 ? !showMove : !showRotate) continue;
            Color c = i % 3 == 0 ? xColor : i % 3 == 1 ? yColor : zColor;
            if (i == drag) c = activeColor; else if (i == hover) c = hoverColor; c.a *= gizmoOpacity;
            materials[i].SetColor("_BaseColor", c);
            float scale = EffectiveGizmoSize * (i < 3 ? arrowLength : 1);
            Graphics.DrawMesh(i < 3 ? arrowMesh : ringMesh, Matrix4x4.TRS(selected.position, AxisRotation(i % 3), Vector3.one * scale), materials[i], 0, cam, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
    private Vector3 RingPoint(int i, float a) => selected.position + AxisRotation(i) * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * ringRadius * EffectiveGizmoSize;
    private int Pick(Vector2 mouse)
    {
        int best = -1; float score = 1;
        for (int i = 0; i < 6; i++)
        {
            if (i < 3 ? !showMove : !showRotate) continue;
            int n = i < 3 ? 1 : ringSegments;
            for (int j = 0; j < n; j++)
            {
                Vector3 a = cam.WorldToScreenPoint(i < 3 ? selected.position : RingPoint(i % 3, 2 * Mathf.PI * j / n));
                Vector3 b = cam.WorldToScreenPoint(i < 3 ? selected.position + AxisDirs[i] * EffectiveGizmoSize * arrowLength : RingPoint(i % 3, 2 * Mathf.PI * (j + 1) / n));
                if (a.z <= 0 || b.z <= 0) continue;
                Vector2 ab = (Vector2)b - (Vector2)a; if (ab.sqrMagnitude < .001f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(mouse - (Vector2)a, ab) / ab.sqrMagnitude);
                float d = Vector2.Distance(mouse, (Vector2)a + ab * t) / (i < 3 ? arrowHitRadiusPixels : ringHitRadiusPixels);
                if (d < score) { score = d; best = i; }
            }
        }
        return best;
    }
    private bool AxisParam(Vector2 mouse, out float s)
    {
        Ray r = cam.ScreenPointToRay(mouse); Vector3 w = origin - r.origin; float b = Vector3.Dot(axis, r.direction), den = 1 - b * b;
        s = 0; if (den < .001f) return false; s = (b * Vector3.Dot(r.direction, w) - Vector3.Dot(axis, w)) / den; return true;
    }
    private bool Radial(Vector2 mouse, out Vector3 radial)
    {
        Ray ray = cam.ScreenPointToRay(mouse); Plane plane = new(axis, origin); radial = Vector3.zero;
        if (!plane.Raycast(ray, out float t)) return false; radial = ray.GetPoint(t) - origin; if (radial.sqrMagnitude < .000001f) return false; radial.Normalize(); return true;
    }
    private void BeginDrag(int handle, Vector2 mouse)
    {
        origin = startPosition = selected.position; startRotation = selected.rotation; axis = AxisDirs[handle % 3];
        if (handle < 3 ? !AxisParam(mouse, out grab) : !Radial(mouse, out lastRadial)) return;
        remainingLift = 0f;
        drag = handle; target = applied = angle = 0;
        if (cameraController) { savedCameraInput = cameraController.InputEnabled; cameraController.InputEnabled = false; }
    }
    private void UpdateTarget(Vector2 mouse)
    {
        if (drag < 3) { if (!AxisParam(mouse, out float s)) return; target = s - grab; if (Snap) target = Mathf.Round(target / moveSnap) * moveSnap; }
        else { if (!Radial(mouse, out var r)) return; angle += Vector3.SignedAngle(lastRadial, r, axis) * rotationSensitivity; lastRadial = r; target = Snap ? Mathf.Round(angle / rotationSnap) * rotationSnap : angle; }
    }
    private void EndDrag() { if (drag >= 0 && cameraController) cameraController.InputEnabled = savedCameraInput; drag = -1; }
    private void UpdatePickupLift()
    {
        if (remainingLift <= 0f) return;
        liftElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(liftElapsed / Mathf.Max(.01f, pickupLiftDuration));
        // Smoothstep: gentle acceleration and deceleration.
        float eased = t * t * (3f - 2f * t);
        float distance = Mathf.Clamp(liftTotal * eased - (liftTotal - remainingLift), 0f, remainingLift);
        if (t >= 1f) distance = remainingLift;
        if (distance <= 0f) return;
        int steps = Mathf.Max(1, Mathf.CeilToInt(distance / Mathf.Max(.001f, translationStep)));
        float stepDistance = distance / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 nextPosition = selected.position + Vector3.up * stepDistance;
            if (!CanPlace(nextPosition, selected.rotation))
            {
                remainingLift = 0f;
                return;
            }
            selected.position = nextPosition;
            Physics.SyncTransforms();
        }
        remainingLift = t >= 1f ? 0f : Mathf.Max(0f, remainingLift - distance);
    }

    private void FixedUpdate()
    {
        if (!selected) return;
        if (drag < 0) return;
        bool move = drag < 3;
        float end = Mathf.MoveTowards(applied, target, (move ? maxDragSpeed : maxRotationSpeed) * Time.fixedDeltaTime);
        float step = move ? translationStep : rotationStep;
        int count = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(end - applied) / Mathf.Max(.001f, step)), 1, 128);
        float from = applied;
        for (int i = 1; i <= count; i++)
        {
            float next = Mathf.Lerp(from, end, (float)i / count);
            Vector3 p = move ? startPosition + axis * next : startPosition;
            Quaternion q = move ? startRotation : Quaternion.AngleAxis(next, axis) * startRotation;
            if (!CanPlace(p, q)) break;
            selected.position = p; selected.rotation = q; applied = next;
            Physics.SyncTransforms();
        }
    }
    // Conservative broad phase plus exact penetration tests for supported selected shapes.
    private bool CanPlace(Vector3 p, Quaternion q)
    {
        Vector3 oldP = selected.position; Quaternion delta = q * Quaternion.Inverse(selected.rotation);
        foreach (var c in shapes)
        {
            if (!c || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy || c.attachedRigidbody != selected) continue;
            if (!(c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex))) return false;
            Vector3 cp = p + delta * (c.transform.position - oldP); Quaternion cq = delta * c.transform.rotation;
            Bounds b = c.bounds; Vector3 center = p + delta * (b.center - oldP);
            float radius = b.extents.magnitude + collisionClearance;
            foreach (var other in Physics.OverlapSphere(center, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                if (!other || other.attachedRigidbody == selected || Physics.GetIgnoreLayerCollision(c.gameObject.layer, other.gameObject.layer) || Physics.GetIgnoreCollision(c, other)) continue;
                bool overlap = Physics.ComputePenetration(c, cp, cq, other, other.transform.position, other.transform.rotation, out _, out float depth);
                if (overlap && depth > 0.00001f)
                {
                    // Allow an existing contact to be escaped, never deepen it.
                    if (!Physics.ComputePenetration(c, c.transform.position, c.transform.rotation, other, other.transform.position, other.transform.rotation, out _, out float before) || depth >= before - 0.00001f) return false;
                }
                // Translation sweep prevents tunnelling and keeps clearance from new contacts.
            }
        }
        Vector3 displacement = p - oldP;
        if (displacement.sqrMagnitude > 1e-12f)
        {
            float distance = displacement.magnitude;
            foreach (var hit in selected.SweepTestAll(displacement / distance, distance + collisionClearance, QueryTriggerInteraction.Ignore))
            {
                var other = hit.collider; if (!other || other.attachedRigidbody == selected || (obstacleLayers.value & (1 << other.gameObject.layer)) == 0) continue;
                bool relevant = false; foreach (var c in shapes) if (c && c.enabled && !c.isTrigger && c.attachedRigidbody == selected && !Physics.GetIgnoreLayerCollision(c.gameObject.layer, other.gameObject.layer) && !Physics.GetIgnoreCollision(c, other)) { relevant = true; break; }
                if (!relevant) continue;
                if (hit.distance > 0.00001f && hit.distance < distance + collisionClearance && Vector3.Dot(hit.normal, displacement) < -0.00001f) return false;
            }
        }
        return true;
    }
    public void RegisterSpawn(Rigidbody rb) { if (rb && !rb.isKinematic && !spawnPoses.ContainsKey(rb) && (selectableLayers.value & (1 << rb.gameObject.layer)) != 0) spawnPoses.Add(rb, new Pose(rb.position, rb.rotation)); }
    public bool Respawn(Rigidbody rb) { if (!rb || !spawnPoses.TryGetValue(rb, out var pose)) return false; if (rb == selected) ClearSelection(); if (rb.isKinematic) return false; rb.position = pose.position; rb.rotation = pose.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.WakeUp(); return true; }
    // Convert desired screen pixels to world units using the actual camera projection.
    private void RefreshGizmoMeshes()
    {
        float shaft = arrowShaftThickness, head = arrowHeadWidth, ring = ringThickness;
        if (adaptiveGizmoThickness && selected && cam && cam.pixelHeight > 0)
        {
            float depth = Mathf.Max(cam.nearClipPlane, Vector3.Dot(selected.position - cam.transform.position, cam.transform.forward));
            float worldPerPixel = cam.orthographic
                ? 2f * cam.orthographicSize / cam.pixelHeight
                : 2f * depth * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * .5f) / cam.pixelHeight;
            float arrowScale = Mathf.Max(.0001f, EffectiveGizmoSize * arrowLength);
            float ringScale = Mathf.Max(.0001f, EffectiveGizmoSize);
            float cap = Mathf.Max(1f, maximumThicknessMultiplier);
            shaft = Mathf.Clamp(minimumArrowPixels * worldPerPixel / (2f * arrowScale), shaft, shaft * cap);
            head = Mathf.Clamp(minimumHeadPixels * worldPerPixel / (2f * arrowScale), head, head * cap);
            ring = Mathf.Clamp(minimumRingPixels * worldPerPixel / ringScale, ring, ring * cap);
            // Avoid a shaft wider than its arrowhead or a ring collapsing into a disk.
            shaft = Mathf.Min(shaft, Mathf.Max(arrowShaftThickness, head * .7f));
            ring = Mathf.Min(ring, Mathf.Max(ringThickness, ringRadius * .5f));
        }
        if (!arrowMesh || ThicknessChanged(shaft, drawnShaft) || ThicknessChanged(head, drawnHead) || !Mathf.Approximately(arrowHeadLength, drawnHeadLength))
        {
            if (arrowMesh) Destroy(arrowMesh);
            arrowMesh = BuildArrowMesh(shaft, head);
            drawnShaft = shaft; drawnHead = head; drawnHeadLength = arrowHeadLength;
        }
        if (!ringMesh || ThicknessChanged(ring, drawnRing) || !Mathf.Approximately(ringRadius, drawnRadius) || ringSegments != drawnSegments)
        {
            if (ringMesh) Destroy(ringMesh);
            ringMesh = BuildRing(ring);
            drawnRing = ring; drawnRadius = ringRadius; drawnSegments = ringSegments;
        }
    }
    private static bool ThicknessChanged(float a, float b) => Mathf.Abs(a - b) > Mathf.Max(.00001f, Mathf.Abs(b) * .025f);
    private Mesh BuildRing(float thickness)
    {
        var v = new List<Vector3>(); var t = new List<int>(); int n = Mathf.Max(24, ringSegments), sides = 8;
        for (int i = 0; i < n; i++) { float a = 2 * Mathf.PI * i / n; Vector3 radial = new(Mathf.Cos(a), Mathf.Sin(a), 0); for (int j = 0; j < sides; j++) { float b = 2 * Mathf.PI * j / sides; v.Add(radial * (ringRadius + thickness * .5f * Mathf.Cos(b)) + Vector3.forward * (thickness * .5f * Mathf.Sin(b))); } }
        for (int i = 0; i < n; i++) for (int j = 0; j < sides; j++) { int a = i * sides + j, b = i * sides + (j + 1) % sides, c = ((i + 1) % n) * sides + j, d = ((i + 1) % n) * sides + (j + 1) % sides; t.AddRange(new[] { a, b, c, b, d, c }); }
        Mesh m = new() { name = "GizmoRing" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds(); return m;
    }
    private Mesh BuildArrowMesh(float shaftR, float headR)
    {
        const int seg = 16;
        float shaftLen = 1f - arrowHeadLength;

        Vector3[] v = new Vector3[seg * 3 + 2];
        for (int i = 0; i < seg; i++)
        {
            float ang = 2f * Mathf.PI * i / seg;
            float cx = Mathf.Cos(ang), cy = Mathf.Sin(ang);
            v[i] = new Vector3(cx * shaftR, cy * shaftR, 0f);
            v[seg + i] = new Vector3(cx * shaftR, cy * shaftR, shaftLen);
            v[2 * seg + i] = new Vector3(cx * headR, cy * headR, shaftLen);
        }
        v[3 * seg] = new Vector3(0f, 0f, 1f);
        v[3 * seg + 1] = new Vector3(0f, 0f, shaftLen);

        List<int> t = new List<int>();
        for (int i = 0; i < seg; i++)
        {
            int j = (i + 1) % seg;
            t.Add(i); t.Add(seg + i); t.Add(seg + j);
            t.Add(i); t.Add(seg + j); t.Add(j);
            t.Add(2 * seg + i); t.Add(3 * seg); t.Add(2 * seg + j);
            t.Add(3 * seg + 1); t.Add(2 * seg + j); t.Add(2 * seg + i);
        }

        Mesh mesh = new Mesh { name = "GizmoArrow" };
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
