using UnityEngine;

/// <summary>
/// Makes the courier ride the scooter instead of sitting on it like a statue.
/// Runs after <see cref="ScooterRiderPose"/> (the seated base pose) and layers on top of it:
///  - hands stay on the grips while the handlebar turns (two-bone IK), right hand twists the throttle
///  - upper body is pushed back when accelerating, forward when braking, tucks in at speed
///  - leans a little further into corners than the scooter, head looks into the turn and stays level
///  - puts a foot down on the road when stopping, lifts it back onto the footboard when riding off
/// Added and bound automatically by <see cref="ScooterController"/>.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class ScooterRiderMotion : MonoBehaviour
{
    [Header("Hands")]
    [SerializeField] private bool handsOnGrips = true;
    [Tooltip("Right-hand twist at full throttle (degrees).")]
    [SerializeField] private float throttleTwist = 20f;

    [Header("Upper Body")]
    [Tooltip("Torso lean per m/s² of acceleration (pushed back when speeding up, forward when braking).")]
    [SerializeField] private float accelerationLean = 1.1f;
    [SerializeField] private float maxAccelerationLean = 9f;
    [Tooltip("Extra forward tuck at top speed.")]
    [SerializeField] private float speedTuck = 7f;
    [Tooltip("How much further than the scooter the rider leans into a corner (fraction of the scooter lean).")]
    [SerializeField, Range(0f, 1f)] private float cornerExtraLean = 0.3f;
    [SerializeField] private float headTurn = 25f;
    [Tooltip("How much the head counters the lean to keep the eyes level (0..1).")]
    [SerializeField, Range(0f, 1f)] private float headLevelling = 0.6f;
    [SerializeField] private float smoothing = 8f;

    [Header("Foot Down")]
    [Tooltip("Ankle height above the road when the foot is down.")]
    [SerializeField] private float ankleHeight = 0.09f;
    [SerializeField] private float footForward = 0.12f;
    [SerializeField] private float footOutward = 0.14f;

    private ScooterController scooter;
    private Transform hips, spine, spine1, neck, head;
    private Transform lArm, lForeArm, lHand, lPalm, rArm, rForeArm, rHand, rPalm;
    private Transform lUpLeg, lLeg, lFoot, rUpLeg, rLeg, rFoot;

    // Bones the base pose doesn't set every frame: restored before adding motion so offsets never pile up.
    private Transform[] unposed;
    private Quaternion[] unposedRot;

    private float torsoPitch, torsoRoll, headYaw;

    public void Bind(ScooterController owner, Transform rider)
    {
        scooter = owner;
        hips = Find(rider, "Hips");
        spine = Find(rider, "Spine");
        spine1 = Find(rider, "Spine1");
        neck = Find(rider, "Neck");
        head = Find(rider, "Head");
        lArm = Find(rider, "LeftArm"); lForeArm = Find(rider, "LeftForeArm"); lHand = Find(rider, "LeftHand");
        rArm = Find(rider, "RightArm"); rForeArm = Find(rider, "RightForeArm"); rHand = Find(rider, "RightHand");
        lPalm = lHand != null ? Find(lHand, "attach-lefthand") : null;
        rPalm = rHand != null ? Find(rHand, "attach-righthand") : null;
        lUpLeg = Find(rider, "LeftUpLeg"); lLeg = Find(rider, "LeftLeg"); lFoot = Find(rider, "LeftFoot");
        rUpLeg = Find(rider, "RightUpLeg"); rLeg = Find(rider, "RightLeg"); rFoot = Find(rider, "RightFoot");

        unposed = System.Array.FindAll(new[] { spine1, neck, lHand, rHand }, t => t != null);
        unposedRot = new Quaternion[unposed.Length];
        for (int i = 0; i < unposed.Length; i++) unposedRot[i] = unposed[i].localRotation;
    }

    private void LateUpdate()
    {
        if (scooter == null || !scooter.IsMounted || hips == null) return;
        if (!float.IsFinite(scooter.LeanAngle) || !float.IsFinite(scooter.LongitudinalAccel)) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        for (int i = 0; i < unposed.Length; i++) unposed[i].localRotation = unposedRot[i];

        Transform bike = scooter.LeanPivot;
        Vector3 fwd = bike.forward, right = bike.right, up = bike.up;
        float k = 1f - Mathf.Exp(-smoothing * dt);
        float speed01 = Mathf.Clamp01(Mathf.Abs(scooter.ForwardSpeed) / Mathf.Max(scooter.MaxSpeed, 0.1f));

        // ---- Torso: inertia + tuck + corner lean ----
        float targetPitch = Mathf.Clamp(-scooter.LongitudinalAccel * accelerationLean, -maxAccelerationLean, maxAccelerationLean)
                            + speedTuck * speed01 + 12f * scooter.WheelieAmount;
        torsoPitch = Mathf.Lerp(torsoPitch, targetPitch, k);
        torsoRoll = Mathf.Lerp(torsoRoll, scooter.LeanAngle * cornerExtraLean, k);
        Rotate(spine, torsoPitch * 0.6f, right);
        Rotate(spine1, torsoPitch * 0.4f, right);
        Rotate(spine, -torsoRoll * 0.6f, fwd);
        Rotate(spine1, -torsoRoll * 0.4f, fwd);

        // ---- Head: look into the turn, keep the eyes roughly level ----
        headYaw = Mathf.Lerp(headYaw, scooter.SteerAmount * headTurn, k);
        Transform look = head != null ? head : neck;
        Rotate(look, headYaw, up);
        Rotate(look, 8f * scooter.WheelieAmount, right);
        Rotate(look, (scooter.LeanAngle + torsoRoll) * headLevelling, fwd);

        // ---- Hands on the grips ----
        if (handsOnGrips)
        {
            ReachGrip(lArm, lForeArm, lHand, lPalm, scooter.LeftGrip, -right, up);
            ReachGrip(rArm, rForeArm, rHand, rPalm, scooter.RightGrip, right, up);
            Rotate(rHand, -Mathf.Max(0f, scooter.ThrottleInput) * throttleTwist, right);
        }

        // ---- Foot down when stopped ----
        float w = scooter.FootDown;
        if (w > 0.001f)
        {
            bool rightSide = scooter.FootSide > 0f;
            Transform upLeg = rightSide ? rUpLeg : lUpLeg, leg = rightSide ? rLeg : lLeg, foot = rightSide ? rFoot : lFoot;
            if (upLeg != null && leg != null && foot != null)
            {
                Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
                Vector3 flatRight = Vector3.Cross(Vector3.up, flatFwd);
                Vector3 target = upLeg.position + flatRight * (scooter.FootSide * footOutward) + flatFwd * footForward;
                target.y = scooter.transform.position.y + ankleHeight;
                Vector3 kneeHint = leg.position + flatFwd * 0.5f + flatRight * (scooter.FootSide * 0.1f);
                SolveTwoBone(upLeg, leg, foot, target, kneeHint, w);

                // Sole flat on the road.
                Quaternion flat = Quaternion.FromToRotation(foot.up, Vector3.up) * foot.rotation;
                foot.rotation = Quaternion.Slerp(foot.rotation, flat, w);
            }
        }
    }

    private static void ReachGrip(Transform arm, Transform foreArm, Transform hand, Transform palm, Transform grip,
                                  Vector3 outward, Vector3 up)
    {
        if (arm == null || foreArm == null || hand == null || grip == null) return;
        // Bring the palm (not the wrist) to the grip; elbows bend out and down.
        Vector3 palmOffset = palm != null ? palm.position - hand.position : Vector3.zero;
        Vector3 hint = foreArm.position + outward * 0.15f - up * 0.1f;
        SolveTwoBone(arm, foreArm, hand, grip.position - palmOffset, hint, 1f);
    }

    /// Analytic two-bone IK (shoulder-elbow-wrist or hip-knee-ankle). Works with any bone axes.
    public static void SolveTwoBone(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 hint, float weight)
    {
        Vector3 a = upper.position, b = lower.position, c = end.position;
        if (!Finite(a) || !Finite(b) || !Finite(c) || !Finite(target) || !Finite(hint)) return;
        float lab = Vector3.Distance(a, b), lbc = Vector3.Distance(b, c);
        if (lab < 1e-4f || lbc < 1e-4f) return;

        Vector3 toTarget = target - a;
        float dist = Mathf.Clamp(toTarget.magnitude, 1e-3f, (lab + lbc) * 0.999f);
        Vector3 dir = toTarget.normalized;

        Vector3 pole = Vector3.ProjectOnPlane(hint - a, dir);
        if (pole.sqrMagnitude < 1e-6f) pole = Vector3.ProjectOnPlane(b - a, dir);
        if (pole.sqrMagnitude < 1e-6f) return;
        pole.Normalize();

        float cosA = Mathf.Clamp((lab * lab + dist * dist - lbc * lbc) / (2f * lab * dist), -1f, 1f);
        Vector3 joint = a + dir * (lab * cosA) + pole * (lab * Mathf.Sqrt(1f - cosA * cosA));

        Quaternion upperStart = upper.rotation, lowerStart = lower.rotation;
        upper.rotation = Quaternion.FromToRotation(b - a, joint - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, (a + dir * dist) - lower.position) * lower.rotation;

        if (weight < 1f)
        {
            Quaternion upperIk = upper.rotation, lowerIk = lower.rotation;
            upper.rotation = Quaternion.Slerp(upperStart, upperIk, weight);
            lower.rotation = Quaternion.Slerp(lowerStart, lowerIk, weight);
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    private static void Rotate(Transform t, float angle, Vector3 axis)
    {
        if (t != null && Mathf.Abs(angle) > 0.001f) t.rotation = Quaternion.AngleAxis(angle, axis) * t.rotation;
    }

    private static Transform Find(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var found = Find(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
