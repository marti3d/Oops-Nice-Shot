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
    [Header("Gizmo Controls")]
    [SerializeField] private Key moveKey = Key.Q, rotateKey = Key.W, spaceKey = Key.E;
    [SerializeField] private bool rotationTool, localSpace;
    [Header("Gizmo Visuals")]
    [SerializeField] private bool limitScreenSize = true;
    public enum GizmoSizeMode { Adaptive, WorldSize, ConstantScreenSize, ConstantScreenSizePerObject }
    [SerializeField] private GizmoSizeMode sizeMode = GizmoSizeMode.Adaptive;
    [Min(1)][SerializeField] private float constantScreenPixels = 85;
    [Min(.01f)][SerializeField] private float referenceObjectSize = 1;
    [Range(1, 2)][SerializeField] private float planeSizeMultiplier = 1.4f;
    [Range(.005f,.1f)][SerializeField] private float planeBorder = .035f;
    [SerializeField] private bool facePlanesTowardsCamera = true;
    [SerializeField] private bool fullRingWhileDragging = true;
    private Mesh planeBorderMesh, fullRingMesh;
    private float drawnPlaneBorder;
    private Material planeBorderMaterial;
    private MaterialPropertyBlock planeBorderBlock;
    private float PlaneSize => planeSize * planeSizeMultiplier;
    private readonly Color[] visualColors = new Color[9];
    [Min(1)][SerializeField] private float minimumGizmoPixels = 60, maximumGizmoPixels = 120;
    [Range(0,1)][SerializeField] private float idleOpacity = .55f, inactiveOpacity = .1f;
    [Min(.01f)][SerializeField] private float fadeDuration = .12f;
    [Min(1)][SerializeField] private float hoverScale = 1.1f;
    [Range(30,360)][SerializeField] private float arcDegrees = 180;
    [Range(.05f,.5f)][SerializeField] private float planeOffset = .15f, planeSize = .23f;
    [SerializeField] private bool showReadout = true;
    [SerializeField] private bool showToolLabel = true, showSpaceLabel = true;
    [SerializeField] private bool showMovementDistance = true, showRotationAngle = true, showSnapIndicator = true;
    private readonly float[] visualAlpha = new float[9];
    private readonly float[] visualScale = new float[9];
    private Mesh planeMesh;
    private Vector3 planeU, planeV, planeGrab, planeTarget, planeApplied;
    private Quaternion dragFrame;
    private float drawnArc = -1;
    private float EffectiveGizmoSize {
        get {
            float size = autoSizeGizmo ? Mathf.Max(minimumGizmoSize, gizmoWorldSize * selectedObjectSize) : gizmoWorldSize;
            if (sizeMode == GizmoSizeMode.WorldSize || !cam || !ghost || cam.pixelHeight <= 0) return size;
            float depth = Mathf.Max(cam.nearClipPlane, Vector3.Dot(ghost.Root.position-cam.transform.position,cam.transform.forward));
            float pixel = cam.orthographic ? 2*cam.orthographicSize/cam.pixelHeight : 2*depth*Mathf.Tan(cam.fieldOfView*Mathf.Deg2Rad*.5f)/cam.pixelHeight;
            if (sizeMode == GizmoSizeMode.ConstantScreenSize) return constantScreenPixels * pixel;
            if (sizeMode == GizmoSizeMode.ConstantScreenSizePerObject)
                return Mathf.Max(minimumGizmoSize, constantScreenPixels * selectedObjectSize / Mathf.Max(.01f, referenceObjectSize)) * pixel;
            if (!limitScreenSize) return size;
            return Mathf.Clamp(size, minimumGizmoPixels*pixel, Mathf.Max(minimumGizmoPixels,maximumGizmoPixels)*pixel);
        }
    }
    [SerializeField] private bool showMove = true, showRotate = true;
    [Min(.01f)][SerializeField] private float arrowLength = 1.4f;
    [Min(.001f)][SerializeField] private float arrowShaftThickness = .02f, arrowHeadWidth = .075f;
    [Range(.05f, .8f)][SerializeField] private float arrowHeadLength = .22f;
    [Min(.01f)][SerializeField] private float ringRadius = .85f;
    [Min(.001f)][SerializeField] private float ringThickness = .025f;
    [Range(24, 192)][SerializeField] private int ringSegments = 96;
    [SerializeField] private Color xColor = new(.94f, .47f, .47f), yColor = new(.55f, .83f, .49f), zColor = new(.47f, .66f, .94f);
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
    private readonly Material[] materials = new Material[9];
    private Camera cam;
    private Material outlineMask, outlineOverlay;
    private Rigidbody selected;
    public Rigidbody SelectedObject => selected;
    public Transform PreviewTransform => ghost ? ghost.Root : null;
    public bool PreviewValid => selected && ghost && CanPlace(ghost.Root.position, ghost.Root.rotation);
    [Header("Ghost Placement")]
    [SerializeField] private Shader ghostShader;
    [SerializeField] private Color validColor = Color.white, invalidColor = Color.red, noGroundColor = Color.yellow;
    [Range(0,1)][SerializeField] private float ghostOpacity = .35f;
    [Min(.01f)][SerializeField] private float transferSpeed = 25f;
    [Min(0)][SerializeField] private float releaseDelay = .5f;
    [Min(0)][SerializeField] private float rejectDuration = .25f, rejectShake = .06f;
    [SerializeField] private GhostGroundLine groundLine;
    private GhostPreview ghost;
    private float rejectedUntil;
    public bool HasGround { get; set; } = true;
    public Color PreviewColor => Time.time < rejectedUntil || !PreviewValid ? invalidColor : HasGround ? validColor : noGroundColor;
    private readonly List<Transfer> transfers = new();
    private sealed class Transfer {
        public Rigidbody body; public GhostPreview preview; public Pose start, end;
        public float elapsed, duration, hold; public bool arrived, gravity, collisions;
        public CollisionDetectionMode ccd; public RigidbodyInterpolation interpolation;
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
    private bool Snap => Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
    private void Reset() { targetCamera = Camera.main; gizmoShader = Shader.Find("MiniGolf/GizmoAlwaysOnTop"); }
    private void Awake()
    {
        planeBorderBlock = new MaterialPropertyBlock();
        cam = targetCamera != null ? targetCamera : Camera.main;
        if (!cam) { enabled = false; return; }
        if (!cameraController) cam.TryGetComponent(out cameraController);
        Shader shader = gizmoShader ? gizmoShader : Shader.Find("MiniGolf/GizmoAlwaysOnTop");
        if (!shader) { Debug.LogError("Assign gizmo shader", this); enabled = false; return; }
        for (int i = 0; i < 9; i++) materials[i] = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        planeMesh = new Mesh { name = "GizmoPlane" };
        planeMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.right+Vector3.up };
        planeMesh.triangles = new[] { 0,1,2,2,1,3,2,1,0,3,1,2 }; planeMesh.RecalculateBounds();
        planeBorderMesh = BuildPlaneBorder(); drawnPlaneBorder=planeBorder;
        planeBorderMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        for(int i=0;i<9;i++) { visualAlpha[i]=idleOpacity; visualScale[i]=1; visualColors[i]=i%3==0?xColor:i%3==1?yColor:zColor; }
        RefreshGizmoMeshes();
    }
    private void Start() { foreach (var rb in FindObjectsByType<Rigidbody>()) RegisterSpawn(rb); }
    private void OnDisable() { ClearSelection(); foreach (var t in transfers) FinishTransfer(t); transfers.Clear(); }
    private void OnApplicationFocus(bool focus) { if (!focus) ClearSelection(); }
    private void OnDestroy() { ClearSelection(); if (outlineMask) Destroy(outlineMask); if (outlineOverlay) Destroy(outlineOverlay); foreach (var m in materials) if (m) Destroy(m); if (arrowMesh) Destroy(arrowMesh); if (ringMesh) Destroy(ringMesh); if (planeMesh) Destroy(planeMesh); if (planeBorderMesh) Destroy(planeBorderMesh); if (fullRingMesh) Destroy(fullRingMesh); if (planeBorderMaterial) Destroy(planeBorderMaterial); }
    private void Update()
    {
        if (!selected && (drag >= 0 || outlines.Count > 0)) ClearSelection();
        if (selected && !selected.gameObject.activeInHierarchy) ClearSelection();
        UpdateTransfers();
        var keyboard = Keyboard.current;
        if (keyboard != null && drag < 0) {
            if (moveKey != Key.None && keyboard[moveKey].wasPressedThisFrame) { rotationTool=false; hover=-1; }
            if (rotateKey != Key.None && keyboard[rotateKey].wasPressedThisFrame) { rotationTool=true; hover=-1; }
            if (spaceKey != Key.None && keyboard[spaceKey].wasPressedThisFrame) { localSpace=!localSpace; hover=-1; }
        }
        if (selected && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) { Confirm(); return; }
        var mouse = Mouse.current;
        if (mouse == null || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)) { ClearSelection(); return; }
        Vector2 pos = mouse.position.ReadValue();
        if (drag >= 0) { if (!mouse.leftButton.isPressed) EndDrag(); else UpdateTarget(pos); return; }
        bool ui = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        hover = selected && !ui ? Pick(pos) : -1;
        if (!ui && mouse.leftButton.wasPressedThisFrame)
        {
            if (hover >= 0) { BeginDrag(hover, pos); return; }
            // Keep the current preview when clicking outside; Escape cancels.
            if (Physics.Raycast(cam.ScreenPointToRay(pos), out var hit, Mathf.Infinity, selectionRayLayers, QueryTriggerInteraction.Ignore) && hit.rigidbody && (hit.rigidbody == selected || (!hit.rigidbody.isKinematic && (selectableLayers.value & (1 << hit.rigidbody.gameObject.layer)) != 0))) Select(hit.rigidbody);
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
        ghost = GhostPreview.Create(rb, ghostShader, ghostOpacity);
        if (!ghost) { ClearSelection(); return; }
        HasGround = true; rejectedUntil = 0;
        remainingLift = liftTotal = pickupLiftHeight;
        liftElapsed = 0f;
        if (outlineMaterial == null) return;
        foreach (MeshFilter f in ghost.Root.GetComponentsInChildren<MeshFilter>())
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
        if (ghost) Destroy(ghost.gameObject); ghost = null;
        remainingLift = 0f;
        selectedObjectSize = 1f;
        if (selected) { selected.isKinematic = false; selected.collisionDetectionMode = savedCcd; selected.interpolation = savedInterpolation; selected.useGravity = savedGravity; selected.linearVelocity = Vector3.zero; selected.angularVelocity = Vector3.zero; selected.WakeUp(); }
        foreach (var m in outlines.Values) if (m) Destroy(m); outlines.Clear(); selected = null; shapes = null; hover = -1;
    }
    private Quaternion Frame => drag >= 0 ? dragFrame : localSpace && ghost ? ghost.Root.rotation : Quaternion.identity;
    private Vector3 Direction(int i) => Frame * AxisDirs[i];
    private Quaternion AxisRotation(int i) => Frame * Quaternion.FromToRotation(Vector3.forward, AxisDirs[i]);
    private bool Visible(int i) => rotationTool ? showRotate && i>=3 && i<6 : showMove && (i<3 || i>=6);
    private Vector3 PlaneDirection(int i) {
        Vector3 d = Direction(i);
        if (!facePlanesTowardsCamera || drag >= 0) return d * (drag >= 0 ? planeSigns[i] : 1f);
        Vector3 view = cam.orthographic ? -cam.transform.forward : cam.transform.position - ghost.Root.position;
        return Vector3.Dot(d, view) < 0 ? -d : d;
    }
    private readonly float[] planeSigns = {1,1,1};
    private Vector3 PlanePoint(int i, float u, float v) => ghost.Root.position + EffectiveGizmoSize * (PlaneDirection((i+1)%3)*(planeOffset+u*PlaneSize)+PlaneDirection((i+2)%3)*(planeOffset+v*PlaneSize));
    private Mesh BuildPlaneBorder() {
        var v = new List<Vector3>(); var t = new List<int>();
        void Quad(float x,float y,float w,float h) {
            int n=v.Count; v.Add(new Vector3(x,y,0)); v.Add(new Vector3(x+w,y,0)); v.Add(new Vector3(x,y+h,0)); v.Add(new Vector3(x+w,y+h,0));
            t.AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3,n+2,n+1,n,n+3,n+1,n+2});
        }
        float b=planeBorder; Quad(0,0,1,b); Quad(0,1-b,1,b); Quad(0,b,b,1-2*b); Quad(1-b,b,b,1-2*b);
        var m=new Mesh {name="GizmoPlaneBorder"}; m.SetVertices(v); m.SetTriangles(t,0); m.RecalculateBounds(); return m;
    }
    private float ArcStart(int i) {
        Vector3 view = cam.orthographic ? -cam.transform.forward : cam.transform.position-ghost.Root.position;
        Vector3 p = Quaternion.Inverse(AxisRotation(i))*view;
        return Mathf.Atan2(p.y,p.x)-arcDegrees*Mathf.Deg2Rad*.5f;
    }
    private void LateUpdate()
    {
        if (!selected || !ghost) return;
        if (drag < 0) UpdatePickupLift();
        ghost.SetAppearance(PreviewColor, Time.time < rejectedUntil ? Mathf.Sin(Time.time * 90f) * rejectShake : 0f);
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
            if (outlineOverlay.HasProperty("_OutlineColor")) outlineOverlay.SetColor("_OutlineColor", PreviewColor);
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
        for (int i=0;i<9;i++) {
            if (!Visible(i)) continue;
            float goal = drag>=0 ? (drag==i ? 1 : inactiveOpacity) : hover==i ? 1 : idleOpacity;
            visualAlpha[i]=Mathf.MoveTowards(visualAlpha[i],goal,Time.deltaTime/Mathf.Max(.01f,fadeDuration));
            visualScale[i]=Mathf.MoveTowards(visualScale[i],hover==i && drag<0 ? hoverScale : 1,Time.deltaTime/Mathf.Max(.01f,fadeDuration));
            Color c=i%3==0?xColor:i%3==1?yColor:zColor;
            if(i==drag)c=activeColor; else if(i==hover)c=hoverColor;
            visualColors[i]=Color.Lerp(visualColors[i],c,1-Mathf.Exp(-Time.deltaTime/Mathf.Max(.01f,fadeDuration)*4));
            c=visualColors[i]; c.a*=gizmoOpacity*visualAlpha[i];
            materials[i].SetColor("_BaseColor",c);
            Matrix4x4 matrix;
            Mesh mesh;
            if(i>=6) {
                int n=i%3; Vector3 u=PlaneDirection((n+1)%3),v=PlaneDirection((n+2)%3);
                matrix=Matrix4x4.TRS(PlanePoint(n,0,0),Quaternion.LookRotation(Vector3.Cross(u,v),v),Vector3.one*EffectiveGizmoSize*PlaneSize);
                mesh=planeMesh;
                Color border=c; border.a=Mathf.Clamp01(c.a*1.5f);
                // Per-handle property block avoids shared material color changes.
                planeBorderBlock.SetColor("_BaseColor",border);
                Graphics.DrawMesh(planeBorderMesh,matrix,planeBorderMaterial,0,cam,0,planeBorderBlock,UnityEngine.Rendering.ShadowCastingMode.Off,false);
                c.a*=.45f; materials[i].SetColor("_BaseColor",c);
            } else {
                float scale=EffectiveGizmoSize*(i<3?arrowLength:1)*visualScale[i];
                Quaternion q=AxisRotation(i%3);
                if(i>=3) q*=Quaternion.AngleAxis(ArcStart(i%3)*Mathf.Rad2Deg,Vector3.forward);
                matrix=Matrix4x4.TRS(ghost.Root.position,q,Vector3.one*scale); mesh=i<3?arrowMesh:(fullRingWhileDragging && drag==i ? fullRingMesh : ringMesh);
            }
            Graphics.DrawMesh(mesh,matrix,materials[i],0,cam,0,null,UnityEngine.Rendering.ShadowCastingMode.Off,false);
        }
    }
    private Vector3 RingPoint(int i,float a) => ghost.Root.position+AxisRotation(i)*new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*ringRadius*EffectiveGizmoSize*visualScale[i+3];
    private bool PlaneHit(int i,Vector2 mouse) {
        Vector3 u=PlaneDirection((i+1)%3),v=PlaneDirection((i+2)%3),p=PlanePoint(i,0,0);
        Ray ray=cam.ScreenPointToRay(mouse);
        if(!new Plane(Vector3.Cross(u,v),p).Raycast(ray,out float t))return false;
        Vector3 d=ray.GetPoint(t)-p; float size=EffectiveGizmoSize*PlaneSize;
        float x=Vector3.Dot(d,u),y=Vector3.Dot(d,v);
        return x>=0 && y>=0 && x<=size && y<=size;
    }
    private int Pick(Vector2 mouse) {
        if(!ghost)return -1;
        if(!rotationTool && showMove) for(int i=0;i<3;i++) if(PlaneHit(i,mouse))return i+6;
        int best=-1; float score=1;
        for(int i=0;i<6;i++) {
            if(!Visible(i))continue;
            int n=i<3?1:ringSegments; float start=i<3?0:ArcStart(i%3), span=arcDegrees*Mathf.Deg2Rad;
            for(int j=0;j<n;j++) {
                Vector3 a=cam.WorldToScreenPoint(i<3?ghost.Root.position:RingPoint(i%3,start+span*j/n));
                Vector3 b=cam.WorldToScreenPoint(i<3?ghost.Root.position+Direction(i)*EffectiveGizmoSize*arrowLength*visualScale[i]:RingPoint(i%3,start+span*(j+1)/n));
                if(a.z<=0||b.z<=0)continue;
                Vector2 ab=(Vector2)b-(Vector2)a; if(ab.sqrMagnitude<.001f)continue;
                float t=Mathf.Clamp01(Vector2.Dot(mouse-(Vector2)a,ab)/ab.sqrMagnitude);
                float d=Vector2.Distance(mouse,(Vector2)a+ab*t)/(i<3?arrowHitRadiusPixels:ringHitRadiusPixels);
                if(d<score){score=d;best=i;}
            }
        }
        return best;
    }
    private bool PlaneDragPoint(Vector2 mouse,out Vector3 point) {
        Ray ray=cam.ScreenPointToRay(mouse); point=Vector3.zero;
        if(!new Plane(axis,origin).Raycast(ray,out float t))return false;
        point=ray.GetPoint(t);return true;
    }
    private void OnGUI() {
        if(!showReadout || !ghost || !cam)return;
        Vector3 p=cam.WorldToScreenPoint(ghost.Root.position); if(p.z<=0)return;
        string label=showToolLabel?(rotationTool?"ROTATE":"MOVE"):"";
        if(showSpaceLabel) label+=(label.Length>0?" / ":"")+(localSpace?"LOCAL":"WORLD");
        if(drag>=0 && (drag>=6 || drag<3 ? showMovementDistance : showRotationAngle))label+="  "+(drag>=6?planeApplied.magnitude.ToString("F2")+" m":drag<3?applied.ToString("+0.00;-0.00;0.00")+" m":applied.ToString("F1")+"°");
        if(Snap && showSnapIndicator)label+="  SNAP";
        GUI.Label(new Rect(p.x-100,Screen.height-p.y+25,260,25),label);
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
        for(int i=0;i<3;i++) planeSigns[i]=Vector3.Dot(PlaneDirection(i),Direction(i))<0?-1:1;
        origin = startPosition = ghost.Root.position; startRotation = ghost.Root.rotation; dragFrame = Frame; axis = dragFrame * AxisDirs[handle % 3];
        if(handle>=6) {
            planeU=dragFrame*AxisDirs[(handle+1)%3]; planeV=dragFrame*AxisDirs[(handle+2)%3];
            if(!PlaneDragPoint(mouse,out planeGrab))return;
            planeTarget=planeApplied=Vector3.zero;
        } else if (handle < 3 ? !AxisParam(mouse, out grab) : !Radial(mouse, out lastRadial)) return;
        remainingLift = 0f;
        drag = handle; target = applied = angle = 0;
        if (cameraController) { savedCameraInput = cameraController.InputEnabled; cameraController.InputEnabled = false; }
    }
    private void UpdateTarget(Vector2 mouse)
    {
        if(drag>=6) {
            if(!PlaneDragPoint(mouse,out Vector3 point))return;
            Vector3 d=point-planeGrab; float u=Vector3.Dot(d,planeU),v=Vector3.Dot(d,planeV);
            if(Snap){u=Mathf.Round(u/moveSnap)*moveSnap;v=Mathf.Round(v/moveSnap)*moveSnap;}
            planeTarget=planeU*u+planeV*v; return;
        }
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
            Vector3 nextPosition = ghost.Root.position + Vector3.up * stepDistance;
            ghost.Root.position = nextPosition;

        }
        remainingLift = t >= 1f ? 0f : Mathf.Max(0f, remainingLift - distance);
    }

    private void FixedUpdate()
    {
        if (!selected || !ghost) return;
        if (drag < 0) return;
        if(drag>=6) { planeApplied=Vector3.MoveTowards(planeApplied,planeTarget,maxDragSpeed*Time.fixedDeltaTime); ghost.Root.position=startPosition+planeApplied; return; }
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
            ghost.Root.position = p; ghost.Root.rotation = q; applied = next;

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
                if (overlap && depth > .00001f) return false;
            }
        }
        return true;
    }
    private void Confirm()
    {
        if (!ghost) return;
        if (!groundLine) groundLine = FindAnyObjectByType<GhostGroundLine>();
        Physics.SyncTransforms();
        HasGround = groundLine && groundLine.TryGround(ghost.Root, selected, out _, out _);
        if (!HasGround || !PreviewValid) { rejectedUntil = Time.time + rejectDuration; return; }
        EndDrag();
        var t = new Transfer { body = selected, preview = ghost,
            start = new Pose(selected.position, selected.rotation), end = new Pose(ghost.Root.position, ghost.Root.rotation),
            gravity = savedGravity, ccd = savedCcd, interpolation = savedInterpolation, collisions = selected.detectCollisions };
        t.duration = Mathf.Clamp(Vector3.Distance(t.start.position,t.end.position)/Mathf.Max(.01f,transferSpeed), .1f,.5f);
        selected.detectCollisions = false;
        ghost.SetAppearance(validColor,0); transfers.Add(t);
        ghost = null; selected = null; shapes = null; remainingLift = 0; hover = -1; selectedObjectSize = 1;
        foreach (var m in outlines.Values) if (m) Destroy(m); outlines.Clear();
    }
    private void UpdateTransfers()
    {
        for (int i = transfers.Count-1; i >= 0; i--) {
            var t = transfers[i];
            if (!t.body) { if(t.preview) Destroy(t.preview.gameObject); transfers.RemoveAt(i); continue; }
            if (!t.arrived) {
                t.elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(t.elapsed/t.duration); u = u*u*(3-2*u);
                t.body.position = Vector3.Lerp(t.start.position,t.end.position,u);
                t.body.rotation = Quaternion.Slerp(t.start.rotation,t.end.rotation,u);
                if (t.elapsed < t.duration) continue;
                Physics.SyncTransforms();
                // Recheck destination, including other transfers which have already arrived.
                if (!TransferDestinationClear(t)) { t.body.position=t.start.position; t.body.rotation=t.start.rotation; FinishTransfer(t); transfers.RemoveAt(i); continue; }
                t.arrived = true; t.body.detectCollisions = t.collisions;
                if(t.preview) Destroy(t.preview.gameObject); t.preview=null;
                continue;
            }
            t.hold += Time.deltaTime;
            if(t.hold >= releaseDelay) { FinishTransfer(t); transfers.RemoveAt(i); }
        }
    }
    private bool TransferDestinationClear(Transfer t) {
        foreach(var c in t.body.GetComponentsInChildren<Collider>()) {
            if(!c || !c.enabled || c.isTrigger || c.attachedRigidbody != t.body) continue;
            // Collider bounds can be empty while collision detection is disabled.
            t.body.detectCollisions = true; Physics.SyncTransforms();
            var b=c.bounds;
            foreach(var other in Physics.OverlapSphere(b.center,b.extents.magnitude+collisionClearance,obstacleLayers,QueryTriggerInteraction.Ignore)) {
                if(!other || other.attachedRigidbody==t.body || Physics.GetIgnoreLayerCollision(c.gameObject.layer,other.gameObject.layer) || Physics.GetIgnoreCollision(c,other)) continue;
                if(Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,other,other.transform.position,other.transform.rotation,out _,out float depth) && depth>.00001f) return false;
            }
        }
        return true;
    }
    private void FinishTransfer(Transfer t) {
        if(t.preview) Destroy(t.preview.gameObject);
        if(!t.body) return;
        if(!t.arrived) { t.body.position=t.start.position; t.body.rotation=t.start.rotation; }
        t.body.detectCollisions=t.collisions; t.body.isKinematic=false; t.body.collisionDetectionMode=t.ccd; t.body.interpolation=t.interpolation; t.body.useGravity=t.gravity;
        t.body.linearVelocity=Vector3.zero; t.body.angularVelocity=Vector3.zero; t.body.WakeUp();
    }
    public void RegisterSpawn(Rigidbody rb) { if (rb && !rb.isKinematic && !spawnPoses.ContainsKey(rb) && (selectableLayers.value & (1 << rb.gameObject.layer)) != 0) spawnPoses.Add(rb, new Pose(rb.position, rb.rotation)); }
    public bool Respawn(Rigidbody rb) { if (!rb || !spawnPoses.TryGetValue(rb, out var pose)) return false; if (rb == selected) ClearSelection(); for(int i=transfers.Count-1;i>=0;i--) if(transfers[i].body==rb) { FinishTransfer(transfers[i]); transfers.RemoveAt(i); } if (rb.isKinematic) return false; rb.position = pose.position; rb.rotation = pose.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; rb.WakeUp(); return true; }
    // Convert desired screen pixels to world units using the actual camera projection.
    private void RefreshGizmoMeshes()
    {
        if (planeBorderMesh && !Mathf.Approximately(drawnPlaneBorder,planeBorder)) { Destroy(planeBorderMesh); planeBorderMesh=BuildPlaneBorder(); drawnPlaneBorder=planeBorder; }
        float shaft = arrowShaftThickness, head = arrowHeadWidth, ring = ringThickness;
        if (adaptiveGizmoThickness && sizeMode != GizmoSizeMode.WorldSize && selected && cam && cam.pixelHeight > 0)
        {
            float depth = Mathf.Max(cam.nearClipPlane, Vector3.Dot((ghost ? ghost.Root.position : selected.position) - cam.transform.position, cam.transform.forward));
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
        if (!ringMesh || ThicknessChanged(ring, drawnRing) || !Mathf.Approximately(ringRadius, drawnRadius) || ringSegments != drawnSegments || !Mathf.Approximately(arcDegrees,drawnArc))
        {
            if (ringMesh) Destroy(ringMesh);
            ringMesh = BuildRing(ring, arcDegrees);
            if(fullRingMesh) Destroy(fullRingMesh); fullRingMesh=BuildRing(ring,360);
            drawnRing = ring; drawnRadius = ringRadius; drawnSegments = ringSegments; drawnArc=arcDegrees;
        }
    }
    private static bool ThicknessChanged(float a, float b) => Mathf.Abs(a - b) > Mathf.Max(.00001f, Mathf.Abs(b) * .025f);
    private Mesh BuildRing(float thickness, float degrees)
    {
        var v = new List<Vector3>(); var t = new List<int>(); int n = Mathf.Max(24, ringSegments), sides = 8;
        for (int i = 0; i <= n; i++) { float a = degrees * Mathf.Deg2Rad * i / n; Vector3 radial = new(Mathf.Cos(a), Mathf.Sin(a), 0); for (int j = 0; j < sides; j++) { float b = 2 * Mathf.PI * j / sides; v.Add(radial * (ringRadius + thickness * .5f * Mathf.Cos(b)) + Vector3.forward * (thickness * .5f * Mathf.Sin(b))); } }
        for (int i = 0; i < n; i++) for (int j = 0; j < sides; j++) { int a = i * sides + j, b = i * sides + (j + 1) % sides, c = (i + 1) * sides + j, d = (i + 1) * sides + (j + 1) % sides; t.AddRange(new[] { a, b, c, b, d, c }); }
        if(degrees<359.99f) foreach(float a in new[]{0f,degrees*Mathf.Deg2Rad}) {
            Vector3 center=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*ringRadius;
            int offset=v.Count; const int lat=6, lon=12;
            for(int j=0;j<=lat;j++) for(int k=0;k<=lon;k++) {
                float phi=Mathf.PI*j/lat, theta=2*Mathf.PI*k/lon;
                v.Add(center+thickness*.5f*new Vector3(Mathf.Sin(phi)*Mathf.Cos(theta),Mathf.Sin(phi)*Mathf.Sin(theta),Mathf.Cos(phi)));
            }
            for(int j=0;j<lat;j++) for(int k=0;k<lon;k++) {int x=offset+j*(lon+1)+k,y=x+lon+1;t.AddRange(new[]{x,y,x+1,x+1,y,y+1});}
        }
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
