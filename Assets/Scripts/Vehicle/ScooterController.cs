using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rideable delivery scooter with the courier on it. Put it on the scooter root (e.g. "SitPoint").
///
/// Driving (keys come from <see cref="VehicleInput"/>):
///   W / Shift - throttle       S - brake (S at a standstill pushes the scooter back slowly)
///   A / D     - steer         Space - rear brake (locks the rear wheel, lets the back slide)
///   E         - get off / get on (only when nearly stopped / standing next to the scooter)
///   H         - horn          L - headlight          F - put the scooter back upright
///
/// The scooter model is one flat list of meshes without pivots, so on Awake this script builds a small rig:
///   root (Rigidbody, colliders)
///     └ Lean Pivot           - leans into corners, pitches when accelerating / braking, rocks over bumps
///         ├ body parts, rider
///         ├ Steer Pivot      - handlebar, fork, headlamp, mirrors... turn around the real steering axis
///         │   └ Front Wheel  - spins with the road speed
///         ├ Rear Wheel       - spins (locks while the rear brake is held)
///         └ Side Stand       - folds up while riding, comes down when parked
/// Physics: Unity's physics engine drives it through two WheelColliders (suspension, tyre grip, shared engine torque,
/// brakes, steering on the front). The body is kept upright (it can't fall over); the lean into
/// corners is visual. Rider body motion lives in <see cref="ScooterRiderMotion"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(VehicleInput))]
[DefaultExecutionOrder(-50)]   // rig is posed before ScooterRiderPose / ScooterRiderMotion run in LateUpdate
[DisallowMultipleComponent]
public class ScooterController : PlayerVehicle
{
    [Header("Engine")]
    [SerializeField] private float maxSpeedKmh = 55f;
    [Tooltip("Acceleration from standstill (m/s²). Falls off towards top speed.")]
    [SerializeField] private float acceleration = 5.5f;
    [Tooltip("Pushing the scooter backwards with the feet.")]
    [SerializeField] private float maxReverseSpeedKmh = 12f;
    [SerializeField] private float reverseAcceleration = 3.5f;

    [Header("Brakes & Drag")]
    [SerializeField] private float brakeDeceleration = 9f;
    [SerializeField] private float rearBrakeDeceleration = 5f;
    [Tooltip("Engine braking / rolling resistance when no key is pressed (m/s²). Higher = stops sooner.")]
    [SerializeField] private float rollingDrag = 2.5f;
    [SerializeField] private float airDrag = 0.004f;
    [Tooltip("Below this speed (m/s) S switches from braking to pushing back.")]
    [SerializeField] private float directionChangeSpeed = 0.4f;

    [Header("Steering")]
    [SerializeField] private float maxSteerAngle = 25f;
    [Tooltip("Steering angle at top speed - small, so the scooter stays stable when fast.")]
    [SerializeField] private float steerAngleAtMaxSpeed = 8f;
    [Tooltip("How fast the handlebar follows the input (full lock per second).")]
    [SerializeField] private float steerRate = 3f;
    [SerializeField] private float steerReturnRate = 4f;
    [Tooltip("Handlebar speed at top speed, as a fraction of Steer Rate (calmer steering when fast).")]
    [SerializeField, Range(0.2f, 1f)] private float steerRateAtMaxSpeed = 0.8f;
    [Tooltip("Cornering limit (m/s²): the steering angle is reduced at speed so turns never exceed this.")]
    [SerializeField] private float maxCornerAccel = 6f;

    [Tooltip("Helps the tyres turn the scooter as much as the handlebar asks (0 = pure tyre physics).")]
    [SerializeField] private float steeringAssist = 10f;

    [Header("Wheels & Suspension (physics)")]
    [Tooltip("Scooter mass in kg (the rider is not simulated as a separate body).")]
    [SerializeField] private float mass = 180f;
    [SerializeField] private float suspensionDistance = 0.24f;
    [SerializeField] private float springStrength = 35000f;
    [SerializeField] private float springDamper = 3200f;
    [Tooltip("Tyre grip along the rolling direction (acceleration / braking).")]
    [SerializeField] private float forwardGrip = 1.8f;
    [Tooltip("Tyre grip sideways (cornering). Higher = less sliding.")]
    [SerializeField] private float sidewaysGrip = 2.2f;
    [Tooltip("Rear tyre sideways grip while the rear brake is held - lets the back slide.")]
    [SerializeField] private float rearBrakeSidewaysGrip = 0.9f;
    [Tooltip("Pushes the scooter onto the road, per m/s of speed (N). Keeps it from hopping over seams and kerbs.")]
    [SerializeField] private float downforce = 30f;
    [Tooltip("Gravity multiplier while airborne, so jumps land quickly instead of floating.")]
    [SerializeField] private float airGravity = 2f;

    [Header("Body Motion (visual)")]
    [SerializeField] private float maxLeanAngle = 30f;
    [SerializeField] private float leanSmoothing = 6f;
    [Tooltip("Lean towards the rider's foot when stopped with the rider on.")]
    [SerializeField] private float stoppedLeanAngle = 4f;
    [Tooltip("Lean onto the side stand when parked.")]
    [SerializeField] private float parkedLeanAngle = 7f;
    [Tooltip("Nose up / dive per m/s² of acceleration.")]
    [SerializeField] private float pitchPerAccel = 0.2f;
    [SerializeField] private float maxPitch = 2f;
    [Tooltip("Small up/down rocking while riding (m).")]
    [SerializeField] private float roadBumpHeight = 0.003f;

    [Header("Rider")]
    [SerializeField] private bool startMounted = true;
    [Tooltip("Fastest speed (m/s) at which the rider can still get off.")]
    [SerializeField] private float maxDismountSpeed = 1.5f;
    [Tooltip("How close (m) the courier must be to get back on.")]
    [SerializeField] private float mountRange = 2.2f;
    [SerializeField] private bool showHints = true;
    [Tooltip("Height of the hip joint above the saddle surface (m).")]
    [SerializeField] private float seatHeight = 0.1f;

    [Header("Lights & Sound")]
    [SerializeField] private bool headlightOnAtStart = true;
    [SerializeField] private float headlightIntensity = 6f;
    [SerializeField] private float headlightRange = 30f;
    [SerializeField] private bool engineSound = true;
    [SerializeField, Range(0f, 1f)] private float engineVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float hornVolume = 0.6f;

    [Header("Ground")]
    [SerializeField] private LayerMask groundLayers = ~0;

    private const float MaxCurbHeight = 0.25f;
    private const float MaxCurbAssistSpeed = 4f;
    private const float MaxCurbLiftSpeed = 1.6f;

    // ---- State read by ScooterRiderMotion / UI ----
    public bool IsMounted { get; private set; }
    public bool IsGrounded { get; private set; }
    /// Smoothed steering input, -1 (left) .. +1 (right).
    public float SteerAmount { get; private set; }
    public float ThrottleInput { get; private set; }
    public bool IsBraking { get; private set; }
    /// Visual lean in degrees, positive = leaning right.
    public float LeanAngle => lean;
    /// Smoothed forward acceleration (m/s²).
    public float LongitudinalAccel { get; private set; }
    public float MaxSpeed => maxSpeedKmh / 3.6f;
    /// 0 = feet on the footboard, 1 = foot down on the road.
    public float FootDown { get; private set; }
    /// +1 = rider puts the right foot down, -1 = left foot (the side stand side).
    public float FootSide { get; private set; } = -1f;
    public Transform LeftGrip { get; private set; }
    public Transform RightGrip { get; private set; }
    public Transform LeanPivot => leanPivot;
    public Transform Rider => riderRoot;

    public override float ForwardSpeed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;
    public override Rigidbody Body => rb;

    private Rigidbody rb;
    private VehicleInput input;
    private ScooterRiderPose riderPose;
    private ScooterRiderMotion riderMotion;
    private DeliveryPackageController deliveryPackages;
    private Transform riderRoot;

    // Rig
    private Transform leanPivot, steerPivot, frontWheel, rearWheel, standPivot;
    private Vector3 steerAxis;                // in lean-pivot space
    private Vector3 standAxis;
    private float standFoldAngle;
    private float wheelRadius = 0.25f, wheelBase = 1.3f, frontContactZ = 0.65f, rearContactZ = -0.65f;
    private Light headlight, brakeLight;
    private AudioSource engineSource, hornSource;

    // Rider pose cached while seated, restored when getting back on.
    private readonly List<(Transform t, Vector3 pos, Quaternion rot)> riderSeatedPose = new();
    private Vector3 riderSeatPos;
    private Bounds saddle;   // lean-pivot space, size zero if the model has no saddle part
    private Quaternion riderSeatRot;

    // Runtime values
    private float steerAngle, lean, pitch, frontSpin, rearSpin, standAmount = 1f;
    private float lastForwardSpeed, lateralAccel, lastGroundedTime = -10f;
    private WheelCollider frontCollider, rearCollider;
    private bool waitForThrottleRelease;   // after getting on: ignore a key still held from running
    private Vector3 lastGoodPosition;
    private Quaternion lastGoodRotation = Quaternion.identity;
    private bool headlightOn;
    private string hint;

    // =====================================================================================
    // Setup
    // =====================================================================================

    /// Pressing Play in a scene that has the scooter but no player vehicle yet (Tools > City Driving not run):
    /// make the scooter rideable anyway, with a chase camera.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoSetUp()
    {
        if (FindFirstObjectByType<PlayerVehicle>() != null) return;
        var pose = FindFirstObjectByType<ScooterRiderPose>();
        if (pose == null) return;

        GameObject root = HierarchyGroup.PieceRoot(pose.transform).gameObject;
        var scooter = root.AddComponent<ScooterController>();   // also adds Rigidbody + VehicleInput
        if (root.GetComponent<VehicleCollisionHandler>() == null) root.AddComponent<VehicleCollisionHandler>();

        var cam = Camera.main;
        if (cam != null)
        {
            var follow = cam.GetComponent<VehicleCameraFollow>();
            if (follow == null) follow = cam.gameObject.AddComponent<VehicleCameraFollow>();
            follow.SetFraming(4.8f, 2.1f, 1.1f);
            follow.Target = scooter.transform;
        }
        Debug.Log($"'{root.name}' was set up as a rideable scooter for this play session. " +
                  "Run Tools > City Driving > Use Scooter As Player to save it in the scene with the timer, route and HUD.", root);
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<VehicleInput>();
        riderPose = GetComponentInChildren<ScooterRiderPose>();
        FindRider();
        BuildRig();
        deliveryPackages = GetComponent<DeliveryPackageController>();
        if (deliveryPackages == null) deliveryPackages = gameObject.AddComponent<DeliveryPackageController>();
        deliveryPackages.Bind(this, riderRoot);
        SetUpPhysics();
        Vector3 start = SafeSpot(transform.position);
        transform.position = start;
        rb.position = start;
        lastGoodPosition = start;
        lastGoodRotation = rb.rotation;
        if (!System.Array.Exists(Physics.RaycastAll(transform.position + Vector3.up * 1.5f, Vector3.down, 3f, groundLayers,
                                                    QueryTriggerInteraction.Ignore), h => h.rigidbody != rb))
            Debug.LogWarning($"{name}: no collider under the scooter, it will fall through the floor. " +
                             "Give the road/ground under it a collider.", this);
        SetUpLights();
        if (engineSound) SetUpAudio();

        if (riderRoot != null)
        {
            riderMotion = riderRoot.GetComponentInParent<ScooterRiderMotion>();
            if (riderMotion == null) riderMotion = gameObject.AddComponent<ScooterRiderMotion>();
            riderMotion.Bind(this, riderRoot);
            SeatRider();
            CacheSeatedPose();
        }

        IsMounted = riderRoot == null || startMounted;
        if (riderRoot != null && !startMounted) GetOff(force: true);
        standAmount = IsMounted ? 0f : 1f;
        headlightOn = headlightOnAtStart;
    }

    private void FindRider()
    {
        Transform hips = riderPose != null ? riderPose.hips : null;
        if (hips == null) hips = FindDeep(transform, "Hips");
        if (hips == null) return;

        // The rider is the direct child of the scooter root that holds the skeleton.
        Transform t = hips;
        while (t.parent != null && t.parent != transform) t = t.parent;
        if (t.parent == transform) riderRoot = t;

        // Clips would fight the seated pose; CharacterAnimator turns them back on when walking.
        var anim = riderRoot != null ? riderRoot.GetComponentInChildren<Animation>() : null;
        if (anim != null) anim.enabled = false;
    }

    private enum Part { Body, Steer, FrontWheel, RearWheel, Stand }

    private static readonly string[] SteerKeywords =
    {
        "handlebar", "handgrip", "brake lever", "lever pivot", "control switch", "mirror", "instrument",
        "headlamp", "front indicator", "indicator mount", "indicator backplate", "triple clamp", "stanchion",
        "fork", "front axle", "front curved mudguard", "front fender", "front disc brake", "front brake hose",
    };

    private static Part Classify(string name)
    {
        string n = name.ToLowerInvariant();
        // Wheel parts are named "Scooter front wheel ..." - don't match body panels such as
        // "leg shield with front wheel clearance", they must not spin.
        if (IsWheelPart(n, "front wheel")) return Part.FrontWheel;
        if (IsWheelPart(n, "rear wheel")) return Part.RearWheel;
        if (n.Contains("deployed side stand") || n.Contains("stand ground shoe")) return Part.Stand;
        foreach (var k in SteerKeywords) if (n.Contains(k)) return Part.Steer;
        return Part.Body;
    }

    private static bool IsWheelPart(string lowerName, string wheel)
    {
        int i = lowerName.IndexOf(wheel, System.StringComparison.Ordinal);
        if (i < 0) return false;
        string before = lowerName.Substring(0, i).Trim();
        return before.Length == 0 || before == "scooter" || before == "delivery";
    }

    private void BuildRig()
    {
        var parts = new List<Transform>();
        foreach (Transform child in transform)
            if (child != riderRoot && child.GetComponentInChildren<Renderer>() != null) parts.Add(child);

        var groups = new Dictionary<Part, List<Transform>>();
        foreach (Part p in System.Enum.GetValues(typeof(Part))) groups[p] = new List<Transform>();
        foreach (var p in parts) groups[Classify(p.name)].Add(p);

        // Which way does the model face? From the rear tyre to the front tyre.
        Vector3 frontC = WorldCenter(Tyre(groups[Part.FrontWheel]), transform.position + transform.forward);
        Vector3 rearC = WorldCenter(Tyre(groups[Part.RearWheel]), transform.position - transform.forward);
        Vector3 forward = Vector3.ProjectOnPlane(frontC - rearC, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f) forward = transform.forward;
        Quaternion facing = Quaternion.LookRotation(forward.normalized, Vector3.up);

        // Re-root so this object's +Z is the scooter's forward, without moving anything in the world.
        leanPivot = new GameObject("Lean Pivot").transform;
        leanPivot.SetPositionAndRotation(transform.position, facing);
        foreach (var p in parts) p.SetParent(leanPivot, true);
        if (riderRoot != null) riderRoot.SetParent(leanPivot, true);
        transform.rotation = facing;
        rb.rotation = facing;
        leanPivot.SetParent(transform, true);

        // Wheel geometry (local space, +Z forward).
        Bounds front = LocalBounds(Tyre(groups[Part.FrontWheel]), leanPivot);
        Bounds rear = LocalBounds(Tyre(groups[Part.RearWheel]), leanPivot);
        if (front.size != Vector3.zero && rear.size != Vector3.zero)
        {
            wheelRadius = Mathf.Max(0.1f, (front.extents.y + rear.extents.y) * 0.5f);
            frontContactZ = front.center.z;
            rearContactZ = rear.center.z;
            wheelBase = Mathf.Max(0.5f, frontContactZ - rearContactZ);
        }
        Vector3 frontHub = front.size != Vector3.zero ? front.center : new Vector3(0f, wheelRadius, frontContactZ);
        Vector3 rearHub = rear.size != Vector3.zero ? rear.center : new Vector3(0f, wheelRadius, rearContactZ);

        // Steering head: through the fork clamps (or the handlebar) down to the front axle.
        Bounds head = LocalBounds(Renderers(groups[Part.Steer].FindAll(t => t.name.ToLowerInvariant().Contains("triple clamp"))), leanPivot);
        if (head.size == Vector3.zero)
            head = LocalBounds(Renderers(groups[Part.Steer].FindAll(t => t.name.ToLowerInvariant().Contains("handlebar"))), leanPivot);
        Vector3 headTop = head.size != Vector3.zero ? head.center : frontHub + new Vector3(0f, 0.6f, -0.2f);
        steerAxis = (headTop - frontHub).normalized;

        steerPivot = NewPivot("Steer Pivot", leanPivot, headTop);
        foreach (var p in groups[Part.Steer]) p.SetParent(steerPivot, true);
        // frontHub is in lean-pivot space: create the pivot there, then hang it under the steering.
        frontWheel = NewPivot("Front Wheel", leanPivot, frontHub);
        frontWheel.SetParent(steerPivot, true);
        foreach (var p in groups[Part.FrontWheel]) p.SetParent(frontWheel, true);
        rearWheel = NewPivot("Rear Wheel", leanPivot, rearHub);
        foreach (var p in groups[Part.RearWheel]) p.SetParent(rearWheel, true);

        // Grips for the rider's hands.
        foreach (var p in groups[Part.Steer])
        {
            if (!p.name.ToLowerInvariant().Contains("handgrip")) continue;
            Bounds b = LocalBounds(Renderers(new List<Transform> { p }), leanPivot);
            var grip = new GameObject(b.center.x < 0f ? "Grip L" : "Grip R").transform;
            grip.SetParent(leanPivot, false);
            grip.localPosition = b.center;
            grip.SetParent(steerPivot, true);
            if (b.center.x < 0f) LeftGrip = grip; else RightGrip = grip;
        }

        saddle = LocalBounds(Renderers(groups[Part.Body].FindAll(t => t.name.ToLowerInvariant().Contains("saddle"))), leanPivot);
        BuildStand(groups);
    }

    /// Moves the rider so the hips sit on the saddle (centred, at the saddle's height), whatever the scene offsets are.
    private void SeatRider()
    {
        if (saddle.size == Vector3.zero) return;
        if (riderPose != null) { riderPose.FindBones(); riderPose.ApplyPose(); }
        Transform hips = riderPose != null && riderPose.hips != null ? riderPose.hips : FindDeep(riderRoot, "Hips");
        if (hips == null) return;

        Vector3 current = leanPivot.InverseTransformPoint(hips.position);
        Vector3 target = new Vector3(saddle.center.x, saddle.max.y + seatHeight,
                                     Mathf.Clamp(current.z, saddle.min.z + 0.05f, saddle.max.z - 0.05f));
        riderRoot.position += leanPivot.TransformVector(target - current);
    }

    private void BuildStand(Dictionary<Part, List<Transform>> groups)
    {
        var stand = groups[Part.Stand];
        if (stand.Count == 0) return;

        Bounds sb = LocalBounds(Renderers(stand), leanPivot);
        FootSide = sb.center.x >= 0f ? 1f : -1f;   // rider puts down the foot on the stand side

        // Hinge: the hinge bolt if there is one, else the top end of the stand.
        Vector3 hinge = new Vector3(sb.center.x, sb.max.y, sb.max.z);
        foreach (Transform t in leanPivot)
            if (t.name.ToLowerInvariant().Contains("side stand hinge"))
            {
                hinge = LocalBounds(Renderers(new List<Transform> { t }), leanPivot).center;
                break;
            }

        standPivot = NewPivot("Side Stand", leanPivot, hinge);
        foreach (var p in stand) p.SetParent(standPivot, true);

        // Fold it up around the sideways axis until the foot is level with the hinge; pick the direction that lifts the foot.
        Vector3 foot = new Vector3(sb.center.x, sb.min.y, sb.min.z) - hinge;
        standAxis = Vector3.right;
        standFoldAngle = Vector3.Angle(foot, Vector3.ProjectOnPlane(foot, Vector3.up)) + 8f;
        if ((Quaternion.AngleAxis(standFoldAngle, standAxis) * foot).y < foot.y) standFoldAngle = -standFoldAngle;
    }

    private void SetUpPhysics()
    {
        rb.mass = mass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.05f;   // low, or it fights every turn
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Unity's default push-out speed for overlapping colliders is ~1e32 m/s: a scooter that starts inside a kerb,
        // floor tile or prop is fired out at "infinite" speed and its position becomes NaN. Cap it, and the speeds.
        rb.maxDepenetrationVelocity = 2f;
        rb.maxLinearVelocity = 40f;
        rb.maxAngularVelocity = 8f;
        // Keep it upright: a two-wheeler can't balance by itself. Constraints lock WORLD axes, so both X and Z must be
        // frozen - freezing one only locks the roll while the scooter faces along that axis, and it tips over in turns.
        // Yaw stays free; the wheels' suspension still handles bumps and kerbs.
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.centerOfMass = new Vector3(0f, 0.35f, (frontContactZ + rearContactZ) * 0.5f);

        bool designerColliders = GetComponent<Collider>() != null;

        // Wheels: the physics engine handles suspension, grip and rolling over the road.
        var holder = new GameObject("Wheel Colliders").transform;
        holder.SetParent(transform, false);
        frontCollider = CreateWheelCollider(holder, "Front", new Vector3(0f, wheelRadius, frontContactZ));
        rearCollider = CreateWheelCollider(holder, "Rear", new Vector3(0f, wheelRadius, rearContactZ));
        frontCollider.ConfigureVehicleSubsteps(5f, 12, 15);   // steadier at low speed, no creeping when parked

        if (designerColliders) { SetInertia(); return; }   // keep colliders the designer added

        // Raise the low-friction body box clear of curbs while keeping coverage for walls and cars.
        var bodyMaterial = new PhysicsMaterial("ScooterBody")
        {
            dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
        };
        Bounds all = LocalBounds(leanPivot.GetComponentsInChildren<MeshRenderer>(), transform);
        float bottom = wheelRadius + 0.22f;
        float top = Mathf.Max(all.max.y, 1.45f);
        var body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, (bottom + top) * 0.5f, all.center.z);
        body.size = new Vector3(Mathf.Min(all.size.x, 0.6f), top - bottom, Mathf.Max(all.size.z, wheelBase));
        body.material = bodyMaterial;
        SetInertia();
    }

    /// The Rigidbody was created before any collider existed, so Unity's automatic inertia can end up zero on an axis,
    /// and the first wheel force then spins it infinitely fast (NaN). Give it a solid box-shaped inertia instead.
    private void SetInertia()
    {
        Bounds b = LocalBounds(leanPivot.GetComponentsInChildren<MeshRenderer>(), transform);
        float w = Mathf.Max(b.size.x, 0.5f), h = Mathf.Max(b.size.y, 1f), l = Mathf.Max(b.size.z, wheelBase);
        float k = rb.mass / 12f;
        rb.inertiaTensor = new Vector3(k * (h * h + l * l), k * (w * w + l * l), k * (w * w + h * h));
        rb.inertiaTensorRotation = Quaternion.identity;
        rb.centerOfMass = new Vector3(0f, 0.35f, (frontContactZ + rearContactZ) * 0.5f);
        lastGoodPosition = rb.position;
        lastGoodRotation = rb.rotation;
    }

    private WheelCollider CreateWheelCollider(Transform holder, string label, Vector3 hubLocal)
    {
        var go = new GameObject(label + " Wheel Collider");
        go.transform.SetParent(holder, false);
        // Raise it by half the suspension travel plus the sag under the scooter's weight,
        // so at rest the tyre touches the road exactly where the wheel mesh is.
        float sag = rb.mass * -Physics.gravity.y * 0.5f / Mathf.Max(springStrength, 1f);
        go.transform.localPosition = hubLocal + Vector3.up * (suspensionDistance * 0.5f + sag);

        var wc = go.AddComponent<WheelCollider>();
        wc.radius = wheelRadius;
        wc.mass = 10f;
        wc.wheelDampingRate = 0.4f;
        wc.suspensionDistance = suspensionDistance;
        wc.forceAppPointDistance = 0.05f;   // low force point = less pitching under throttle / brakes
        var spring = wc.suspensionSpring;
        spring.spring = springStrength;
        spring.damper = springDamper;
        spring.targetPosition = 0.5f;
        wc.suspensionSpring = spring;
        SetGrip(wc, forwardGrip, sidewaysGrip);
        return wc;
    }

    private static void SetGrip(WheelCollider wc, float forward, float sideways)
    {
        var f = wc.forwardFriction;
        f.stiffness = forward;
        wc.forwardFriction = f;
        var sf = wc.sidewaysFriction;
        sf.stiffness = sideways;
        wc.sidewaysFriction = sf;
    }

    private void SetUpLights()
    {
        Transform lamp = FindChildContaining(steerPivot, "headlamp finite curved optic") ?? FindChildContaining(steerPivot, "headlamp");
        Vector3 lampPos = lamp != null ? WorldCenter(lamp.GetComponentsInChildren<Renderer>(), steerPivot.position)
                                       : steerPivot.position + transform.forward * 0.15f;
        headlight = new GameObject("Headlight").AddComponent<Light>();
        headlight.transform.SetParent(steerPivot, false);
        headlight.transform.position = lampPos + transform.forward * 0.05f;
        headlight.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);
        headlight.type = LightType.Spot;
        headlight.spotAngle = 65f;
        headlight.innerSpotAngle = 35f;
        headlight.range = headlightRange;
        headlight.intensity = headlightIntensity;
        headlight.color = new Color(1f, 0.96f, 0.85f);
        headlight.shadows = LightShadows.None;

        Transform tail = FindChildContaining(leanPivot, "rear lamp");
        Vector3 tailPos = tail != null ? WorldCenter(tail.GetComponentsInChildren<Renderer>(), tail.position)
                                       : transform.TransformPoint(0f, 0.6f, rearContactZ - wheelRadius);
        brakeLight = new GameObject("Brake Light").AddComponent<Light>();
        brakeLight.transform.SetParent(leanPivot, false);
        brakeLight.transform.position = tailPos - transform.forward * 0.08f;
        brakeLight.type = LightType.Point;
        brakeLight.range = 2f;
        brakeLight.color = new Color(1f, 0.08f, 0.05f);
        brakeLight.shadows = LightShadows.None;
    }

    private void SetUpAudio()
    {
        engineSource = gameObject.AddComponent<AudioSource>();
        engineSource.clip = ScooterSounds.Engine();
        engineSource.loop = true;
        engineSource.spatialBlend = 0.7f;
        engineSource.volume = 0f;
        engineSource.dopplerLevel = 0f;
        engineSource.Play();

        hornSource = gameObject.AddComponent<AudioSource>();
        hornSource.clip = ScooterSounds.Horn();
        hornSource.loop = true;
        hornSource.spatialBlend = 0.7f;
        hornSource.volume = hornVolume;
        hornSource.playOnAwake = false;
    }

    private void CacheSeatedPose()
    {
        riderSeatPos = riderRoot.localPosition;
        riderSeatRot = riderRoot.localRotation;
        riderSeatedPose.Clear();
        foreach (var t in riderRoot.GetComponentsInChildren<Transform>(true))
            riderSeatedPose.Add((t, t.localPosition, t.localRotation));
    }

    // =====================================================================================
    // Input & physics
    // =====================================================================================

    private void Update()
    {
        bool canAct = ControlsEnabled;
        if (canAct && input.InteractPressed && riderRoot != null)
        {
            if (IsMounted) TryGetOff();
            else if (deliveryPackages == null || !deliveryPackages.TryInteract()) TryGetOn();
        }
        if (input.LightTogglePressed && IsMounted) headlightOn = !headlightOn;

        if (hornSource != null)
        {
            bool horn = IsMounted && input.HornHeld;
            if (horn && !hornSource.isPlaying) hornSource.Play();
            else if (!horn && hornSource.isPlaying) hornSource.Stop();
        }
        UpdateHint();
    }

    private void FixedUpdate()
    {
        // Safety net: if the physics ever produced NaN / infinity, put the scooter back where it last was fine
        // instead of letting the bad numbers spread to the rider, the camera and the renderer.
        if (!IsFinite(rb.linearVelocity) || !IsFinite(rb.angularVelocity) || !IsFinite(rb.position))
        {
            Debug.LogWarning($"{name}: physics produced invalid values - restored the last good position.", this);
            Teleport(SafeSpot(IsFinite(lastGoodPosition) ? lastGoodPosition : transform.parent != null
                                  ? transform.parent.position : Vector3.zero), lastGoodRotation);
            return;
        }
        lastGoodPosition = rb.position;
        lastGoodRotation = rb.rotation;

        float dt = Time.fixedDeltaTime;
        bool drive = ControlsEnabled && IsMounted;
        float throttle = drive ? input.Throttle : 0f;
        // Shift is also "run": if it is still held from running when getting on, don't ride off until it's released.
        if (waitForThrottleRelease)
        {
            if (throttle <= 0.05f) waitForThrottleRelease = false;
            else throttle = 0f;
        }
        float steer = drive ? input.Steer : 0f;
        bool rearBrake = drive && input.Handbrake;
        ThrottleInput = throttle;

        Vector3 v = rb.linearVelocity;
        float speed = Vector3.Dot(v, transform.forward);
        float absSpeed = Mathf.Abs(speed);
        float speed01 = Mathf.Clamp01(absSpeed / MaxSpeed);

        // Handlebar follows the input smoothly (faster when letting go, calmer at speed).
        bool returning = Mathf.Abs(steer) < Mathf.Abs(SteerAmount) && steer * SteerAmount >= 0f;
        float rate = (returning ? steerReturnRate : steerRate) * Mathf.Lerp(1f, steerRateAtMaxSpeed, speed01);
        SteerAmount = Mathf.MoveTowards(SteerAmount, steer, rate * dt);

        // Steering angle: smaller at speed, and never more than the tyres can hold (bicycle model: a = v² tanδ / L).
        float steerLimit = Mathf.Lerp(maxSteerAngle, steerAngleAtMaxSpeed, speed01);
        if (absSpeed > 1f)
            steerLimit = Mathf.Min(steerLimit, Mathf.Atan(maxCornerAccel * wheelBase / (speed * speed)) * Mathf.Rad2Deg);
        steerAngle = SteerAmount * steerLimit;
        frontCollider.steerAngle = steerAngle;

        bool frontGrounded = frontCollider.GetGroundHit(out WheelHit frontGround);
        bool rearGrounded = rearCollider.GetGroundHit(out WheelHit rearGround);
        bool touching = frontGrounded || rearGrounded;
        if (touching) lastGroundedTime = Time.time;
        IsGrounded = Time.time - lastGroundedTime < 0.15f;

        // ---- Engine & brakes as wheel torques (settings are in m/s², converted with mass and wheel radius) ----
        float m = rb.mass, r = wheelRadius;
        float motor = 0f, frontBrake = 0f, rearBrakeTorque = 0f;
        IsBraking = false;
        if (throttle > 0.01f)
        {
            if (speed < -directionChangeSpeed) { frontBrake = rearBrakeTorque = brakeDeceleration * throttle * m * r * 0.5f; IsBraking = true; }
            else motor = acceleration * throttle * Mathf.Max(0f, 1f - speed01 * speed01 * speed01) * m * r;
        }
        else if (throttle < -0.01f)
        {
            if (speed > directionChangeSpeed) { frontBrake = rearBrakeTorque = brakeDeceleration * -throttle * m * r * 0.5f; IsBraking = true; }
            else if (-speed < maxReverseSpeedKmh / 3.6f) motor = -reverseAcceleration * -throttle * m * r;
        }
        else
        {
            frontBrake = rearBrakeTorque = rollingDrag * m * r * 0.5f;   // engine braking / rolling resistance
            // Nearly stopped with no key pressed: hold it still instead of creeping.
            if (absSpeed < 0.5f) frontBrake = rearBrakeTorque = brakeDeceleration * m * r;
        }
        if (rearBrake) { rearBrakeTorque += rearBrakeDeceleration * m * r; motor = 0f; IsBraking = true; }
        if (!drive) { frontBrake = rearBrakeTorque = brakeDeceleration * m * r; motor = 0f; }   // parked / held
        if (speed01 >= 1f && motor > 0f) motor = 0f;                                         // top speed

        float groundedWheelTorque = frontGrounded && rearGrounded ? motor * 0.5f : motor;
        frontCollider.motorTorque = frontGrounded ? groundedWheelTorque : 0f;
        rearCollider.motorTorque = rearGrounded ? groundedWheelTorque : 0f;
        frontCollider.brakeTorque = frontBrake;
        rearCollider.brakeTorque = rearBrakeTorque;
        SetGrip(rearCollider, forwardGrip, rearBrake && absSpeed > 2f ? rearBrakeSidewaysGrip : sidewaysGrip);

        // ---- Steering assist: nudge the turn rate towards what the handlebar asks (bicycle model) ----
        if (touching && steeringAssist > 0f)
        {
            float targetYaw = speed / wheelBase * Mathf.Tan(steerAngle * Mathf.Deg2Rad);
            float yawAccel = Mathf.Clamp((targetYaw - rb.angularVelocity.y) * steeringAssist, -30f, 30f);
            // Change the turn rate directly. Don't use AddTorque(..., ForceMode.Acceleration): the body's X/Z rotation
            // is frozen, so its inertia there is 0, and that mode turns it into 0 x infinity = NaN on the next step
            // (this was the "physics produced invalid values" / bouncing-at-spawn bug).
            if (float.IsFinite(yawAccel)) rb.angularVelocity += Vector3.up * (yawAccel * dt);
        }

        // ---- Air drag, downforce, airborne behaviour ----
        Vector3 flat = Vector3.ProjectOnPlane(v, Vector3.up);
        rb.AddForce(-flat * (airDrag * flat.magnitude * m));
        if (touching)
        {
            rb.AddForce(-transform.up * (downforce * absSpeed));
        }
        else
        {
            rb.AddForce(Physics.gravity * (airGravity - 1f), ForceMode.Acceleration);
        }

        if (motor != 0f && Mathf.Abs(throttle) > 0.05f && flat.sqrMagnitude < MaxCurbAssistSpeed * MaxCurbAssistSpeed)
        {
            bool forward = motor > 0f;
            WheelHit ground = forward
                ? (frontGrounded ? frontGround : rearGround)
                : (rearGrounded ? rearGround : frontGround);
            TryAssistCurb(forward, touching, ground.point.y, dt);
        }

        lateralAccel = touching ? speed * rb.angularVelocity.y : 0f;
        float rawAccel = (speed - lastForwardSpeed) / dt;
        lastForwardSpeed = speed;
        LongitudinalAccel = Mathf.Lerp(LongitudinalAccel, rawAccel, 1f - Mathf.Exp(-8f * dt));

        if (drive && input.ResetPressed) ResetOntoWheels();
    }

    private void TryAssistCurb(bool forward, bool touching, float groundHeight, float dt)
    {
        if (!touching)
        {
            // Both wheel rays can briefly lose the road as the chassis rises toward the curb.
            float trailingZ = forward ? rearContactZ : frontContactZ;
            Vector3 probe = transform.TransformPoint(0f, 0.05f, trailingZ);
            if (!IsGrounded ||
                !Physics.Raycast(probe, Vector3.down, out RaycastHit road, MaxCurbHeight + 0.08f,
                    groundLayers, QueryTriggerInteraction.Ignore) ||
                road.collider.attachedRigidbody != null || road.normal.y < 0.85f)
                return;
            groundHeight = road.point.y;
        }

        Vector3 direction = forward
            ? Quaternion.AngleAxis(steerAngle, Vector3.up) * transform.forward
            : -transform.forward;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        Vector3 origin = (forward ? frontCollider : rearCollider).transform.position;
        origin.y = groundHeight + 0.06f;

        float reach = wheelRadius + MaxCurbAssistSpeed * dt;
        if (!Physics.Raycast(origin, direction, out RaycastHit face, reach, groundLayers, QueryTriggerInteraction.Ignore) ||
            face.collider.attachedRigidbody != null || Mathf.Abs(face.normal.y) > 0.25f)
            return;

        // Look just past the face for a walkable top on this same static obstacle.
        Vector3 landing = face.point - face.normal * 0.05f;
        landing.y = groundHeight + MaxCurbHeight + 0.02f;
        if (!Physics.Raycast(landing, Vector3.down, out RaycastHit top, MaxCurbHeight, groundLayers, QueryTriggerInteraction.Ignore) ||
            top.collider != face.collider || top.normal.y < 0.85f || top.point.y - groundHeight > MaxCurbHeight)
            return;

        // Raise the chassis only enough to reach the curb. Wheel torque continues to move it across.
        float remainingRise = top.point.y + 0.02f - rb.position.y;
        if (remainingRise <= 0f)
            return;

        Vector3 velocity = rb.linearVelocity;
        float liftSpeed = Mathf.Min(MaxCurbLiftSpeed, remainingRise / dt);
        velocity.y = Mathf.Max(velocity.y, liftSpeed);
        float approachSpeed = Vector3.Dot(velocity, direction);
        float forwardAssist = Mathf.Clamp(1.5f - approachSpeed, 0f, 8f * dt);
        rb.linearVelocity = velocity + direction * forwardAssist;
    }

    // =====================================================================================
    // Visual rig
    // =====================================================================================

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (IsMounted && riderRoot != null)
        {
            if (riderRoot.parent != leanPivot) riderRoot.SetParent(leanPivot, false);
            riderRoot.SetLocalPositionAndRotation(riderSeatPos, riderSeatRot);
        }
        float speed = ForwardSpeed;
        float absSpeed = Mathf.Abs(speed);
        float moving = Mathf.Clamp01(absSpeed / 1.5f);

        // Foot goes down when the rider stops without throttle.
        bool wantFoot = IsMounted && absSpeed < 1.2f && ThrottleInput < 0.05f;
        FootDown = Mathf.MoveTowards(FootDown, wantFoot ? 1f : 0f, (wantFoot ? 3f : 5f) * dt);

        // Lean: into corners from the sideways acceleration; resting on the foot or the stand when stopped.
        float cornerLean = Mathf.Clamp(Mathf.Atan2(lateralAccel, -Physics.gravity.y) * Mathf.Rad2Deg, -maxLeanAngle, maxLeanAngle);
        float restLean = IsMounted ? FootSide * stoppedLeanAngle * FootDown : FootSide * parkedLeanAngle * standAmount;
        float targetLean = IsGrounded ? Mathf.Lerp(restLean, cornerLean, moving) : lean * 0.98f;
        lean = Mathf.Lerp(lean, targetLean, 1f - Mathf.Exp(-leanSmoothing * dt));

        float targetPitch = Mathf.Clamp(-LongitudinalAccel * pitchPerAccel, -maxPitch, maxPitch);
        pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-5f * dt));

        // Lean about the tyre contact line, pitch about the rear contact (accelerating) or front contact (braking).
        Quaternion rot = Quaternion.AngleAxis(-lean, Vector3.forward) * Quaternion.AngleAxis(pitch, Vector3.right);
        Vector3 pivotPoint = new Vector3(0f, 0f, pitch < 0f ? rearContactZ : frontContactZ);
        float bump = roadBumpHeight * moving * (Mathf.PerlinNoise(Time.time * 6f, 0.3f) - 0.5f) * 2f;
        if (!float.IsFinite(lean) || !float.IsFinite(pitch)) { lean = 0f; pitch = 0f; rot = Quaternion.identity; }
        leanPivot.localRotation = rot;
        leanPivot.localPosition = pivotPoint - rot * pivotPoint + Vector3.up * bump;

        steerPivot.localRotation = Quaternion.AngleAxis(steerAngle, steerAxis);

        // Wheel meshes spin as fast as the physics wheels (the rear stops when it locks under the rear brake).
        if (float.IsFinite(frontCollider.rpm)) frontSpin = Mathf.Repeat(frontSpin + frontCollider.rpm * 6f * dt, 360f);
        if (float.IsFinite(rearCollider.rpm)) rearSpin = Mathf.Repeat(rearSpin + rearCollider.rpm * 6f * dt, 360f);
        frontWheel.localRotation = Quaternion.AngleAxis(frontSpin, Vector3.right);
        rearWheel.localRotation = Quaternion.AngleAxis(rearSpin, Vector3.right);

        if (standPivot != null)
        {
            standAmount = Mathf.MoveTowards(standAmount, IsMounted ? 0f : 1f, 3f * dt);
            standPivot.localRotation = Quaternion.AngleAxis(standFoldAngle * (1f - standAmount), standAxis);
        }

        headlight.enabled = headlightOn && IsMounted;
        bool brakeOn = IsMounted && (IsBraking || (ThrottleInput < -0.01f && speed > 0.3f));
        brakeLight.enabled = IsMounted && (brakeOn || headlightOn);
        brakeLight.intensity = brakeOn ? 2.5f : 0.6f;

        if (engineSource != null)
        {
            float speed01 = Mathf.Clamp01(absSpeed / MaxSpeed);
            float throttle = Mathf.Abs(ThrottleInput);
            float targetVol = IsMounted ? engineVolume * (0.45f + 0.35f * throttle + 0.2f * speed01) : 0f;
            engineSource.volume = Mathf.MoveTowards(engineSource.volume, targetVol, dt * 1.5f);
            engineSource.pitch = Mathf.Lerp(engineSource.pitch, 0.8f + 1.3f * speed01 + 0.25f * throttle, 1f - Mathf.Exp(-4f * dt));
        }
    }

    // =====================================================================================
    // Getting on / off
    // =====================================================================================

    private void TryGetOff()
    {
        if (Mathf.Abs(ForwardSpeed) > maxDismountSpeed) { FlashHint("Slow down to get off"); return; }
        GetOff(force: false);
    }

    private void GetOff(bool force)
    {
        // Stand next to the scooter on the foot side, facing the same way.
        Vector3 side = transform.right * FootSide;
        Vector3 spot = transform.position + side * 0.85f + transform.forward * 0.1f;
        if (!force && Physics.CheckCapsule(spot + Vector3.up * 0.4f, spot + Vector3.up * 1.5f, 0.28f,
                                           groundLayers, QueryTriggerInteraction.Ignore))
        {
            spot = transform.position - side * 0.85f;   // blocked: try the other side
        }
        if (Physics.Raycast(spot + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, groundLayers, QueryTriggerInteraction.Ignore))
            spot.y = hit.point.y;

        IsMounted = false;
        SteerAmount = 0f;
        if (riderPose != null) { riderPose.DismountScooter(); riderPose.enabled = false; }
        if (riderMotion != null) riderMotion.enabled = false;

        riderRoot.SetParent(null, true);
        riderRoot.SetPositionAndRotation(spot, Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up)));
        EnableOnFoot(true);
        SetCameraFollowsScooter(false);
    }

    private void TryGetOn()
    {
        Vector3 offset = riderRoot.position - transform.position;
        offset.y = 0f;
        if (offset.magnitude > mountRange) return;

        EnableOnFoot(false);
        riderRoot.SetParent(leanPivot, false);
        riderRoot.localPosition = riderSeatPos;
        riderRoot.localRotation = riderSeatRot;
        foreach (var (t, pos, rot) in riderSeatedPose)
            if (t != null) { t.localPosition = pos; t.localRotation = rot; }

        if (riderPose != null) { riderPose.enabled = true; riderPose.MountScooter(); }
        if (riderMotion != null) riderMotion.enabled = true;
        IsMounted = true;
        waitForThrottleRelease = true;
        SetCameraFollowsScooter(true);
    }

    /// Switches the courier between the walking character and the seated rider.
    /// Walking: CapsuleCollider + Rigidbody + PlayerController + CharacterAnimator, added fresh every time.
    /// Riding: all of those are removed. A Rigidbody inside the scooter would keep its own physics position
    /// (and interpolation) and stay behind on the road while the scooter drives away.
    private void EnableOnFoot(bool on)
    {
        GameObject go = riderRoot.gameObject;
        var anim = riderRoot.GetComponentInChildren<Animation>(true);

        if (on)
        {
            // Add while inactive so every Awake runs together once all the components exist
            // (CharacterAnimator needs the PlayerController, PlayerController reads the capsule).
            bool wasActive = go.activeSelf;
            go.SetActive(false);
            if (!go.TryGetComponent(out CapsuleCollider capsule)) capsule = go.AddComponent<CapsuleCollider>();
            capsule.enabled = true;
            capsule.height = 1.8f;
            capsule.radius = 0.3f;
            capsule.center = new Vector3(0f, 0.9f, 0f);
            if (!go.TryGetComponent(out Rigidbody body)) body = go.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.detectCollisions = true;
            body.mass = 70f;
            if (anim != null) anim.enabled = true;   // CharacterAnimator (re)starts Idle / Walk / Run in its Awake
            if (!go.TryGetComponent(out PlayerController _)) go.AddComponent<PlayerController>();   // + CharacterAnimator
            go.SetActive(wasActive);
            return;
        }

        // Riding: remove the walking body completely (dependents first, so RequireComponent allows it).
        if (go.TryGetComponent(out PlayerController walker)) DestroyImmediate(walker);
        if (go.TryGetComponent(out CharacterAnimator animator)) DestroyImmediate(animator);
        if (go.TryGetComponent(out Rigidbody rbody)) DestroyImmediate(rbody);
        if (go.TryGetComponent(out CapsuleCollider col)) DestroyImmediate(col);
        if (anim != null) { anim.Stop(); anim.enabled = false; }   // the seated pose takes over
    }

    private void SetCameraFollowsScooter(bool scooter)
    {
        var cam = Camera.main;
        var follow = cam != null ? cam.GetComponent<VehicleCameraFollow>() : null;
        if (follow == null) return;
        follow.enabled = scooter;
        if (scooter) follow.Target = transform;   // snaps the camera back behind the scooter
    }

    // =====================================================================================
    // PlayerVehicle API
    // =====================================================================================

    public override void ResetOntoWheels()
    {
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
        Teleport(transform.position + Vector3.up * 0.6f, Quaternion.LookRotation(flatForward));
    }

    public override void Teleport(Vector3 position, Quaternion rotation)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = position;
        rb.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        lastForwardSpeed = 0f;
        LongitudinalAccel = 0f;
    }

    // =====================================================================================
    // Hints
    // =====================================================================================

    private float flashUntil;
    private string flashText;

    private void FlashHint(string text) { flashText = text; flashUntil = Time.time + 1.5f; }

    private void UpdateHint()
    {
        hint = null;
        if (!showHints || riderRoot == null || !ControlsEnabled) return;
        if (Time.time < flashUntil) { hint = flashText; return; }
        if (IsMounted)
        {
            if (Mathf.Abs(ForwardSpeed) < 0.5f) hint = "[E] Get off    [W / Shift] Ride    [A / D] Steer    [H] Horn    [L] Light";
        }
        else
        {
            Vector3 d = riderRoot.position - transform.position;
            d.y = 0f;
            if (d.magnitude <= mountRange) hint = "[E] Ride scooter";
        }
    }

    private GUIStyle hintStyle;

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(hint)) return;
        hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        var size = hintStyle.CalcSize(new GUIContent(hint)) + new Vector2(24f, 12f);
        GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height - size.y - 40f, size.x, size.y), hint, hintStyle);
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);


    /// The spot at 'around' with both tyres standing on top of whatever is below (road, kerb, floor tile),
    /// a few centimetres up so nothing starts overlapping.
    private Vector3 SafeSpot(Vector3 around)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        float ground = float.NegativeInfinity;
        foreach (float z in new[] { frontContactZ, rearContactZ, (frontContactZ + rearContactZ) * 0.5f })
        {
            Vector3 top = around + fwd * z + Vector3.up * 2f;
            foreach (var hit in Physics.RaycastAll(top, Vector3.down, 6f, groundLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.rigidbody == rb || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.rigidbody != null && !hit.rigidbody.isKinematic) continue;   // not on top of something loose
                ground = Mathf.Max(ground, hit.point.y);
            }
        }
        if (float.IsNegativeInfinity(ground)) return around;
        return new Vector3(around.x, ground + 0.03f, around.z);
    }

    private static Transform NewPivot(string name, Transform parent, Vector3 localPos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        return t;
    }

    private static List<Renderer> Renderers(List<Transform> parts)
    {
        var list = new List<Renderer>();
        foreach (var p in parts) list.AddRange(p.GetComponentsInChildren<Renderer>());
        return list;
    }

    /// The tyre meshes of a wheel group (they define the wheel size); the whole group if no tyre is named.
    private static List<Renderer> Tyre(List<Transform> wheelParts)
    {
        var tyre = Renderers(wheelParts.FindAll(t => t.name.ToLowerInvariant().Contains("tyre") ||
                                                     t.name.ToLowerInvariant().Contains("tire")));
        return tyre.Count > 0 ? tyre : Renderers(wheelParts);
    }

    private static Vector3 WorldCenter(IEnumerable<Renderer> renderers, Vector3 fallback)
    {
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any ? b.center : fallback;
    }

    /// Bounds of the renderers in the local space of <paramref name="space"/>.
    private static Bounds LocalBounds(IEnumerable<Renderer> renderers, Transform space)
    {
        bool any = false;
        Bounds result = default;
        foreach (var r in renderers)
        {
            Bounds lb = r is MeshRenderer && r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null
                ? mf.sharedMesh.bounds : new Bounds(r.transform.InverseTransformPoint(r.bounds.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, ((i & 4) >> 1) - 1));
                Vector3 p = space.InverseTransformPoint(r.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    private static Transform FindChildContaining(Transform root, string text)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name.ToLowerInvariant().Contains(text)) return t;
        return null;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform c in parent)
        {
            var f = FindDeep(c, name);
            if (f != null) return f;
        }
        return null;
    }

    private void OnValidate()
    {
        steerAngleAtMaxSpeed = Mathf.Min(steerAngleAtMaxSpeed, maxSteerAngle);
    }
}
