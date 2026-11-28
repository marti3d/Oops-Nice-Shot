using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(101)]
public sealed class GizmoFloatingButtons : MonoBehaviour, ISerializationCallbackReceiver
{
    [MiniGolfFoldout("Scene References")]
    [Tooltip("Controller that supplies selection, placement validity and current tool state.")]
    [SerializeField] private ObjectGizmoController gizmoController;
    [Tooltip("Panel containing the floating placement controls. Keep this component on an active parent outside this panel.")]
    [SerializeField] private GameObject buttonPanel;

    [MiniGolfFoldout("Floating Rows")]
    [SerializeField] private RectTransform toolRow;
    [SerializeField] private RectTransform secondaryRow;
    [Tooltip("Extra screen pixels above the object's selection-time outline for the tool row.")]
    [Min(0f)][SerializeField] private float topRowPadding = 32f;
    [Tooltip("Extra screen pixels below the object's selection-time outline for Place and Cancel.")]
    [Min(0f)][SerializeField] private float bottomRowPadding = 32f;
    [UnityEngine.Serialization.FormerlySerializedAs("ghostPadding")]
    [HideInInspector][SerializeField] private float buttonPadding = -1f;
    [Tooltip("Minimum screen pixels from the gizmo pivot to the nearest edge of either button row.")]
    [Min(0f)][SerializeField] private float minimumGizmoClearance = 96f;
    [Tooltip("Minimum distance from the edge of the camera viewport in screen pixels.")]
    [Min(0f)][SerializeField] private float screenMargin = 12f;

    private Transform trackedPreview;
    private Bounds initialBoundsRelativeToPivot;
    private Canvas canvas;
    private readonly Vector3[] corners = new Vector3[4];

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (buttonPadding < 0f)
            return;

        topRowPadding = bottomRowPadding = buttonPadding;
        buttonPadding = -1f;
    }

    private void Awake() => canvas = GetComponentInParent<Canvas>();

    [MiniGolfFoldout("Visibility")]
    [Tooltip("Time in seconds for a complete fade. Uses unscaled time so fades work during Pause.")]
    [Min(0f)][SerializeField] private float fadeDuration = .2f;

    private CanvasGroup panelGroup;

    [MiniGolfFoldout("Button Feedback")]
    [Tooltip("The two tool buttons whose backgrounds show the current tool.")]
    [SerializeField] private Button moveButton, rotateButton;
    [Tooltip("Button that confirms placement or triggers rejection feedback for an invalid preview.")]
    [SerializeField] private Button placeButton;
    [Tooltip("Optional label inside the Local / World toggle.")]
    [SerializeField] private TMP_Text axisLabel;
    [Tooltip("Background color of the active tool button.")]
    [SerializeField] private Color activeToolColor = new(.98f, .83f, .37f);

    [Tooltip("Place button tint when the preview cannot be placed.")]
    [SerializeField] private Color invalidPlaceColor = new Color32(220, 32, 32, 255);

    private Button styledPlaceButton;
    private ColorBlock defaultPlaceColors;

    private void OnEnable() => HideImmediately();

    private void OnDisable() => HideImmediately();

    private void LateUpdate()
    {
        if (!gizmoController || !gizmoController.isActiveAndEnabled ||
            !gizmoController.SelectedObject || !gizmoController.PreviewTransform ||
            !buttonPanel || !placeButton || !toolRow || !secondaryRow || !canvas || !gizmoController.ViewCamera)
        {
            UpdatePlaceFeedback(false, false);
            SetVisible(false);
            return;
        }

        bool opening = !buttonPanel.activeInHierarchy || !panelGroup || panelGroup.alpha <= 0f;
        SetVisible(true);
        if (opening)
            Canvas.ForceUpdateCanvases();
        PositionRows();
        bool canPlace = gizmoController.HasGround && gizmoController.PreviewValid;
        UpdatePlaceFeedback(canPlace, true);
        if (axisLabel)
        {
            string axis = gizmoController.IsLocalSpace ? "Local Axis" : "World Axis";
            if (axisLabel.text != axis)
                axisLabel.text = axis;
        }

        HighlightTool(moveButton, !gizmoController.IsRotationTool);
        HighlightTool(rotateButton, gizmoController.IsRotationTool);

    }

    private void PositionRows()
    {
        Camera view = gizmoController.ViewCamera;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Transform preview = gizmoController.PreviewTransform;
        if (trackedPreview != preview)
        {
            trackedPreview = preview;
            Bounds initial = gizmoController.PreviewBounds;
            initialBoundsRelativeToPivot = new Bounds(initial.center - preview.position, initial.size);
        }

        // Follow translation, keeping the selection-time world axes and pivot offset.
        Bounds bounds = initialBoundsRelativeToPivot;
        bounds.center += preview.position;
        Rect viewport = view.pixelRect;
        Vector3 projected = view.WorldToScreenPoint(bounds.center);
        Vector2 center = projected;
        float topGap = 0f;
        float bottomGap = 0f;

        if (projected.z > view.nearClipPlane)
        {
            float halfExtent = 0f;
            // Project all eight bounds corners of the selection-time box into the current camera view.
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = new((i & 1) == 0 ? bounds.min.x : bounds.max.x,
                                (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                                (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                Vector3 pixel = view.WorldToScreenPoint(p);
                if (pixel.z > view.nearClipPlane)
                    halfExtent = Mathf.Max(halfExtent, Mathf.Abs(pixel.y - center.y));
            }
            topGap = halfExtent + topRowPadding;
            bottomGap = halfExtent + bottomRowPadding;
        }
        else
        {
            Vector3 local = view.transform.InverseTransformPoint(bounds.center);
            Vector2 direction = new(local.x, local.y);
            if (direction.sqrMagnitude < .0001f)
                direction = Vector2.down;
            center = viewport.center + direction.normalized * viewport.size.magnitude;
        }

        Rect top = RowOffsets(toolRow, uiCamera);
        Rect bottom = RowOffsets(secondaryRow, uiCamera);
        float halfWidth = Mathf.Max(top.width, bottom.width) * .5f;
        float maxGap = Mathf.Max(0f, viewport.height - 2f * screenMargin - top.height - bottom.height);
        float requestedGap = topGap + bottomGap;
        if (requestedGap > maxGap)
        {
            float fit = maxGap / requestedGap;
            topGap *= fit;
            bottomGap *= fit;
        }
        float x = ClampToViewport(center.x, viewport.xMin + screenMargin + halfWidth,
                                            viewport.xMax - screenMargin - halfWidth);
        float y = ClampToViewport(center.y, viewport.yMin + screenMargin + bottom.height + bottomGap,
                                            viewport.yMax - screenMargin - top.height - topGap);
        float topEdge = y + topGap;
        float bottomEdge = y - bottomGap;
        Vector3 pivotPixel = view.WorldToScreenPoint(preview.position);
        if (pivotPixel.z > view.nearClipPlane)
        {
            float upperLimit = viewport.yMax - screenMargin - top.height;
            float lowerLimit = viewport.yMin + screenMargin + bottom.height;
            float safeTop = pivotPixel.y + minimumGizmoClearance;
            float safeBottom = pivotPixel.y - minimumGizmoClearance;
            topEdge = Mathf.Min(upperLimit, Mathf.Max(topEdge, safeTop));
            bottomEdge = Mathf.Max(lowerLimit, Mathf.Min(bottomEdge, safeBottom));

            // At a screen edge, stack on the side with room rather than covering a handle.
            const float rowSpacing = 4f;
            if (safeTop > upperLimit && bottomEdge - bottom.height - rowSpacing - top.height >= viewport.yMin + screenMargin)
            {
                bottomEdge -= top.height + rowSpacing;
                topEdge = bottomEdge + rowSpacing;
            }
            else if (safeBottom < lowerLimit && topEdge + top.height + rowSpacing + bottom.height <= viewport.yMax - screenMargin)
            {
                bottomEdge = topEdge + bottom.height;
                topEdge = bottomEdge + rowSpacing;
            }
        }
        PlaceRow(toolRow, uiCamera, new Vector2(x - top.center.x, topEdge - top.yMin));
        PlaceRow(secondaryRow, uiCamera, new Vector2(x - bottom.center.x, bottomEdge - bottom.yMax));
    }

    private Rect RowOffsets(RectTransform row, Camera camera)
    {
        Vector2 pivot = RectTransformUtility.WorldToScreenPoint(camera, row.position);
        row.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]) - pivot;
        Vector2 max = min;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]) - pivot;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void PlaceRow(RectTransform row, Camera camera, Vector2 pivot)
    {
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)row.parent, pivot,
                                                                    camera, out Vector3 position))
            row.position = position;
    }

    private static float ClampToViewport(float value, float min, float max)
    {
        return min <= max ? Mathf.Clamp(value, min, max) : (min + max) * .5f;
    }

    private void UpdatePlaceFeedback(bool canPlace, bool previewActive)
    {
        if (!placeButton)
            return;

        if (styledPlaceButton != placeButton)
        {
            styledPlaceButton = placeButton;
            defaultPlaceColors = placeButton.colors;
        }

        ColorBlock colors = defaultPlaceColors;
        // Invalid previews stay clickable so Confirm can play its existing rejection shake.
        if (previewActive && !canPlace)
        {
            colors.normalColor = invalidPlaceColor;
            colors.highlightedColor = invalidPlaceColor;
            colors.pressedColor = invalidPlaceColor;
            colors.selectedColor = invalidPlaceColor;
        }
        // Panel fade-out also disables interaction. Keep that state neutral.
        colors.disabledColor = defaultPlaceColors.normalColor;
        if (!placeButton.colors.Equals(colors))
            placeButton.colors = colors;
        if (placeButton.interactable != previewActive)
            placeButton.interactable = previewActive;
    }

    private void EnsurePanelGroup()
    {
        if (!buttonPanel)
            return;
        if (!panelGroup || panelGroup.gameObject != buttonPanel)
        {
            panelGroup = buttonPanel.GetComponent<CanvasGroup>();
            if (!panelGroup)
                panelGroup = buttonPanel.AddComponent<CanvasGroup>();
        }
    }

    private void HideImmediately()
    {
        UpdatePlaceFeedback(false, false);
        EnsurePanelGroup();
        if (!panelGroup)
            return;
        panelGroup.alpha = 0f;
        panelGroup.interactable = false;
        panelGroup.blocksRaycasts = false;
    }

    private void SetVisible(bool visible)
    {
        EnsurePanelGroup();
        if (!panelGroup)
            return;

        if (visible && !buttonPanel.activeSelf)
            buttonPanel.SetActive(true);

        float targetAlpha = visible ? 1f : 0f;
        panelGroup.alpha = fadeDuration <= 0f ? targetAlpha
            : Mathf.MoveTowards(panelGroup.alpha, targetAlpha,
                                Time.unscaledDeltaTime / fadeDuration);
        panelGroup.interactable = visible && panelGroup.alpha > 0f;
        panelGroup.blocksRaycasts = visible && panelGroup.alpha > 0f;
    }

    private void HighlightTool(Button button, bool active)
    {
        if (button && button.targetGraphic)
            button.targetGraphic.color = active ? activeToolColor : button.colors.normalColor;
    }
}
