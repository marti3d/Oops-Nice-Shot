using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneChange : MonoBehaviour
{
    [Tooltip("Enabled scene name from the Build Profiles Scene List. Use the asset path if names repeat.")]
    [SerializeField] private string sceneName;

    private static AsyncOperation pendingLoad;

    public void LoadScene()
    {
        if (pendingLoad != null && !pendingLoad.isDone)
            return;

        string destination = sceneName?.Trim();
        if (string.IsNullOrEmpty(destination) || !Application.CanStreamedLevelBeLoaded(destination))
        {
            Debug.LogError($"Scene '{destination}' is unavailable. Add and enable it in the active Build Profile's Scene List.", this);
            return;
        }

        pendingLoad = SceneManager.LoadSceneAsync(destination, LoadSceneMode.Single);
    }
}
