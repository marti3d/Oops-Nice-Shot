using UnityEngine;

[DefaultExecutionOrder(101)]
public sealed class GizmoButtonVisibility : MonoBehaviour
{
    [Tooltip("The scene controller that manages object selection and placement.")]
    [SerializeField] private ObjectGizmoController gizmoController;

    [Tooltip("The child panel containing the five gizmo buttons.")]
    [SerializeField] private GameObject buttonPanel;

    private void OnEnable() => RefreshVisibility();

    private void LateUpdate() => RefreshVisibility();

    private void OnDisable()
    {
        if (buttonPanel)
            buttonPanel.SetActive(false);
    }

    private void RefreshVisibility()
    {
        if (!buttonPanel)
            return;

        bool visible = gizmoController && gizmoController.isActiveAndEnabled &&
                       gizmoController.SelectedObject && gizmoController.PreviewTransform;
        if (buttonPanel.activeSelf != visible)
            buttonPanel.SetActive(visible);
    }
}
