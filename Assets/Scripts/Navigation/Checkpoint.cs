using System;
using UnityEngine;

/// <summary>
/// A trigger volume the player has to drive through. Only reacts while it is the active target;
/// <see cref="CheckpointManager"/> decides the order. Move it anywhere along the road and
/// resize its trigger collider to cover the road width.
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Tooltip("Objects with this tag (on their Rigidbody) can collect the checkpoint.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("Marker shown only while this is the current target (beacon, ring...). Optional.")]
    [SerializeField] private GameObject activeVisual;

    public bool IsActive { get; private set; }

    /// Raised once when the player enters the active checkpoint.
    public event Action<Checkpoint> Reached;

    // Called when the component is added in the editor: make sure there is a trigger volume.
    private void Reset() => EnsureTrigger();

    private void Awake()
    {
        EnsureTrigger();
        SetActive(false);
    }

    private void EnsureTrigger()
    {
        var col = GetComponent<Collider>();
        if (col == null)
        {
            var box = gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(12f, 6f, 12f);
            box.center = new Vector3(0f, 3f, 0f);
            col = box;
        }
        col.isTrigger = true;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
        if (activeVisual != null) activeVisual.SetActive(active);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsActive) return;
        Rigidbody body = other.attachedRigidbody;
        GameObject who = body != null ? body.gameObject : other.gameObject;
        if (!who.CompareTag(playerTag)) return;

        SetActive(false);   // a car has several colliders: make sure it only counts once
        Reached?.Invoke(this);
    }

    private void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        Gizmos.color = new Color(0.1f, 0.9f, 1f, IsActive ? 0.5f : 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        if (box != null) Gizmos.DrawCube(box.center, box.size);
        else Gizmos.DrawSphere(Vector3.zero, 2f);
    }
}
