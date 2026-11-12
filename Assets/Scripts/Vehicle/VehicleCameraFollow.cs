using UnityEngine;

/// <summary>
/// Smooth third-person chase camera for a vehicle. Put it on the camera and assign the target.
/// Pulls in closer when a wall or building would block the view.
/// </summary>
[DisallowMultipleComponent]
public class VehicleCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Framing")]
    [SerializeField] private float distance = 7.5f;
    [SerializeField] private float height = 3f;
    [Tooltip("Point above the car's origin the camera looks at.")]
    [SerializeField] private float lookHeight = 1.2f;

    [Header("Smoothing")]
    [SerializeField] private float positionDamping = 8f;
    [SerializeField] private float rotationDamping = 5f;

    [Header("Obstacle avoidance")]
    [SerializeField] private bool avoidObstacles = true;
    [SerializeField] private LayerMask obstacleMask = ~0;
    [SerializeField] private float cameraRadius = 0.3f;

    private float yaw;

    public Transform Target
    {
        get => target;
        set { target = value; SnapToTarget(); }
    }

    private void Start() => SnapToTarget();

    private void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        Vector3 forward = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f)
        {
            float targetYaw = Quaternion.LookRotation(forward).eulerAngles.y;
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-rotationDamping * dt));
        }

        Vector3 desired = DesiredPosition();
        transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-positionDamping * dt));
        transform.LookAt(LookPoint());
    }

    private Vector3 LookPoint() => target.position + Vector3.up * lookHeight;

    private Vector3 DesiredPosition()
    {
        Vector3 look = LookPoint();
        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height - lookHeight, -distance);
        Vector3 desired = look + offset;

        if (avoidObstacles &&
            Physics.SphereCast(look, cameraRadius, offset.normalized, out RaycastHit hit, offset.magnitude,
                               obstacleMask, QueryTriggerInteraction.Ignore) &&
            !hit.transform.IsChildOf(target))
        {
            desired = look + offset.normalized * Mathf.Max(hit.distance - cameraRadius, 1f);
        }
        return desired;
    }

    private void SnapToTarget()
    {
        if (target == null) return;
        yaw = target.eulerAngles.y;
        transform.position = DesiredPosition();
        transform.LookAt(LookPoint());
    }
}
