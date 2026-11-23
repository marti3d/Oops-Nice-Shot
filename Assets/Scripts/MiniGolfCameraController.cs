using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Perspective/orthographic camera for a miniature golf island.
/// Middle-drag: pan on a horizontal ground plane.
/// Right-drag horizontally/vertically: yaw and pitch around the current ground focus.
/// Left mouse button: unused by this controller.
/// Scroll: zoom by changing perspective FOV or orthographic size, without moving closer.
/// Attach to the Main Camera. Requires no camera rig or Cinemachine.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class MiniGolfCameraController : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Optional. The camera starts focused on this object's world position. Its Y also sets the horizontal navigation plane.")]
    [SerializeField] private Transform islandCenter;

    [Tooltip("Optional. Limits the focus point to the box's world-space X/Z bounds. Rotation of the box is not supported.")]
    [SerializeField] private BoxCollider movementBounds;

    public enum ProjectionMode { Perspective, Orthographic }

    [Header("Projection Mode")]
    [Tooltip("Select Perspective or Orthographic. Can also be changed during Play Mode.")]
    [SerializeField] private ProjectionMode projectionMode = ProjectionMode.Perspective;

    [Header("Camera Framing")]
    [Min(0.1f)]
    [Tooltip("Physical distance from the camera to its focus point. This does not change while zooming.")]
    [SerializeField] private float cameraDistance = 250f;

    [SerializeField] private float initialYaw = 0f;
    [Range(1f, 89f)]
    [SerializeField] private float initialPitch = 45f;

    [Header("Panning (Middle Mouse)")]
    [Min(0.01f)]
    [Tooltip("Multiplier on drag movement. 1 = point under cursor tracks drag exactly when smoothing is zero.")]
    [SerializeField] private float panSensitivity = 1f;

    [Min(0f)]
    [Tooltip("Smoothing while dragging, in seconds. Set to 0 for exact mouse tracking.")]
    [SerializeField] private float panSmoothTime = 0.035f;

    [Header("Orbiting (Right Mouse = Yaw + Pitch)")]
    [Min(0.01f)]
    [Tooltip("Degrees of yaw/pitch rotation per pixel dragged.")]
    [SerializeField] private float rotationSensitivity = 0.2f;

    [Min(0f)]
    [Tooltip("Smoothing while dragging, in seconds. No continued movement after release.")]
    [SerializeField] private float rotationSmoothTime = 0.045f;

    [Range(1f, 89f)]
    [SerializeField] private float minPitch = 25f;
    [Range(1f, 89f)]
    [SerializeField] private float maxPitch = 65f;

    [Header("Perspective Zoom (Mouse Wheel)")]
    [Range(0.1f, 179f)]
    [SerializeField] private float initialFieldOfView = 8f;

    [Range(0.1f, 179f)]
    [SerializeField] private float minFieldOfView = 3f;

    [Range(0.1f, 179f)]
    [SerializeField] private float maxFieldOfView = 18f;

    [Header("Orthographic Zoom (Mouse Wheel)")]
    [Min(0.01f)][SerializeField] private float initialOrthographicSize = 17.5f;
    [Min(0.01f)][SerializeField] private float minOrthographicSize = 6.5f;
    [Min(0.01f)][SerializeField] private float maxOrthographicSize = 40f;

    [Header("Zoom Feel (Both Modes)")]
    [Min(0.001f)]
    [Tooltip("Proportional zoom per wheel notch, in either projection mode.")]
    [SerializeField] private float zoomSensitivity = 0.12f;

    [Min(0f)]
    [SerializeField] private float zoomSmoothTime = 0.10f;

    private Camera controlledCamera;
    private Vector3 focus;
    private Vector3 targetFocus;
    private Vector3 panVelocity;
    private float yaw;
    private float targetYaw;
    private float yawVelocity;
    private float pitch;
    private float targetPitch;
    private float pitchVelocity;
    // Vertical half-height of the view at the focus distance.
    // In ortho it equals orthographicSize; in perspective it is distance * tan(FOV / 2).
    private float viewHalfHeight;
    private float targetViewHalfHeight;
    private ProjectionMode appliedProjection;
    private float zoomVelocity;
    private Vector2 previousMousePosition;
    private bool wasPanning;
    private bool wasOrbiting;

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        appliedProjection = projectionMode;
        controlledCamera.orthographic = projectionMode == ProjectionMode.Orthographic;

        Vector3 initialFocus = islandCenter != null ? islandCenter.position : Vector3.zero;
        focus = targetFocus = ClampFocus(initialFocus);

        yaw = targetYaw = initialYaw;
        pitch = targetPitch = Mathf.Clamp(initialPitch, minPitch, maxPitch);
        viewHalfHeight = targetViewHalfHeight = projectionMode == ProjectionMode.Orthographic
            ? Mathf.Clamp(initialOrthographicSize, minOrthographicSize, maxOrthographicSize)
            : HalfHeightFromFov(Mathf.Clamp(initialFieldOfView, minFieldOfView, maxFieldOfView));

        ApplyCameraTransform();
    }

    private void Update()
    {
        if (!TryReadMouse(out Vector2 mousePosition, out bool middleHeld,
                          out bool rightHeld, out float wheelNotches))
        {
            wasPanning = false;
            wasOrbiting = false;
            return;
        }

        bool panning = middleHeld && !rightHeld;
        bool orbiting = rightHeld && !middleHeld;
        Vector2 mouseDelta = mousePosition - previousMousePosition;

        // Do not use the mouse displacement on the first frame of a drag.
        if (panning && wasPanning)
        {
            // Use the *current rendered camera* to intersect the same ground plane
            // at the previous and current pointer positions.
            Plane ground = new Plane(Vector3.up, new Vector3(0f, focus.y, 0f));
            Ray oldRay = controlledCamera.ScreenPointToRay(previousMousePosition);
            Ray newRay = controlledCamera.ScreenPointToRay(mousePosition);
            if (ground.Raycast(oldRay, out float oldT) && ground.Raycast(newRay, out float newT))
            {
                Vector3 delta = (oldRay.GetPoint(oldT) - newRay.GetPoint(newT)) * panSensitivity;
                delta.y = 0f;
                targetFocus = ClampFocus(targetFocus + delta);
            }
        }

        if (orbiting && wasOrbiting)
        {
            targetYaw += mouseDelta.x * rotationSensitivity;
            targetPitch = Mathf.Clamp(targetPitch - mouseDelta.y * rotationSensitivity,
                                      minPitch, maxPitch);
        }

        if (!panning && wasPanning)
        {
            // Kill any remaining pan inertia as soon as the button is released.
            targetFocus = focus;
            panVelocity = Vector3.zero;
        }
        if (!orbiting && wasOrbiting)
        {
            targetYaw = yaw;
            targetPitch = pitch;
            yawVelocity = 0f;
            pitchVelocity = 0f;
        }

        if (!Mathf.Approximately(wheelNotches, 0f))
        {
            targetViewHalfHeight = ClampHalfHeight(
                targetViewHalfHeight * Mathf.Exp(-wheelNotches * zoomSensitivity));
        }

        previousMousePosition = mousePosition;
        wasPanning = panning;
        wasOrbiting = orbiting;
    }

    private void LateUpdate()
    {
        // Changing the Inspector dropdown during Play Mode preserves the apparent framing
        // as closely as the new projection's zoom limits allow.
        if (appliedProjection != projectionMode)
        {
            appliedProjection = projectionMode;
            controlledCamera.orthographic = projectionMode == ProjectionMode.Orthographic;
            targetViewHalfHeight = ClampHalfHeight(targetViewHalfHeight);
            viewHalfHeight = ClampHalfHeight(viewHalfHeight);
            zoomVelocity = 0f;
        }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        if (wasPanning)
        {
            focus = panSmoothTime <= 0f
                ? targetFocus
                : Vector3.SmoothDamp(focus, targetFocus, ref panVelocity, panSmoothTime,
                                     Mathf.Infinity, dt);
        }
        else
        {
            focus = targetFocus;
            panVelocity = Vector3.zero;
        }
        focus = ClampFocus(focus);

        if (wasOrbiting && rotationSmoothTime > 0f)
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity,
                                        rotationSmoothTime, Mathf.Infinity, dt);
        else
            yaw = targetYaw;

        if (wasOrbiting && rotationSmoothTime > 0f)
            pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity,
                                     rotationSmoothTime, Mathf.Infinity, dt);
        else
            pitch = targetPitch;

        viewHalfHeight = zoomSmoothTime <= 0f
            ? targetViewHalfHeight
            : Mathf.SmoothDamp(viewHalfHeight, targetViewHalfHeight, ref zoomVelocity,
                               zoomSmoothTime, Mathf.Infinity, dt);
        ApplyCameraTransform();
    }

    private void ApplyCameraTransform()
    {
        Quaternion orientation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(
            focus + orientation * (Vector3.back * cameraDistance), orientation);
        controlledCamera.orthographic = projectionMode == ProjectionMode.Orthographic;
        if (controlledCamera.orthographic)
            controlledCamera.orthographicSize = viewHalfHeight;
        else
            controlledCamera.fieldOfView = 2f * Mathf.Atan(viewHalfHeight / cameraDistance) * Mathf.Rad2Deg;
    }

    private float HalfHeightFromFov(float degrees)
    {
        return cameraDistance * Mathf.Tan(0.5f * degrees * Mathf.Deg2Rad);
    }

    private float ClampHalfHeight(float height)
    {
        if (projectionMode == ProjectionMode.Orthographic)
            return Mathf.Clamp(height, minOrthographicSize, maxOrthographicSize);
        return Mathf.Clamp(height, HalfHeightFromFov(minFieldOfView),
                            HalfHeightFromFov(maxFieldOfView));
    }

    private Vector3 ClampFocus(Vector3 point)
    {
        if (movementBounds == null) return point;
        Bounds bounds = movementBounds.bounds;
        point.x = Mathf.Clamp(point.x, bounds.min.x, bounds.max.x);
        point.z = Mathf.Clamp(point.z, bounds.min.z, bounds.max.z);
        return point;
    }

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
        initialOrthographicSize = Mathf.Clamp(initialOrthographicSize,
                                               minOrthographicSize, maxOrthographicSize);
    }

    private static bool TryReadMouse(out Vector2 position, out bool middle,
                                     out bool right, out float wheelNotches)
    {
#if ENABLE_INPUT_SYSTEM
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            position = default;
            middle = right = false;
            wheelNotches = 0f;
            return false;
        }
        position = mouse.position.ReadValue();
        middle = mouse.middleButton.isPressed;
        right = mouse.rightButton.isPressed;
        wheelNotches = mouse.scroll.ReadValue().y / 120f;
        return true;
#elif ENABLE_LEGACY_INPUT_MANAGER
        position = Input.mousePosition;
        middle = Input.GetMouseButton(2);
        right = Input.GetMouseButton(1);
        wheelNotches = Input.mouseScrollDelta.y;
        return true;
#else
        position = default;
        middle = right = false;
        wheelNotches = 0f;
        return false;
#endif
    }
}
