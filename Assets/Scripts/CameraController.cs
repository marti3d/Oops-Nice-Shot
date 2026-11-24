using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Perspective/orthographic camera for a miniature golf island (New Input System only).
/// Middle-drag : pan on a horizontal ground plane.
/// Right-drag  : yaw + pitch around the current ground focus.
/// Scroll      : zoom toward the cursor (FOV / ortho size only, the camera never moves closer).
/// Bounds      : any convex Collider (Box, Sphere, Capsule, convex Mesh) limits the focus point in XZ.
///               Dragging past the edge feels like a rubber band and snaps softly back on release.
/// Attach to the Main Camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class CameraController : MonoBehaviour
{
    public enum ProjectionMode { Perspective, Orthographic }

    [Header("Scene References")]
    [Tooltip("Optional. Camera starts focused here. Its Y also defines the horizontal navigation plane.")]
    [SerializeField] private Transform islandCenter;

    [Tooltip("Optional. Any convex collider (Box, Sphere, Capsule, convex Mesh). Mark it 'Is Trigger'. " +
             "Its vertical range should include the navigation plane height.")]
    [SerializeField] private Collider movementBounds;

    [Header("Projection Mode")]
    [SerializeField] private ProjectionMode projectionMode = ProjectionMode.Perspective;

    [Header("Camera Framing")]
    [Min(0.1f)]
    [Tooltip("Physical distance from the camera to its focus point. Does not change while zooming.")]
    [SerializeField] private float cameraDistance = 250f;
    [SerializeField] private float initialYaw = 0f;
    [Range(1f, 89f)][SerializeField] private float initialPitch = 45f;

    [Header("Panning (Middle Mouse)")]
    [Min(0.01f)][SerializeField] private float panSensitivity = 1f;
    [Min(0f)]
    [Tooltip("Seconds. 0 = exact tracking.")]
    [SerializeField] private float panSmoothTime = 0.06f;

    [Header("Orbiting (Right Mouse)")]
    [Min(0.01f)]
    [Tooltip("Degrees per pixel.")]
    [SerializeField] private float rotationSensitivity = 0.2f;
    [Min(0f)][SerializeField] private float rotationSmoothTime = 0.08f;
    [Range(1f, 89f)][SerializeField] private float minPitch = 25f;
    [Range(1f, 89f)][SerializeField] private float maxPitch = 65f;

    [Header("Soft Bounds (Rubber Band)")]
    [Range(0f, 0.5f)]
    [Tooltip("Maximum overshoot past the bounds as a fraction of the visible view height.")]
    [SerializeField] private float maxOvershoot = 0.1f;
    [Min(0f)]
    [Tooltip("Seconds it takes to ease back inside after releasing the mouse.")]
    [SerializeField] private float boundsReturnSmoothTime = 0.18f;

    [Header("Perspective Zoom")]
    [Range(0.1f, 179f)][SerializeField] private float initialFieldOfView = 8f;
    [Range(0.1f, 179f)][SerializeField] private float minFieldOfView = 3f;
    [Range(0.1f, 179f)][SerializeField] private float maxFieldOfView = 18f;

    [Header("Orthographic Zoom")]
    [Min(0.01f)][SerializeField] private float initialOrthographicSize = 17.5f;
    [Min(0.01f)][SerializeField] private float minOrthographicSize = 6.5f;
    [Min(0.01f)][SerializeField] private float maxOrthographicSize = 40f;

    [Header("Zoom Feel")]
    [Min(0.001f)][SerializeField] private float zoomSensitivity = 0.12f;
    [Min(0f)][SerializeField] private float zoomSmoothTime = 0.12f;
    [Min(1f)]
    [Tooltip("Raw scroll units that count as one notch (120 on Windows mice).")]
    [SerializeField] private float scrollUnitsPerNotch = 120f;

    /// <summary>Other scripts (e.g. the object gizmo) can disable camera input while they use the mouse.</summary>
    public bool InputEnabled { get; set; } = true;

    private Camera controlledCamera;
    private float groundY;

    // "raw" = where the mouse wants the focus (may be outside bounds, limited overshoot).
    // "focus" = what is rendered (smoothed, rubber-banded).
    private Vector3 rawFocus, focus, focusVelocity;
    private float yaw, targetYaw, yawVelocity;
    private float pitch, targetPitch, pitchVelocity;
    private float viewHalfHeight, targetViewHalfHeight, zoomVelocity;

    private ProjectionMode appliedProjection;
    private Vector2 previousMouse;
    private bool panActive, orbitActive;
    private bool warnedBounds;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        appliedProjection = projectionMode;
        controlledCamera.orthographic = projectionMode == ProjectionMode.Orthographic;
        controlledCamera.farClipPlane = Mathf.Max(controlledCamera.farClipPlane, cameraDistance * 2f);
        controlledCamera.nearClipPlane = Mathf.Max(0.1f, Mathf.Min(controlledCamera.nearClipPlane, cameraDistance * 0.5f));

        Vector3 start = islandCenter != null ? islandCenter.position : Vector3.zero;
        groundY = start.y;
        start.y = groundY;
        rawFocus = focus = ClampToBounds(start);

        yaw = targetYaw = initialYaw;
        pitch = targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        viewHalfHeight = targetViewHalfHeight = ClampHalfHeight(
            projectionMode == ProjectionMode.Orthographic
                ? initialOrthographicSize
                : HalfHeightFromFov(initialFieldOfView));

        ApplyCameraTransform();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) { panActive = orbitActive = false; return; }

        Vector2 mousePos = mouse.position.ReadValue();

        if (!InputEnabled)
        {
            panActive = orbitActive = false;
            previousMouse = mousePos;
            return;
        }

        // A gesture only starts if the press began outside UI, and ends when the button is released.
        if (mouse.middleButton.wasPressedThisFrame) panActive = !IsPointerOverUI();
        if (!mouse.middleButton.isPressed) panActive = false;
        if (mouse.rightButton.wasPressedThisFrame) orbitActive = !IsPointerOverUI();
        if (!mouse.rightButton.isPressed) orbitActive = false;

        // Orbit (yaw + pitch around the focus).
        if (orbitActive && !mouse.rightButton.wasPressedThisFrame)
        {
            Vector2 delta = mousePos - previousMouse;
            targetYaw += delta.x * rotationSensitivity;
            targetPitch = Mathf.Clamp(targetPitch - delta.y * rotationSensitivity, minPitch, maxPitch);
        }

        // Pan. Rays come from the *target* pose (not the lagging rendered camera),
        // so the point under the cursor stays glued to it regardless of smoothing.
        if (panActive && !mouse.middleButton.wasPressedThisFrame)
        {
            if (TryGroundPoint(previousMouse, rawFocus, targetYaw, targetPitch, targetViewHalfHeight, out Vector3 a) &&
                TryGroundPoint(mousePos, rawFocus, targetYaw, targetPitch, targetViewHalfHeight, out Vector3 b))
            {
                Vector3 d = (a - b) * panSensitivity;
                d.y = 0f;
                rawFocus = LimitOvershoot(rawFocus + d);
            }
        }

        // Zoom toward the cursor: keep the ground point under the pointer fixed.
        float notches = Mathf.Clamp(mouse.scroll.ReadValue().y / scrollUnitsPerNotch, -3f, 3f);
        if (!Mathf.Approximately(notches, 0f) &&
            controlledCamera.pixelRect.Contains(mousePos) && !IsPointerOverUI())
        {
            bool hadBefore = TryGroundPoint(mousePos, rawFocus, targetYaw, targetPitch,
                                            targetViewHalfHeight, out Vector3 before);
            targetViewHalfHeight = ClampHalfHeight(targetViewHalfHeight * Mathf.Exp(-notches * zoomSensitivity));
            if (hadBefore && TryGroundPoint(mousePos, rawFocus, targetYaw, targetPitch,
                                            targetViewHalfHeight, out Vector3 after))
            {
                Vector3 d = before - after;
                d.y = 0f;
                rawFocus += d;
            }
        }

        previousMouse = mousePos;
    }

    private void LateUpdate()
    {
        if (appliedProjection != projectionMode)
        {
            // Half-height is projection-agnostic, so framing is preserved (within the new limits).
            appliedProjection = projectionMode;
            controlledCamera.orthographic = projectionMode == ProjectionMode.Orthographic;
            targetViewHalfHeight = ClampHalfHeight(targetViewHalfHeight);
            viewHalfHeight = ClampHalfHeight(viewHalfHeight);
            zoomVelocity = 0f;
        }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        // Soft bounds: when not dragging, raw focus returns inside; the rendered focus eases there.
        Vector3 inside = ClampToBounds(rawFocus);
        if (!panActive) rawFocus = inside;
        Vector3 offset = rawFocus - inside;
        offset.y = 0f;
        Vector3 desired = inside + RubberBand(offset);
        desired.y = groundY;

        bool outside = (focus - inside).sqrMagnitude > 0.0001f;
        float focusSmooth = panActive ? panSmoothTime
                          : outside ? boundsReturnSmoothTime
                          : zoomSmoothTime; // matches zoom so zoom-to-cursor stays glued
        focus = focusSmooth <= 0f
            ? desired
            : Vector3.SmoothDamp(focus, desired, ref focusVelocity, focusSmooth, Mathf.Infinity, dt);

        yaw = rotationSmoothTime <= 0f ? targetYaw
            : Mathf.SmoothDamp(yaw, targetYaw, ref yawVelocity, rotationSmoothTime, Mathf.Infinity, dt);
        pitch = rotationSmoothTime <= 0f ? targetPitch
            : Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, rotationSmoothTime, Mathf.Infinity, dt);
        viewHalfHeight = zoomSmoothTime <= 0f ? targetViewHalfHeight
            : Mathf.SmoothDamp(viewHalfHeight, targetViewHalfHeight, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);

        ApplyCameraTransform();
    }

    private void ApplyCameraTransform()
    {
        Quaternion orientation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(focus + orientation * (Vector3.back * cameraDistance), orientation);

        bool ortho = projectionMode == ProjectionMode.Orthographic;
        controlledCamera.orthographic = ortho;
        if (ortho) controlledCamera.orthographicSize = viewHalfHeight;
        else controlledCamera.fieldOfView = 2f * Mathf.Atan(viewHalfHeight / cameraDistance) * Mathf.Rad2Deg;
    }

    /// <summary>Ground-plane hit for a screen point as seen from a hypothetical camera pose.</summary>
    private bool TryGroundPoint(Vector2 screen, Vector3 focusPos, float yawDeg, float pitchDeg,
                                float halfHeight, out Vector3 point)
    {
        point = default;
        Rect rect = controlledCamera.pixelRect;
        if (rect.width <= 0f || rect.height <= 0f) return false;

        float nx = (screen.x - rect.x) / rect.width * 2f - 1f;
        float ny = (screen.y - rect.y) / rect.height * 2f - 1f;
        float aspect = rect.width / rect.height;

        Quaternion rot = Quaternion.Euler(pitchDeg, yawDeg, 0f);
        Vector3 camPos = focusPos + rot * (Vector3.back * cameraDistance);
        Vector3 origin, dir;
        if (projectionMode == ProjectionMode.Orthographic)
        {
            origin = camPos + rot * new Vector3(nx * halfHeight * aspect, ny * halfHeight, 0f);
            dir = rot * Vector3.forward;
        }
        else
        {
            origin = camPos;
            dir = rot * new Vector3(nx * aspect * halfHeight / cameraDistance,
                                    ny * halfHeight / cameraDistance, 1f);
            dir.Normalize();
        }

        if (dir.y > -1e-4f) return false;
        float t = (groundY - origin.y) / dir.y;
        if (t < 0f) return false;
        point = origin + dir * t;
        return true;
    }

    // ---------- Bounds ----------

    private bool BoundsUsable()
    {
        if (movementBounds == null || !movementBounds.enabled || !movementBounds.gameObject.activeInHierarchy)
            return false;
        if ((movementBounds is MeshCollider mc && !mc.convex) || movementBounds is TerrainCollider)
        {
            if (!warnedBounds)
            {
                warnedBounds = true;
                Debug.LogWarning("MiniGolfCameraController: bounds collider must be Box, Sphere, Capsule " +
                                 "or a CONVEX Mesh collider. Bounds are ignored.", this);
            }
            return false;
        }
        return true;
    }

    private Vector3 ClampToBounds(Vector3 p)
    {
        if (!BoundsUsable()) return p;
        Bounds b = movementBounds.bounds;
        // Sample the footprint at the plane height (or the collider's middle if the plane is outside it).
        float y = (groundY >= b.min.y && groundY <= b.max.y) ? groundY : b.center.y;
        Vector3 closest = movementBounds.ClosestPoint(new Vector3(p.x, y, p.z));
        return new Vector3(closest.x, groundY, closest.z);
    }

    private float MaxOvershootDistance() => maxOvershoot * 2f * targetViewHalfHeight;

    /// <summary>Saturating curve: 1:1 at first, increasingly resistant, never exceeds the maximum.</summary>
    private Vector3 RubberBand(Vector3 offset)
    {
        float max = MaxOvershootDistance();
        float d = offset.magnitude;
        if (max <= 0f || d < 1e-5f) return Vector3.zero;
        return offset * (max * (1f - Mathf.Exp(-d / max)) / d);
    }

    private Vector3 LimitOvershoot(Vector3 raw)
    {
        Vector3 inside = ClampToBounds(raw);
        Vector3 off = raw - inside;
        off.y = 0f;
        float limit = MaxOvershootDistance() * 3f; // curve is ~95% saturated by then
        float d = off.magnitude;
        if (limit <= 0f) return inside;
        if (d > limit) raw = inside + off * (limit / d);
        return raw;
    }

    // ---------- Zoom helpers ----------

    private float HalfHeightFromFov(float degrees) =>
        cameraDistance * Mathf.Tan(0.5f * degrees * Mathf.Deg2Rad);

    private float ClampHalfHeight(float h) =>
        projectionMode == ProjectionMode.Orthographic
            ? Mathf.Clamp(h, minOrthographicSize, maxOrthographicSize)
            : Mathf.Clamp(h, HalfHeightFromFov(minFieldOfView), HalfHeightFromFov(maxFieldOfView));

    private static bool IsPointerOverUI() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    private void OnValidate()
    {
        cameraDistance = Mathf.Max(0.1f, cameraDistance);
        minPitch = Mathf.Clamp(minPitch, 1f, 89f);
        maxPitch = Mathf.Clamp(maxPitch, minPitch, 89f);
        initialPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        minFieldOfView = Mathf.Clamp(minFieldOfView, 0.1f, 179f);
        maxFieldOfView = Mathf.Clamp(maxFieldOfView, minFieldOfView, 179f);
        initialFieldOfView = Mathf.Clamp(initialFieldOfView, minFieldOfView, maxFieldOfView);
        minOrthographicSize = Mathf.Max(0.01f, minOrthographicSize);
        maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
        initialOrthographicSize = Mathf.Clamp(initialOrthographicSize, minOrthographicSize, maxOrthographicSize);

        if (movementBounds is MeshCollider mc && !mc.convex)
            Debug.LogWarning("Movement bounds: MeshCollider must be Convex.", this);
    }
}
