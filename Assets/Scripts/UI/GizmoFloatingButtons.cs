using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(101)]
public sealed class GizmoFloatingButtons : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Controller that supplies the ghost bounds and current tool state.")]
    [SerializeField] private ObjectGizmoController gizmoController;
    [Tooltip("Gameplay camera used to project the ghost. Uses Main Camera when empty.")]
    [SerializeField] private Camera gameplayCamera;
    [Tooltip("Screen-space Canvas containing the floating controls.")]
    [SerializeField] private Canvas canvas;

    [Header("Layout")]
    [Tooltip("Full-screen UI container with no raycast-blocking background.")]
    [SerializeField] private GameObject buttonPanel;
    [Tooltip("RectTransform containing Move, Rotate and the axis toggle.")]
    [SerializeField] private RectTransform toolRow;
    [Tooltip("RectTransform containing the Place and Cancel buttons.")]
    [SerializeField] private RectTransform actionRow;
    [Tooltip("Screen pixels of clearance between the ghost bounds and each row.")]
    [Min(0f)] [SerializeField] private float ghostPadding = 32f;
    [Tooltip("Minimum screen pixels between a row and the viewport edge.")]
    [Min(0f)] [SerializeField] private float screenMargin = 12f;

    [Header("Button Feedback")]
    [Tooltip("The two tool buttons whose backgrounds show the current tool.")]
    [SerializeField] private Button moveButton, rotateButton;
    [Tooltip("Button enabled only while the current preview can be placed.")]
    [SerializeField] private Button placeButton;
    [Tooltip("Optional label inside the Local / World toggle.")]
    [SerializeField] private TMP_Text axisLabel;
    [Tooltip("Optional TextMeshPro label for placement messages. A label is created automatically when this field is empty.")]
    [SerializeField] private TMP_Text placementStatus;
    [Tooltip("Background color of the active tool button.")]
    [SerializeField] private Color activeToolColor = new(.98f, .83f, .37f);

    [Header("Placement Status Style")]
    [SerializeField] private Color statusTextColor = new Color32(220, 32, 32, 255);
    [SerializeField] private Color statusOutlineColor = Color.white;
    [Tooltip("Outline thickness in Canvas units.")]
    [Min(0f)] [SerializeField] private float statusOutlineWidth = 1.5f;
    [Tooltip("Gap below the Place and Cancel row in Canvas units.")]
    [Min(0f)] [SerializeField] private float statusSpacing = 6f;
    [Min(1f)] [SerializeField] private float statusFontSize = 24f;

    private static readonly Vector2[] OutlineDirections =
    {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new(-.7071f, .7071f), new(.7071f, .7071f),
        new(-.7071f, -.7071f), new(.7071f, -.7071f)
    };

    private readonly TextMeshProUGUI[] statusOutline = new TextMeshProUGUI[OutlineDirections.Length];
    private RectTransform statusRoot;
    private Transform trackedGhost;
    private Vector3 anchorOffset;
    private float restHeight;
    private Vector2 lastDirection = Vector2.down;
    private HorizontalLayoutGroup rowLayout;

    private void OnEnable()
    {
        SetVisible(false);
        if (statusRoot)
            statusRoot.gameObject.SetActive(false);
        else if (placementStatus)
            placementStatus.gameObject.SetActive(false);
    }
    private void OnDisable() => SetVisible(false);

    private void LateUpdate()
    {
        Camera view = gameplayCamera ? gameplayCamera : Camera.main;
        if (!gizmoController || !gizmoController.isActiveAndEnabled ||
            !gizmoController.PreviewTransform || !view || !canvas ||
            !buttonPanel || !toolRow || !actionRow)
        {
            SetVisible(false);
            return;
        }

        Transform ghost = gizmoController.PreviewTransform;
        if (trackedGhost != ghost)
        {
            trackedGhost = ghost;
            Bounds bounds = gizmoController.PreviewBounds;
            anchorOffset = bounds.center - ghost.position;
            restHeight = bounds.extents.y;
            lastDirection = Vector2.down;
            rowLayout = toolRow.GetComponent<HorizontalLayoutGroup>();
            if (!rowLayout)
                rowLayout = actionRow.GetComponent<HorizontalLayoutGroup>();
        }
        bool opening = !buttonPanel.activeSelf;
        SetVisible(true);
        bool canPlace = gizmoController.HasGround && gizmoController.PreviewValid;
        if (placeButton && placeButton.interactable != canPlace)
            placeButton.interactable = canPlace;
        if (axisLabel)
        {
            string axis = gizmoController.IsLocalSpace ? "Local Axis" : "World Axis";
            if (axisLabel.text != axis)
                axisLabel.text = axis;
        }

        HighlightTool(moveButton, !gizmoController.IsRotationTool);
        HighlightTool(rotateButton, gizmoController.IsRotationTool);

        Rect viewport = view.pixelRect;
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        if (opening)
            Canvas.ForceUpdateCanvases();

        Vector3 anchor = ghost.position + anchorOffset;
        Vector3 projected = view.WorldToScreenPoint(anchor);
        Vector2 center = projected;
        bool behind = projected.z <= view.nearClipPlane;
        if (behind)
        {
            Vector3 relative = anchor - view.transform.position;
            Vector2 direction = new(Vector3.Dot(relative, view.transform.right),
                                    Vector3.Dot(relative, view.transform.up));
            if (direction.sqrMagnitude > .0001f)
                lastDirection = direction.normalized;
            center = viewport.center + lastDirection * (viewport.size.magnitude * 2f);
        }
        else if ((center - viewport.center).sqrMagnitude > 1f)
            lastDirection = (center - viewport.center).normalized;

        float extent = behind ? 0f : Mathf.Abs(view.WorldToScreenPoint(anchor + view.transform.up * restHeight).y - center.y);
        Rect top = RowOffsets(toolRow, uiCamera);
        Rect bottom = RowOffsets(actionRow, uiCamera);
        float halfWidth = Mathf.Max(top.width, bottom.width) * .5f;
        float edgeDistance = Mathf.Min(Mathf.Min(center.x - viewport.xMin, viewport.xMax - center.x),
                                       Mathf.Min(center.y - viewport.yMin, viewport.yMax - center.y));
        float spacing = 8f * canvas.scaleFactor;
        if (rowLayout)
        {
            RectTransform row = (RectTransform)rowLayout.transform;
            Vector2 start = RectTransformUtility.WorldToScreenPoint(uiCamera, row.position);
            Vector2 end = RectTransformUtility.WorldToScreenPoint(uiCamera,
                row.TransformPoint(Vector3.right * Mathf.Max(0f, rowLayout.spacing)));
            spacing = Vector2.Distance(start, end);
        }

        bool showStatus = !canPlace && !behind && edgeDistance > 0f;
        float statusSpace = UpdatePlacementStatus(showStatus, uiCamera, out float statusHalfHeight);

        float clearance = extent + ghostPadding;
        float transition = Mathf.SmoothStep(0f, 1f, edgeDistance /
            Mathf.Max(1f, screenMargin + Mathf.Max(halfWidth, clearance + Mathf.Max(top.height, bottom.height))));
        float maxGap = Mathf.Max(0f, viewport.height - 2f * screenMargin - top.height - bottom.height - statusSpace);
        float gap = Mathf.Min(maxGap, Mathf.Lerp(spacing, Mathf.Max(spacing, clearance * 2f), transition));
        float x = ClampToViewport(center.x, viewport.xMin + screenMargin + halfWidth,
                                  viewport.xMax - screenMargin - halfWidth);
        float y = ClampToViewport(center.y, viewport.yMin + screenMargin + bottom.height + gap * .5f + statusSpace,
                                  viewport.yMax - screenMargin - top.height - gap * .5f);
        PlaceRow(toolRow, uiCamera, new Vector2(x - top.center.x, y + gap * .5f - top.yMin));
        PlaceRow(actionRow, uiCamera, new Vector2(x - bottom.center.x, y - gap * .5f - bottom.yMax));
        if (showStatus)
        {
            float statusY = y - gap * .5f - bottom.height - statusSpace + statusHalfHeight;
            float statusHalfWidth = (placementStatus.preferredWidth * .5f + statusOutlineWidth) * canvas.scaleFactor;
            float statusX = ClampToViewport(x, viewport.xMin + screenMargin + statusHalfWidth,
                                             viewport.xMax - screenMargin - statusHalfWidth);
            PlaceRow(statusRoot, uiCamera, new Vector2(statusX, statusY));
        }
    }

    private float UpdatePlacementStatus(bool visible, Camera uiCamera, out float halfHeight)
    {
        halfHeight = 0f;
        if (!visible)
        {
            if (statusRoot && statusRoot.gameObject.activeSelf)
                statusRoot.gameObject.SetActive(false);
            return 0f;
        }

        if (!statusRoot)
            ConfigurePlacementStatus();

        statusRoot.gameObject.SetActive(true);
        string message = gizmoController.IsOverForbiddenSurface ? "Invalid surface"
            : gizmoController.HasGround ? "Blocked" : "No ground";
        UpdateStatusText(placementStatus, message, statusTextColor);
        for (int i = 0; i < statusOutline.Length; i++)
        {
            TextMeshProUGUI outline = statusOutline[i];
            UpdateStatusText(outline, message, statusOutlineColor);
            outline.rectTransform.anchoredPosition = OutlineDirections[i] * statusOutlineWidth;
            outline.enabled = statusOutlineWidth > 0f && statusOutlineColor.a > 0f;
        }

        Vector2 start = RectTransformUtility.WorldToScreenPoint(uiCamera, statusRoot.position);
        Vector2 end = RectTransformUtility.WorldToScreenPoint(uiCamera, statusRoot.TransformPoint(Vector3.up));
        float pixelsPerUnit = Vector2.Distance(start, end);
        halfHeight = (placementStatus.preferredHeight * .5f + statusOutlineWidth) * pixelsPerUnit;
        return 2f * halfHeight + statusSpacing * pixelsPerUnit;
    }

    private void UpdateStatusText(TMP_Text label, string message, Color color)
    {
        if (label.text != message)
            label.text = message;
        if (label.color != color)
            label.color = color;
        if (label.fontSize != statusFontSize)
            label.fontSize = statusFontSize;
    }

    private void ConfigurePlacementStatus()
    {
        statusRoot = new GameObject("Placement Status Overlay", typeof(RectTransform)).GetComponent<RectTransform>();
        statusRoot.SetParent(buttonPanel.transform, false);
        statusRoot.sizeDelta = new Vector2(240f, 48f);
        if (!placementStatus)
        {
            placementStatus = CreateStatusText("Placement Status");
            if (axisLabel && axisLabel.font)
                placementStatus.font = axisLabel.font;
            placementStatus.fontStyle = FontStyles.Bold;
        }

        ConfigureStatusText(placementStatus);
        // Separate white glyphs keep the outline independent of the red text color.
        for (int i = 0; i < statusOutline.Length; i++)
        {
            TextMeshProUGUI outline = CreateStatusText("Outline");
            outline.font = placementStatus.font;
            outline.fontStyle = placementStatus.fontStyle;
            outline.characterSpacing = placementStatus.characterSpacing;
            outline.wordSpacing = placementStatus.wordSpacing;
            ConfigureStatusText(outline);
            statusOutline[i] = outline;
        }
        placementStatus.gameObject.SetActive(true);
        placementStatus.transform.SetAsLastSibling();
    }

    private static TextMeshProUGUI CreateStatusText(string objectName)
    {
        return new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI))
            .GetComponent<TextMeshProUGUI>();
    }

    private void ConfigureStatusText(TMP_Text label)
    {
        RectTransform rect = label.rectTransform;
        rect.SetParent(statusRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.sizeDelta = statusRoot.sizeDelta;
        label.alignment = TextAlignmentOptions.Center;
        label.margin = Vector4.zero;
        label.enableAutoSizing = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
    }

    private void SetVisible(bool visible)
    {
        if (buttonPanel && buttonPanel.activeSelf != visible)
            buttonPanel.SetActive(visible);
    }

    private static Rect RowOffsets(RectTransform row, Camera camera)
    {
        Vector2 pivot = RectTransformUtility.WorldToScreenPoint(camera, row.position);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, row.TransformPoint(row.rect.min)) - pivot;
        Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, row.TransformPoint(row.rect.max)) - pivot;
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

    private void HighlightTool(Button button, bool active)
    {
        if (button && button.targetGraphic)
            button.targetGraphic.color = active ? activeToolColor : button.colors.normalColor;
    }
}
