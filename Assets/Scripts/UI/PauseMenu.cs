using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PauseMenu : MonoBehaviour
{
    [Tooltip("Full-screen panel containing Go to Menu and Continue. Keep this component outside the panel.")]
    [SerializeField] private GameObject pausePanel;

    [Tooltip("Optional camera controller. Camera input is suspended while the panel is open.")]
    [SerializeField] private CameraController cameraController;

    [Tooltip("The scene's Object Gizmo Controller, on Main Camera.")]
    [SerializeField] private ObjectGizmoController gizmoController;

    private bool paused;
    private float previousTimeScale;


    private void Awake()
    {
        if (pausePanel)
            pausePanel.SetActive(false);
    }

    private void Update()
    {
        if (!paused && Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
            Open();
    }

    public void Open()
    {
        if (!isActiveAndEnabled || paused || !pausePanel)
            return;

        previousTimeScale = Time.timeScale;
        if (cameraController)
            cameraController.SetPauseInputBlocked(true);
        if (gizmoController)
            gizmoController.SetPaused(true);

        paused = true;
        Time.timeScale = 0f;
        pausePanel.SetActive(true);
#if UNITY_EDITOR
        if (pausePanel)
            Debug.Log($"Pause Menu opened '{pausePanel.name}' in scene '{pausePanel.scene.name}'. " +
                      $"Active checkbox: {pausePanel.activeSelf}. Active in hierarchy: {pausePanel.activeInHierarchy}.",
                      pausePanel);
#endif
        RestoreIfPanelHidden();
    }

    public void ContinueGame()
    {
        if (!paused)
            return;

        RestoreGameplay();
        if (pausePanel)
            pausePanel.SetActive(false);
    }

    private void LateUpdate() => RestoreIfPanelHidden();

    private void RestoreIfPanelHidden()
    {
        if (!paused || (pausePanel && pausePanel.activeInHierarchy))
            return;

        string reason = !pausePanel ? "the panel reference is missing"
            : pausePanel.activeSelf ? "a parent of the panel is inactive"
            : "the panel was deactivated";
        Debug.LogWarning($"Pause Menu resumed gameplay because {reason}.", pausePanel ? pausePanel : gameObject);
        ContinueGame();
    }

    private void OnDisable() => ContinueGame();

    private void RestoreGameplay()
    {
        paused = false;
        Time.timeScale = previousTimeScale;
        if (gizmoController)
            gizmoController.SetPaused(false);
        if (cameraController)
            cameraController.SetPauseInputBlocked(false);
    }
}

