using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Physics-based (Rigidbody) third-person character controller.
///   WASD          - move (relative to the camera)
///   Left Shift    - run
///   Space         - jump (short crouch wind-up, coyote time and jump buffering for a natural feel)
///   C / Left Ctrl - crouch (C toggles, Ctrl holds)
///   Mouse         - orbit the camera,  Esc - free the cursor
/// Animation (clips + procedural body motion) is handled by <see cref="CharacterAnimator"/>.
/// </summary>
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(CharacterAnimator))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 2.2f;
    [SerializeField] private float runSpeed = 5.5f;
    [SerializeField] private float crouchSpeed = 1.4f;
    [SerializeField] private float acceleration = 18f;    // how fast we reach target speed on the ground
    [SerializeField] private float airControl = 0.3f;     // fraction of acceleration available in the air
    [SerializeField] private float turnSpeed = 540f;      // degrees per second

    [Header("Jump & Gravity")]
    [SerializeField] private float jumpHeight = 1.1f;
    [SerializeField] private float jumpWindup = 0.08f;    // short crouch before take-off
    [SerializeField] private float coyoteTime = 0.12f;    // can still jump shortly after walking off a ledge
    [SerializeField] private float jumpBuffer = 0.15f;    // a jump pressed just before landing still counts
    [SerializeField] private float extraGravity = 1.5f;   // >1 makes falling feel less floaty
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundCheckDistance = 0.15f;

    [Header("Landing")]
    [SerializeField] private float hardLandingSpeed = 8f;     // fall speed that counts as a heavy landing
    [SerializeField] private float hardLandingRecovery = 0.3f; // seconds of slowed movement after one

    [Header("Crouch")]
    [SerializeField] private float crouchHeight = 1.2f;

    [Header("Camera Follow")]
    [SerializeField] private bool followCamera = true;
    [SerializeField] private Transform cameraTransform;   // defaults to Camera.main
    [SerializeField] private float cameraDistance = 5f;
    [SerializeField] private float cameraHeight = 1.6f;   // look-at point above the character's feet
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    // ---- State read by CharacterAnimator ----
    public bool IsGrounded => grounded;
    public bool IsCrouching => crouching;
    public bool IsRunning => running && !crouching && moveDir.sqrMagnitude > 0.01f;
    public float JumpWindup01 => windupTimer < 0f ? 0f : Mathf.Clamp01(windupTimer / Mathf.Max(jumpWindup, 0.0001f));
    public Vector3 Velocity => rb.linearVelocity;
    public float WalkSpeed => walkSpeed;
    public float RunSpeed => runSpeed;
    public Transform CameraTransform => cameraTransform;
    public event Action<float> Landed;   // argument: downward speed at impact (m/s)
    public event Action Jumped;

    private Rigidbody rb;
    private CapsuleCollider capsule;
    private float standHeight, standCenterY;

    private Vector3 moveDir;
    private bool running;
    private bool crouchToggle, crouchHeld;
    private bool crouching;
    private bool grounded;
    private float lastGroundedTime = -10f;
    private float lastJumpPressTime = -10f;
    private float lastJumpTime = -10f;
    private float windupTimer = -1f;
    private float lastAirVelocityY;
    private float recoveryTimer;
    private float yaw;
    private float pitch = 15f;

    // Called when the component is first added (or "Reset" in the Inspector): sensible defaults.
    private void Reset()
    {
        var col = GetComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.3f;
        col.center = new Vector3(0f, 0.9f, 0f);   // the courier model is ~1.76 m tall with its pivot at the feet
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        standHeight = capsule.height;
        standCenterY = capsule.center.y;

        // Keep the character upright and moving smoothly.
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        // Frictionless collider so the character doesn't stick to walls.
        capsule.material = new PhysicsMaterial("PlayerNoFriction")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        yaw = transform.eulerAngles.y;

        if (followCamera)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ---------------- Input (every frame) ----------------
    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }
        moveDir = forward * input.y + right * input.x;
        running = keyboard.leftShiftKey.isPressed;

        if (keyboard.spaceKey.wasPressedThisFrame)
            lastJumpPressTime = Time.time;   // buffered, consumed in FixedUpdate

        if (keyboard.cKey.wasPressedThisFrame)
            crouchToggle = !crouchToggle;
        crouchHeld = keyboard.leftCtrlKey.isPressed;
        if (running && moveDir.sqrMagnitude > 0.01f)
            crouchToggle = false;            // starting to run cancels a toggled crouch

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ---------------- Physics (fixed rate) ----------------
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        bool wasGrounded = grounded;

        // Right after take-off the ground check can still touch the floor; ignore it briefly.
        bool justJumped = Time.time - lastJumpTime < 0.2f && rb.linearVelocity.y > 0.1f;
        grounded = CheckGrounded() && !justJumped;

        if (grounded)
        {
            lastGroundedTime = Time.time;
            if (!wasGrounded)
            {
                float impact = -lastAirVelocityY;
                if (impact > 1f) Landed?.Invoke(impact);
                if (impact > hardLandingSpeed) recoveryTimer = hardLandingRecovery;
            }
        }
        else
        {
            lastAirVelocityY = rb.linearVelocity.y;
        }

        UpdateCrouch();
        UpdateJump(dt);

        // --- Horizontal movement: steer velocity toward the target velocity ---
        float speed = crouching ? crouchSpeed : (running ? runSpeed : walkSpeed);
        if (recoveryTimer > 0f)
        {
            recoveryTimer -= dt;
            speed *= 0.4f;    // stagger after a heavy landing
        }

        Vector3 targetVel = moveDir * speed;
        Vector3 vel = rb.linearVelocity;
        Vector3 horizontal = new Vector3(vel.x, 0f, vel.z);
        float accel = grounded ? acceleration : acceleration * airControl;
        Vector3 newHorizontal = Vector3.MoveTowards(horizontal, targetVel, accel * dt);
        rb.AddForce(newHorizontal - horizontal, ForceMode.VelocityChange);

        // --- Rotation: face the movement direction ---
        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * dt));
        }

        // --- Extra gravity for a snappier fall ---
        if (!grounded && extraGravity > 1f)
            rb.AddForce(Physics.gravity * (extraGravity - 1f), ForceMode.Acceleration);
    }

    private void UpdateJump(float dt)
    {
        bool buffered = Time.time - lastJumpPressTime <= jumpBuffer;
        bool canJump = Time.time - lastGroundedTime <= coyoteTime
                       && windupTimer < 0f
                       && Time.time - lastJumpTime > 0.25f;

        if (buffered && canJump)
        {
            lastJumpPressTime = -10f;   // consume the press
            if (crouching)
            {
                crouchToggle = false;   // Space while crouched = stand up
            }
            else if (grounded)
            {
                windupTimer = 0f;       // start the crouch wind-up
            }
            else
            {
                Launch();               // coyote jump: no time for a wind-up
            }
        }

        if (windupTimer >= 0f)
        {
            windupTimer += dt;
            if (windupTimer >= jumpWindup)
                Launch();
        }
    }

    private void Launch()
    {
        windupTimer = -1f;
        lastJumpTime = Time.time;
        float jumpVel = Mathf.Sqrt(2f * jumpHeight * -Physics.gravity.y);
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpVel, rb.linearVelocity.z);
        Jumped?.Invoke();
    }

    private void UpdateCrouch()
    {
        bool wantCrouch = (crouchToggle || crouchHeld) && grounded;

        if (wantCrouch && !crouching)
        {
            crouching = true;
            SetCapsuleHeight(crouchHeight);
        }
        else if (!wantCrouch && crouching && CanStandUp())
        {
            crouching = false;
            SetCapsuleHeight(standHeight);
        }
    }

    private void SetCapsuleHeight(float height)
    {
        capsule.height = height;
        // Keep the bottom of the capsule at the same place (the feet).
        capsule.center = new Vector3(capsule.center.x, standCenterY - (standHeight - height) * 0.5f, capsule.center.z);
    }

    private bool CanStandUp()
    {
        Vector3 top = transform.TransformPoint(capsule.center) + Vector3.up * (capsule.height * 0.5f - capsule.radius);
        float distance = standHeight - capsule.height;
        return !Physics.SphereCast(top, capsule.radius * 0.9f, Vector3.up, out _, distance,
            groundLayers, QueryTriggerInteraction.Ignore);
    }

    private bool CheckGrounded()
    {
        float radius = capsule.radius * 0.95f;
        Vector3 bottomSphere = transform.TransformPoint(capsule.center)
                               - Vector3.up * (capsule.height * 0.5f - capsule.radius);
        // Cast from slightly above the bottom sphere so we don't start inside the ground.
        Vector3 origin = bottomSphere + Vector3.up * 0.05f;
        return Physics.SphereCast(origin, radius, Vector3.down, out _,
            groundCheckDistance + 0.05f, groundLayers, QueryTriggerInteraction.Ignore);
    }

    // ---------------- Camera ----------------
    private void LateUpdate()
    {
        if (!followCamera || cameraTransform == null) return;

        var mouse = Mouse.current;
        if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            yaw += delta.x;
            pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
        }

        // Click the game view to re-lock the cursor after pressing Escape
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Vector3 target = transform.position + Vector3.up * cameraHeight;
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        cameraTransform.position = target - rot * Vector3.forward * cameraDistance;
        cameraTransform.LookAt(target);
    }
}
