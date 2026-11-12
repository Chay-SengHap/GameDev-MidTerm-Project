using UnityEngine;

/// <summary>
/// Makes the character move like a person.
///  - Blends the model's Idle / Walk / Run clips by actual speed, with playback synced to ground speed
///    (no foot sliding) and Walk/Run kept in step so the legs don't cross while blending.
///  - Adds procedural body motion on top of the clips (the model has no clips for these):
///      * crouch before a jump, legs tucked and arms up in the air, legs reaching for the ground when falling
///      * landing squash scaled by fall speed (heavy landings dip deeper)
///      * crouching / crouch-walking
///      * leaning forward when running / accelerating, banking into turns
///      * head turning toward where the camera looks
/// Bones are found by name (Hips, Spine, LeftUpLeg ...) so it works with this courier rig.
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    [Header("Clips (from the model)")]
    [SerializeField] private string idleClip = "Idle";
    [SerializeField] private string walkClip = "Walk";
    [SerializeField] private string runClip = "Run";
    [SerializeField] private float blendTime = 0.2f;
    [SerializeField] private float walkAnimSpeed = 1.6f;   // ground speed (m/s) the Walk clip was made for
    [SerializeField] private float runAnimSpeed = 4.5f;    // ground speed (m/s) the Run clip was made for
    [SerializeField] private bool syncWalkAndRun = true;

    [Header("Body Lean")]
    [SerializeField] private float walkForwardLean = 2f;
    [SerializeField] private float runForwardLean = 8f;
    [SerializeField] private float accelerationLean = 1.2f; // degrees per m/s² of acceleration
    [SerializeField] private float turnBank = 0.03f;        // degrees per (degree/second) of turning
    [SerializeField] private float maxLean = 15f;
    [SerializeField] private float leanSmoothing = 8f;

    [Header("Jump / Air / Landing")]
    [SerializeField] private float jumpCrouchDepth = 0.15f;
    [SerializeField] private float airLegTuck = 35f;
    [SerializeField] private float airArmRaise = 35f;
    [SerializeField] private float landSquashPerSpeed = 0.035f;
    [SerializeField] private float maxLandSquash = 0.35f;
    [SerializeField] private float landRecovery = 7f;

    [Header("Crouch")]
    [SerializeField] private float crouchDepth = 0.45f;
    [SerializeField] private float crouchSpineLean = 18f;
    [SerializeField] private float crouchTransitionSpeed = 5f;

    [Header("Head Look")]
    [SerializeField] private bool headFollowsCamera = true;
    [SerializeField] private float maxHeadTurn = 60f;

    private PlayerController controller;
    private Animation anim;
    private AnimationState idleState, walkState, runState;
    private float idleW = 1f, walkW, runW;

    private Bone hips, spine, spine1, spine2, neck, head, leftArm, rightArm;
    private Bone leftUpLeg, leftLeg, leftFoot, rightUpLeg, rightLeg, rightFoot;
    private Bone[] allBones;
    private float legLength = 0.9f;

    private float smoothSpeed;
    private float lastYaw, turnRate;
    private float lastForwardSpeed, forwardAccel;
    private float lean, bank;
    private float airAmount, airTimer;
    private float landSquash;
    private float crouchAmount;
    private float headAngle;

    // Remembers the animated pose so procedural offsets never accumulate on bones the clips don't key.
    private class Bone
    {
        public Transform t;
        private Quaternion baseRot, appliedRot;
        private Vector3 basePos, appliedPos;
        private bool hasApplied;

        public Bone(Transform t) { this.t = t; }

        public void Begin()
        {
            if (t == null) return;
            if (hasApplied && t.localRotation == appliedRot) t.localRotation = baseRot; // not re-animated this frame
            if (hasApplied && t.localPosition == appliedPos) t.localPosition = basePos;
            baseRot = t.localRotation;
            basePos = t.localPosition;
        }

        public void End()
        {
            if (t == null) return;
            appliedRot = t.localRotation;
            appliedPos = t.localPosition;
            hasApplied = true;
        }

        public void Rotate(float angle, Vector3 worldAxis)
        {
            if (t != null && Mathf.Abs(angle) > 0.001f)
                t.rotation = Quaternion.AngleAxis(angle, worldAxis) * t.rotation;
        }
    }

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        if (controller == null)
        {
            Debug.LogWarning("CharacterAnimator needs a PlayerController on the same object.", this);
            enabled = false;
            return;
        }
        controller.Landed += OnLanded;
        lastYaw = transform.eulerAngles.y;

        SetupClips();
        SetupBones();
    }

    private void OnDestroy()
    {
        if (controller != null) controller.Landed -= OnLanded;
    }

    private void OnLanded(float impactSpeed)
    {
        landSquash = Mathf.Min(maxLandSquash, landSquash + impactSpeed * landSquashPerSpeed);
    }

    // ---------------- Setup ----------------
    private void SetupClips()
    {
        // The glTF importer may put the Animation on this object, a child, or the model's root parent.
        anim = GetComponentInChildren<Animation>();
        if (anim == null) anim = GetComponentInParent<Animation>();
        if (anim == null)
        {
            Debug.LogWarning("CharacterAnimator: no Animation component found, so Idle/Walk/Run won't play. " +
                             "Select the .glb and make sure its Animation import setting is 'Legacy'.", this);
            return;
        }

        anim.playAutomatically = false;
        anim.cullingType = AnimationCullingType.AlwaysAnimate;
        anim.Stop();

        foreach (AnimationState state in anim)
        {
            string n = state.name.ToLowerInvariant();
            if (idleState == null && n.Contains(idleClip.ToLowerInvariant())) idleState = state;
            else if (walkState == null && n.Contains(walkClip.ToLowerInvariant())) walkState = state;
            else if (runState == null && n.Contains(runClip.ToLowerInvariant())) runState = state;
        }

        foreach (var s in new[] { idleState, walkState, runState })
        {
            if (s == null) continue;
            s.wrapMode = WrapMode.Loop;
            s.layer = 0;
            s.weight = 0f;
            s.enabled = true;     // all three run continuously; we only change their weights
        }
        if (idleState != null) idleState.weight = 1f;

        if (idleState == null) Debug.LogWarning($"CharacterAnimator: no '{idleClip}' clip found.", this);
        if (walkState == null) Debug.LogWarning($"CharacterAnimator: no '{walkClip}' clip found.", this);
        if (runState == null) Debug.LogWarning($"CharacterAnimator: no '{runClip}' clip found.", this);
    }

    private void SetupBones()
    {
        Transform root = anim != null ? anim.transform : transform;
        hips = new Bone(Find(root, "Hips"));
        spine = new Bone(Find(root, "Spine"));
        spine1 = new Bone(Find(root, "Spine1"));
        spine2 = new Bone(Find(root, "Spine2"));
        neck = new Bone(Find(root, "Neck"));
        head = new Bone(Find(root, "Head"));
        leftArm = new Bone(Find(root, "LeftArm"));
        rightArm = new Bone(Find(root, "RightArm"));
        leftUpLeg = new Bone(Find(root, "LeftUpLeg"));
        leftLeg = new Bone(Find(root, "LeftLeg"));
        leftFoot = new Bone(Find(root, "LeftFoot"));
        rightUpLeg = new Bone(Find(root, "RightUpLeg"));
        rightLeg = new Bone(Find(root, "RightLeg"));
        rightFoot = new Bone(Find(root, "RightFoot"));

        allBones = new[] { hips, spine, spine1, spine2, neck, head, leftArm, rightArm,
                           leftUpLeg, leftLeg, leftFoot, rightUpLeg, rightLeg, rightFoot };

        if (leftUpLeg.t != null && leftLeg.t != null && leftFoot.t != null)
            legLength = Vector3.Distance(leftUpLeg.t.position, leftLeg.t.position)
                      + Vector3.Distance(leftLeg.t.position, leftFoot.t.position);

        if (hips.t == null)
            Debug.LogWarning("CharacterAnimator: couldn't find the 'Hips' bone; procedural body motion is disabled.", this);
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

    // ---------------- Clip blending (before the animation is evaluated) ----------------
    private void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 v = controller.Velocity;
        float groundSpeed = new Vector2(v.x, v.z).magnitude;
        smoothSpeed = Mathf.Lerp(smoothSpeed, groundSpeed, 1f - Mathf.Exp(-10f * dt));

        // Turn rate and forward acceleration for leaning.
        float yaw = transform.eulerAngles.y;
        float rawTurn = Mathf.DeltaAngle(lastYaw, yaw) / dt;
        lastYaw = yaw;
        turnRate = Mathf.Lerp(turnRate, rawTurn, 1f - Mathf.Exp(-10f * dt));

        float forwardSpeed = Vector3.Dot(v, transform.forward);
        float rawAccel = (forwardSpeed - lastForwardSpeed) / dt;
        lastForwardSpeed = forwardSpeed;
        forwardAccel = Mathf.Lerp(forwardAccel, rawAccel, 1f - Mathf.Exp(-6f * dt));

        // Air state (small bumps don't count as being airborne).
        airTimer = controller.IsGrounded ? 0f : airTimer + dt;
        bool inAir = airTimer > 0.1f || (!controller.IsGrounded && v.y > 1f);
        airAmount = Mathf.MoveTowards(airAmount, inAir ? 1f : 0f, dt * (inAir ? 6f : 12f));

        crouchAmount = Mathf.MoveTowards(crouchAmount, controller.IsCrouching ? 1f : 0f, dt * crouchTransitionSpeed);
        landSquash = Mathf.Lerp(landSquash, 0f, 1f - Mathf.Exp(-landRecovery * dt));

        UpdateClipWeights(dt);
    }

    private void UpdateClipWeights(float dt)
    {
        if (anim == null) return;

        float s = smoothSpeed;
        float runT = Mathf.InverseLerp(controller.WalkSpeed, controller.RunSpeed, s);
        float moving = Mathf.Clamp01(s / 0.8f) * (1f - airAmount);   // in the air: no stepping legs

        float tIdle = 1f - moving;
        float tWalk = moving * (1f - runT);
        float tRun = moving * runT;
        if (runState == null) { tWalk += tRun; tRun = 0f; }
        if (walkState == null) { tRun += tWalk; tWalk = 0f; }

        float step = dt / Mathf.Max(blendTime, 0.01f);
        idleW = Mathf.MoveTowards(idleW, tIdle, step);
        walkW = Mathf.MoveTowards(walkW, tWalk, step);
        runW = Mathf.MoveTowards(runW, tRun, step);
        float sum = idleW + walkW + runW;
        if (sum > 0.0001f) { idleW /= sum; walkW /= sum; runW /= sum; }

        if (idleState != null) idleState.weight = idleW;
        if (walkState != null) walkState.weight = walkW;
        if (runState != null) runState.weight = runW;

        // Playback speed follows ground speed so feet don't slide.
        float sp = Mathf.Max(s, 0.5f);
        float walkScale = Mathf.Clamp(sp / walkAnimSpeed, 0.6f, 2f);
        float runScale = Mathf.Clamp(sp / runAnimSpeed, 0.6f, 2f);

        if (syncWalkAndRun && walkState != null && runState != null)
        {
            // Same number of steps per second for both clips, so their legs stay in phase while blending.
            float walkLen = Mathf.Max(walkState.length, 0.01f);
            float runLen = Mathf.Max(runState.length, 0.01f);
            float cycleRate = Mathf.Lerp(walkScale / walkLen, runScale / runLen, runT);
            walkState.speed = cycleRate * walkLen;
            runState.speed = cycleRate * runLen;
            if (walkW >= runW) runState.normalizedTime = walkState.normalizedTime;
            else walkState.normalizedTime = runState.normalizedTime;
        }
        else
        {
            if (walkState != null) walkState.speed = walkScale;
            if (runState != null) runState.speed = runScale;
        }
    }

    // ---------------- Procedural body motion (after the animation is evaluated) ----------------
    private void LateUpdate()
    {
        if (hips == null || hips.t == null) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        foreach (var b in allBones) b.Begin();

        Vector3 up = Vector3.up;
        Vector3 right = transform.right;
        Vector3 fwd = transform.forward;
        Vector3 v = controller.Velocity;
        float speed01 = Mathf.Clamp01(smoothSpeed / Mathf.Max(controller.WalkSpeed, 0.1f));
        float windup = controller.JumpWindup01;

        // --- Lean forward when moving/accelerating, bank into turns ---
        float runAmount = Mathf.InverseLerp(controller.WalkSpeed, controller.RunSpeed, smoothSpeed);
        float targetLean = (1f - airAmount) * (speed01 * walkForwardLean + runAmount * (runForwardLean - walkForwardLean)
                                               + forwardAccel * accelerationLean);
        float targetBank = -turnRate * turnBank * speed01 * (1f - airAmount);
        lean = Mathf.Lerp(lean, Mathf.Clamp(targetLean, -maxLean, maxLean), 1f - Mathf.Exp(-leanSmoothing * dt));
        bank = Mathf.Lerp(bank, Mathf.Clamp(targetBank, -maxLean, maxLean), 1f - Mathf.Exp(-leanSmoothing * dt));

        // --- How far the hips drop (crouch, jump wind-up, landing) and how much the legs tuck (air) ---
        float hipDrop = crouchAmount * crouchDepth + windup * jumpCrouchDepth + landSquash;
        hipDrop = Mathf.Min(hipDrop, legLength * 0.6f);

        float fallSpeed = Mathf.Max(0f, -v.y);
        float tuck = airAmount * airLegTuck * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(fallSpeed / 6f)); // reach down when falling

        // Spine leans forward more when crouched/squashed, to keep balance.
        float squash01 = maxLandSquash > 0f ? landSquash / maxLandSquash : 0f;
        float spineLean = lean + crouchAmount * crouchSpineLean + squash01 * 12f + windup * 10f + airAmount * 5f;
        spine.Rotate(spineLean * 0.4f, right);
        spine1.Rotate(spineLean * 0.3f, right);
        spine2.Rotate(spineLean * 0.3f, right);
        spine.Rotate(bank * 0.5f, fwd);
        spine1.Rotate(bank * 0.5f, fwd);

        // --- Legs: bend knees so the feet stay on the ground while the hips drop ---
        float bendFromDrop = Mathf.Acos(Mathf.Clamp(1f - hipDrop / Mathf.Max(legLength, 0.1f), -1f, 1f)) * Mathf.Rad2Deg;
        float thigh = bendFromDrop + tuck;
        BendLeg(leftUpLeg, leftLeg, leftFoot, thigh, right);
        BendLeg(rightUpLeg, rightLeg, rightFoot, thigh, right);
        hips.t.position -= up * hipDrop;

        // --- Arms rise for balance in the air and on hard landings ---
        float armRaise = airAmount * airArmRaise * (1f + 0.3f * Mathf.Clamp01(fallSpeed / 6f)) + squash01 * 20f;
        leftArm.Rotate(-armRaise, fwd);
        rightArm.Rotate(armRaise, fwd);

        // --- Head turns toward where the camera is looking ---
        float targetHead = 0f;
        Transform cam = controller.CameraTransform;
        if (headFollowsCamera && cam != null)
        {
            Vector3 camFwd = Vector3.ProjectOnPlane(cam.forward, up);
            if (camFwd.sqrMagnitude > 0.001f)
            {
                float a = Vector3.SignedAngle(fwd, camFwd, up);
                if (Mathf.Abs(a) < 120f) targetHead = Mathf.Clamp(a, -maxHeadTurn, maxHeadTurn);
            }
        }
        headAngle = Mathf.Lerp(headAngle, targetHead, 1f - Mathf.Exp(-5f * dt));
        neck.Rotate(headAngle * 0.4f, up);
        head.Rotate(headAngle * 0.6f, up);

        foreach (var b in allBones) b.End();
    }

    // Thigh swings forward, shin swings back twice as much, foot is counter-rotated to stay flat.
    private static void BendLeg(Bone upLeg, Bone leg, Bone foot, float angle, Vector3 right)
    {
        if (angle < 0.01f) return;
        upLeg.Rotate(-angle, right);
        leg.Rotate(2f * angle, right);
        foot.Rotate(-angle, right);
    }
}
