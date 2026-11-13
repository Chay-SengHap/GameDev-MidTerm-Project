using System;
using UnityEngine;

/// <summary>
/// Physics-based player car built on Unity's <see cref="WheelCollider"/>s.
/// Handles acceleration, braking, reversing, steering, handbrake and keeps the wheel meshes in sync.
///
/// Setup (done automatically by Tools > City Driving > Set Up Game Scene):
///  - Rigidbody + a convex collider (e.g. BoxCollider) for the body on this object.
///  - One WheelCollider per wheel on child objects, assigned to <see cref="axles"/>.
///  - The visual wheel meshes assigned next to their WheelColliders (optional).
///
/// Collision reactions live in <see cref="VehicleCollisionHandler"/>, input in <see cref="VehicleInput"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(VehicleInput))]
[DisallowMultipleComponent]
public class PlayerVehicleController : PlayerVehicle
{
    [Serializable]
    public class Axle
    {
        public WheelCollider leftWheel;
        public WheelCollider rightWheel;
        [Tooltip("Optional wheel meshes that follow the WheelColliders.")]
        public Transform leftVisual;
        public Transform rightVisual;
        public bool motor;
        public bool steering;
        public bool handbrake;
    }

    [Header("Wheels")]
    [SerializeField] private Axle[] axles = Array.Empty<Axle>();

    [Header("Engine")]
    [Tooltip("Total torque shared between all motor wheels (Nm).")]
    [SerializeField] private float motorTorque = 2200f;
    [SerializeField] private float maxForwardSpeedKmh = 90f;
    [SerializeField] private float maxReverseSpeedKmh = 25f;
    [SerializeField, Range(0.1f, 1f)] private float reverseTorqueFactor = 0.6f;

    [Header("Brakes")]
    [SerializeField] private float brakeTorque = 3500f;
    [SerializeField] private float handbrakeTorque = 6000f;
    [Tooltip("Light braking while no pedal is pressed, so the car slowly rolls to a stop.")]
    [SerializeField] private float coastBrakeTorque = 150f;
    [Tooltip("Below this speed (m/s), pressing the opposite pedal switches direction instead of braking.")]
    [SerializeField] private float directionChangeSpeed = 1f;

    [Header("Steering")]
    [SerializeField] private float maxSteerAngle = 32f;
    [Tooltip("Steering angle at top speed - less steering when fast keeps the car stable.")]
    [SerializeField] private float steerAngleAtMaxSpeed = 10f;
    [Tooltip("How fast the wheels turn toward the target angle (degrees per second).")]
    [SerializeField] private float steerSpeed = 140f;

    [Header("Stability")]
    [Tooltip("Centre of mass in local space. Lower = harder to roll over.")]
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.35f, 0f);
    [Tooltip("Extra downward force per m/s of speed, keeps the car planted.")]
    [SerializeField] private float downforce = 40f;
    [Tooltip("Anti-roll bar stiffness per axle (N). 0 disables it.")]
    [SerializeField] private float antiRollForce = 6000f;
    [Tooltip("Height the car is lifted by when it is reset onto its wheels.")]
    [SerializeField] private float resetLift = 1.2f;

    private Rigidbody rb;
    private VehicleInput input;
    private float currentSteerAngle;
    private Quaternion[] leftVisualOffset, rightVisualOffset;

    /// Signed speed along the car's forward axis (m/s), negative while reversing.
    public override float ForwardSpeed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
    public override Rigidbody Body => rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<VehicleInput>();
        rb.centerOfMass = centerOfMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        CacheVisualOffsets();

        if (axles.Length == 0)
            Debug.LogWarning($"{name}: PlayerVehicleController has no axles assigned. " +
                             "Run Tools > City Driving > Set Up Game Scene or assign WheelColliders in the Inspector.", this);
    }

    private void FixedUpdate()
    {
        float throttle = ControlsEnabled ? input.Throttle : 0f;
        float steer = ControlsEnabled ? input.Steer : 0f;
        bool handbrake = !ControlsEnabled || input.Handbrake;

        float speed = ForwardSpeed;
        float motor = 0f, brake = 0f;

        if (throttle > 0.01f)
        {
            // Forward pedal while still rolling backward -> brake first.
            if (speed < -directionChangeSpeed) brake = brakeTorque * throttle;
            else motor = throttle * motorTorque;
        }
        else if (throttle < -0.01f)
        {
            // Back pedal while moving forward -> brake; once (nearly) stopped -> reverse.
            if (speed > directionChangeSpeed) brake = brakeTorque * -throttle;
            else motor = throttle * motorTorque * reverseTorqueFactor;
        }
        else
        {
            brake = coastBrakeTorque;
        }

        // Speed limiter.
        if (motor > 0f && speed * 3.6f >= maxForwardSpeedKmh) motor = 0f;
        if (motor < 0f && -speed * 3.6f >= maxReverseSpeedKmh) motor = 0f;

        // Less steering at high speed.
        float speedFactor = Mathf.Clamp01(Mathf.Abs(speed) * 3.6f / Mathf.Max(maxForwardSpeedKmh, 1f));
        float targetSteer = steer * Mathf.Lerp(maxSteerAngle, steerAngleAtMaxSpeed, speedFactor);
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetSteer, steerSpeed * Time.fixedDeltaTime);

        ApplyToWheels(motor, brake, handbrake);

        if (downforce > 0f)
            rb.AddForce(-transform.up * (downforce * rb.linearVelocity.magnitude));

        if (ControlsEnabled && input.ResetPressed) ResetOntoWheels();
    }

    private void LateUpdate()
    {
        for (int i = 0; i < axles.Length; i++)
        {
            SyncVisual(axles[i].leftWheel, axles[i].leftVisual, leftVisualOffset[i]);
            SyncVisual(axles[i].rightWheel, axles[i].rightVisual, rightVisualOffset[i]);
        }
    }

    // ------------------------------------------------------------------ public API

    /// Used by the editor setup tool to assign the wheels.
    public void SetAxles(Axle[] newAxles) => axles = newAxles ?? Array.Empty<Axle>();

    /// Puts the car upright at its current position, keeping its heading.
    public override void ResetOntoWheels()
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
        Teleport(transform.position + Vector3.up * resetLift, Quaternion.LookRotation(flatForward));
    }

    /// Moves the car and removes all its momentum.
    public override void Teleport(Vector3 position, Quaternion rotation)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
    }

    // ------------------------------------------------------------------ internals

    private void ApplyToWheels(float motor, float brake, bool handbrake)
    {
        int motorWheels = 0;
        foreach (var a in axles) if (a.motor) motorWheels += 2;
        float torquePerWheel = motorWheels > 0 ? motor / motorWheels : 0f;

        foreach (var a in axles)
        {
            float axleBrake = brake;
            if (handbrake && a.handbrake) axleBrake = Mathf.Max(axleBrake, handbrakeTorque);
            float axleMotor = a.motor && !(handbrake && a.handbrake) ? torquePerWheel : 0f;

            SetWheel(a.leftWheel, axleMotor, axleBrake, a.steering);
            SetWheel(a.rightWheel, axleMotor, axleBrake, a.steering);
            ApplyAntiRoll(a);
        }
    }

    private void SetWheel(WheelCollider wheel, float motor, float brake, bool steering)
    {
        if (wheel == null) return;
        wheel.motorTorque = motor;
        wheel.brakeTorque = brake;
        if (steering) wheel.steerAngle = currentSteerAngle;
    }

    /// Pushes down the more-compressed side so the car leans less in corners.
    private void ApplyAntiRoll(Axle a)
    {
        if (antiRollForce <= 0f || a.leftWheel == null || a.rightWheel == null) return;

        bool leftGrounded = a.leftWheel.GetGroundHit(out WheelHit leftHit);
        bool rightGrounded = a.rightWheel.GetGroundHit(out WheelHit rightHit);
        float leftTravel = leftGrounded ? SuspensionTravel(a.leftWheel, leftHit) : 1f;
        float rightTravel = rightGrounded ? SuspensionTravel(a.rightWheel, rightHit) : 1f;
        float force = (leftTravel - rightTravel) * antiRollForce;

        if (leftGrounded)
            rb.AddForceAtPosition(a.leftWheel.transform.up * -force, a.leftWheel.transform.position);
        if (rightGrounded)
            rb.AddForceAtPosition(a.rightWheel.transform.up * force, a.rightWheel.transform.position);
    }

    private static float SuspensionTravel(WheelCollider wheel, WheelHit hit)
    {
        float extension = -wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius;
        return extension / Mathf.Max(wheel.suspensionDistance, 0.001f);
    }

    private void CacheVisualOffsets()
    {
        leftVisualOffset = new Quaternion[axles.Length];
        rightVisualOffset = new Quaternion[axles.Length];
        for (int i = 0; i < axles.Length; i++)
        {
            leftVisualOffset[i] = VisualOffset(axles[i].leftWheel, axles[i].leftVisual);
            rightVisualOffset[i] = VisualOffset(axles[i].rightWheel, axles[i].rightVisual);
        }
    }

    // Wheel meshes may be modelled with a different rotation than the collider (e.g. mirrored left wheels).
    private static Quaternion VisualOffset(WheelCollider wheel, Transform visual)
    {
        if (wheel == null || visual == null) return Quaternion.identity;
        return Quaternion.Inverse(wheel.transform.rotation) * visual.rotation;
    }

    private static void SyncVisual(WheelCollider wheel, Transform visual, Quaternion offset)
    {
        if (wheel == null || visual == null) return;
        wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
        visual.SetPositionAndRotation(pos, rot * offset);
    }

    private void OnValidate()
    {
        steerAngleAtMaxSpeed = Mathf.Min(steerAngleAtMaxSpeed, maxSteerAngle);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(centerOfMass), 0.2f);
    }
}
