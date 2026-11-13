using UnityEngine;

/// <summary>
/// A 3D arrow that floats above the player and points toward the current checkpoint.
/// It is a separate object (not a child of the car) so it stays level when the car tilts.
/// The arrow model's tip must point along its local +Z axis.
/// </summary>
[DisallowMultipleComponent]
public class NavigationArrow : MonoBehaviour
{
    [SerializeField] private CheckpointManager checkpoints;
    [Tooltip("The object the arrow hovers over (the player vehicle).")]
    [SerializeField] private Transform follow;
    [SerializeField] private Vector3 offset = new Vector3(0f, 3.2f, 0f);
    [SerializeField] private float turnSpeed = 10f;
    [Tooltip("Child holding the arrow mesh; hidden when there is no target.")]
    [SerializeField] private GameObject visual;

    /// Flat distance from the followed object to the current checkpoint (-1 if none).
    public float DistanceToTarget { get; private set; } = -1f;

    private void LateUpdate()
    {
        Checkpoint target = checkpoints != null ? checkpoints.Current : null;
        bool show = follow != null && target != null;
        if (visual != null && visual.activeSelf != show) visual.SetActive(show);
        if (!show)
        {
            DistanceToTarget = -1f;
            return;
        }

        transform.position = follow.position + offset;

        Vector3 toTarget = target.transform.position - follow.position;
        toTarget.y = 0f;
        DistanceToTarget = toTarget.magnitude;
        if (toTarget.sqrMagnitude < 0.01f) return;

        Quaternion look = Quaternion.LookRotation(toTarget);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
    }
}
