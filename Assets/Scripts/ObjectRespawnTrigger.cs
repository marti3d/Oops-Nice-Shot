using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public sealed class ObjectRespawnTrigger : MonoBehaviour
{
    [SerializeField] private ObjectGizmoController gizmoController;

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        if (gizmoController == null)
            gizmoController = FindAnyObjectByType<ObjectGizmoController>();
        if (gizmoController == null)
            Debug.LogError("Assign ObjectGizmoController to the respawn trigger.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (gizmoController != null && other.attachedRigidbody != null)
            gizmoController.Respawn(other.attachedRigidbody);
    }
}
