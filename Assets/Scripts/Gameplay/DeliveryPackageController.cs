using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lets the courier pick up a shop parcel and place it inside the scooter's rear cargo box.
/// Added and bound automatically by ScooterController.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public class DeliveryPackageController : MonoBehaviour
{
    private enum PackageState { World, Approaching, PickingUp, Carried, Stowing, Stowed, Delivered }

    private const float PickupContact = 0.40f;

    [SerializeField] private float pickupRange = 2.2f;
    [SerializeField] private float stowRange = 2.4f;
    [SerializeField] private float pickupDuration = 1.15f;
    [SerializeField] private float stowDuration = 1.35f;

    private ScooterController scooter;
    private Transform rider;
    private Transform cargoBox;
    private Transform cargoLid;
    private Vector3 lidClosedPosition;
    private Quaternion lidClosedRotation;
    private Transform package;
    private GameObject pickupTemplate;
    private Transform pickupParent;
    private Transform replacementParcel;
    private Vector3 pickupPosition;
    private Transform leftGrip;
    private Transform rightGrip;
    private PackageState state;
    private float animationTime;
    private Vector3 animationStartPosition;
    private Quaternion animationStartRotation;
    private Vector3 animationStartScale;
    private Vector3 fittedScale;
    private Vector3 parcelLocalCenter;
    private Quaternion carryLocalRotation;
    private float pickupCrouch;
    private float approachDistance;
    private Renderer[] packageRenderers;
    private float carryPose;
    private float action01;
    private bool packageStored;
    private float lidCloseTimer;
    private PlayerController walker;
    private CharacterAnimator characterAnimator;
    private Rigidbody riderBody;
    private Quaternion interactionFacing;
    private string hint;
    private GUIStyle hintStyle;

    public bool IsCarrying => state == PackageState.Approaching || state == PackageState.PickingUp ||
                              state == PackageState.Carried || state == PackageState.Stowing;
    public bool IsStowed => state == PackageState.Stowed && package != null;
    public event System.Action PackageStowed;
    internal event System.Action PackagePickedUp;
    internal Vector3 PickupPosition => pickupPosition;
    internal System.Func<bool> TryDeliver { get; set; }
    internal System.Func<bool> CanUnload { get; set; }

    public void Bind(ScooterController owner, Transform courier)
    {
        scooter = owner;
        rider = courier;
        cargoBox = FindDeep(owner.transform, "Delivery closed hollow cargo box");
        cargoLid = FindDeep(owner.transform, "Delivery seated cargo lid");
        if (cargoLid != null)
        {
            lidClosedPosition = cargoLid.localPosition;
            lidClosedRotation = cargoLid.localRotation;
        }
    }

    /// <summary>Returns true when the E press was used for a package action.</summary>
    public bool TryInteract()
    {
        if (scooter == null || rider == null || scooter.IsMounted)
            return false;
        if (state == PackageState.Approaching || state == PackageState.PickingUp || state == PackageState.Stowing)
            return true;

        if (IsStowed && CanUnload?.Invoke() == true &&
            FlatDistance(rider.position, scooter.transform.position) <= stowRange)
        {
            package.SetParent(rider, true);
            SetPackageVisible(true);
            state = PackageState.Carried;
            return true;
        }

        if (state == PackageState.Carried)
        {
            if (TryDeliver?.Invoke() == true) return true;
            if (FlatDistance(rider.position, scooter.transform.position) > stowRange) return false;
            BeginStow();
            return true;
        }

        if (state != PackageState.World) return false;
        Transform nearest = FindNearestPackage();
        if (nearest == null) return false;
        BeginApproach(nearest);
        return true;
    }

    internal void RestockPickup()
    {
        if (state != PackageState.Delivered || pickupTemplate == null) return;
        package = null;
        var parcel = Instantiate(pickupTemplate, pickupParent, false);
        replacementParcel = parcel.transform;
        parcel.SetActive(true);
        state = PackageState.World;
    }

    internal bool GiveToCustomer(Transform parcelHold)
    {
        if (state != PackageState.Carried || package == null || parcelHold == null) return false;
        state = PackageState.Delivered;
        package.SetParent(parcelHold, true);
        SetPackagePose(parcelHold.position, parcelHold.rotation, fittedScale);
        return true;
    }

    private void Update()
    {
        if (scooter == null || rider == null) return;
        UpdateAnimation(Time.deltaTime);
        UpdateCharacterPose();
        UpdateHint();
    }

    private void FixedUpdate()
    {
        if (riderBody == null || (state != PackageState.Approaching && state != PackageState.PickingUp && state != PackageState.Stowing)) return;
        Vector3 horizontal = Vector3.zero;
        if (state == PackageState.Approaching && package != null)
        {
            Vector3 towardParcel = Vector3.ProjectOnPlane(PackageCenter() - riderBody.position, Vector3.up);
            float remaining = Mathf.Max(0f, towardParcel.magnitude - approachDistance);
            float speed = Mathf.Min(1.8f, remaining / Time.fixedDeltaTime);
            horizontal = towardParcel.normalized * speed;
        }
        Vector3 velocity = riderBody.linearVelocity;
        riderBody.linearVelocity = horizontal + Vector3.up * velocity.y;
        Quaternion rotation = Quaternion.RotateTowards(riderBody.rotation, interactionFacing, 360f * Time.fixedDeltaTime);
        riderBody.MoveRotation(rotation);
    }

    private void LateUpdate()
    {
        if (rider == null) return;

        float targetPose = state == PackageState.PickingUp || state == PackageState.Carried || state == PackageState.Stowing ? 1f : 0f;
        carryPose = Mathf.MoveTowards(carryPose, targetPose, Time.deltaTime * 5f);
        if (state == PackageState.Carried && package != null)
        {
            float speed = riderBody != null ? Vector3.ProjectOnPlane(riderBody.linearVelocity, Vector3.up).magnitude : 0f;
            float bob = Mathf.Sin(Time.time * 8f) * 0.012f * Mathf.Clamp01(speed / 2f);
            float sway = Mathf.Sin(Time.time * 4f) * 1.5f * Mathf.Clamp01(speed / 2f);
            Quaternion rotation = CarryRotation() * Quaternion.Euler(0f, 0f, sway);
            SetPackagePose(CarryPosition(rotation) + Vector3.up * bob, rotation, fittedScale);
        }

        if (characterAnimator == null) return;
        float grip = 0f;
        if (state == PackageState.PickingUp) grip = Smooth(Mathf.InverseLerp(0.08f, PickupContact, action01));
        if (state == PackageState.Carried) grip = carryPose;
        if (state == PackageState.Stowing && !packageStored) grip = 1f - Smooth(Mathf.InverseLerp(0.68f, 0.92f, action01));
        characterAnimator.SetParcelGrip(leftGrip, rightGrip, grip);
    }

    private void BeginApproach(Transform target)
    {
        package = target;
        packageRenderers = package.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = CombinedBounds(packageRenderers, package.position);
        BeginCharacterAction(bounds.center);
        Vector3 forward = interactionFacing * Vector3.forward;
        float halfDepth = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
        approachDistance = Mathf.Clamp(halfDepth + 0.32f, 0.48f, 0.78f);
        animationTime = 0f;
        state = PackageState.Approaching;
    }

    private void BeginPickup()
    {
        if (pickupTemplate == null)
        {
            pickupParent = package.parent;
            pickupPosition = PackageCenter();
            // Keep a clean copy before the pickup animation modifies the colliders and adds grip points.
            pickupTemplate = Instantiate(package.gameObject, package.parent, false);
            pickupTemplate.SetActive(false);
        }
        foreach (Collider col in package.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (Rigidbody body in package.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        package.SetParent(null, true);
        animationStartPosition = package.position;
        animationStartRotation = package.rotation;
        animationStartScale = package.localScale;

        Bounds bounds = CombinedBounds(packageRenderers, package.position);
        float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float fit = largest > 0.7f ? 0.7f / largest : 1f;
        fittedScale = animationStartScale * fit;
        Vector3 parcelForward = Quaternion.Inverse(interactionFacing) * package.forward;
        float yaw = Mathf.Atan2(parcelForward.x, parcelForward.z) * Mathf.Rad2Deg;
        carryLocalRotation = Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f);
        CreateGrips();
        float gripHeight = (leftGrip.position.y + rightGrip.position.y) * 0.5f - rider.position.y;
        pickupCrouch = Mathf.InverseLerp(1.15f, 0.25f, gripHeight);
        animationTime = 0f;
        state = PackageState.PickingUp;
    }

    private void BeginStow()
    {
        if (package == null) return;
        package.SetParent(null, true);
        animationStartPosition = package.position;
        animationStartRotation = package.rotation;
        animationStartScale = package.localScale;
        animationTime = 0f;
        packageStored = false;
        lidCloseTimer = 0f;
        BeginCharacterAction(CargoCenter());
        state = PackageState.Stowing;
    }

    private void UpdateAnimation(float dt)
    {
        if (state == PackageState.Approaching)
        {
            animationTime += dt;
            if (package != null)
            {
                FaceInteraction(dt);
                float distance = FlatDistance(rider.position, PackageCenter());
                if ((distance <= approachDistance + 0.06f || riderBody == null) &&
                    Quaternion.Angle(rider.rotation, interactionFacing) <= 12f)
                {
                    BeginPickup();
                    return;
                }
                if (animationTime < 1.8f) return;
            }
            state = PackageState.World;
            package = null;
            EndCharacterAction();
        }
        else if (state == PackageState.PickingUp)
        {
            animationTime += dt;
            float t = Mathf.Clamp01(animationTime / pickupDuration);
            action01 = t;
            FaceInteraction(dt);
            float lift = Smooth(Mathf.InverseLerp(PickupContact, 0.95f, t));
            Vector3 end = CarryPosition(CarryRotation());
            Vector3 pos = Vector3.Lerp(animationStartPosition, end, lift)
                          + Vector3.up * (Mathf.Sin(lift * Mathf.PI) * 0.12f);
            SetPackagePose(pos, Quaternion.Slerp(animationStartRotation, CarryRotation(), lift),
                           Vector3.Lerp(animationStartScale, fittedScale, lift));
            if (t >= 1f)
            {
                package.SetParent(rider, true);
                state = PackageState.Carried;
                EndCharacterAction();
                PackagePickedUp?.Invoke();
            }
        }
        else if (state == PackageState.Stowing)
        {
            animationTime += dt;
            FaceInteraction(dt);

            if (!packageStored)
            {
                float insertTime = Mathf.Max(stowDuration, 1.1f);
                float t = Mathf.Clamp01(animationTime / insertTime);
                action01 = t;
                SetLidOpen(Smooth(Mathf.InverseLerp(0f, 0.22f, t)));

                Vector3 end = CargoCenter();
                Vector3 above = CargoTop() + Vector3.up * 0.18f;
                float approach = Smooth(Mathf.InverseLerp(0.25f, 0.68f, t));
                float lower = Smooth(Mathf.InverseLerp(0.68f, 1f, t));
                Vector3 pos = Vector3.Lerp(Vector3.Lerp(animationStartPosition, above, approach), end, lower);
                float eased = Smooth(t);
                SetPackagePose(pos, Quaternion.Slerp(animationStartRotation, CargoRotation(), eased),
                               Vector3.Lerp(animationStartScale, fittedScale * 0.85f, eased));

                // Only enter the closing stage after the parcel has physically reached the box centre.
                if (t >= 1f && Vector3.Distance(package.position, end) < 0.03f)
                {
                    if (cargoBox != null) package.SetParent(cargoBox, true);
                    SetPackageVisible(false);
                    packageStored = true;
                    lidCloseTimer = 0f;
                    SetLidOpen(1f);
                }
            }
            else
            {
                lidCloseTimer += dt;
                const float holdOpenTime = 0.35f;
                const float closeDuration = 0.55f;
                float close = Smooth(Mathf.InverseLerp(holdOpenTime, holdOpenTime + closeDuration, lidCloseTimer));
                SetLidOpen(1f - close);
                action01 = 1f - close;
                if (close >= 1f)
                {
                    SetLidOpen(0f);
                    carryPose = 0f;
                    state = PackageState.Stowed;
                    EndCharacterAction();
                    PackageStowed?.Invoke();
                }
            }
        }
    }

    private void UpdateHint()
    {
        hint = null;
        if (scooter.IsMounted || state == PackageState.Approaching || state == PackageState.PickingUp || state == PackageState.Stowing) return;
        if (state == PackageState.Carried)
        {
            if (FlatDistance(rider.position, scooter.transform.position) <= stowRange)
                hint = "[E] Put package in scooter box";
            return;
        }
        if (state == PackageState.World && FindNearestPackage() != null) hint = "[E] Pick up package";
    }

    private Transform FindNearestPackage()
    {
        if (replacementParcel != null)
        {
            Renderer renderer = replacementParcel.GetComponentInChildren<Renderer>();
            return renderer != null &&
                FlatDistance(rider.position, renderer.bounds.ClosestPoint(rider.position)) < pickupRange
                ? replacementParcel : null;
        }

        Transform best = null;
        float bestDistance = pickupRange;
        foreach (GameObject piece in HierarchyGroup.AllPieces())
        {
            if (!piece.activeInHierarchy || !piece.name.StartsWith("SM_Boxes")) continue;
            Renderer renderer = piece.GetComponentInChildren<Renderer>();
            if (renderer == null) continue;
            Vector3 closest = renderer.bounds.ClosestPoint(rider.position);
            float distance = FlatDistance(rider.position, closest);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = piece.transform;
        }
        return best;
    }

    private Vector3 CarryPosition(Quaternion rotation)
    {
        Vector3 center = rider.TransformPoint(new Vector3(0f, 1.18f, 0.40f));
        Vector3 parentScale = package.parent != null ? package.parent.lossyScale : Vector3.one;
        return center - rotation * Vector3.Scale(parcelLocalCenter, Vector3.Scale(fittedScale, parentScale));
    }

    private Quaternion CarryRotation()
    {
        return rider.rotation * carryLocalRotation;
    }

    private Vector3 PackageCenter() => CombinedBounds(packageRenderers, package.position).center;

    private void CreateGrips()
    {
        // Combine mesh bounds in parcel space so the grips follow the box through rotation and scaling.
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in packageRenderers)
        {
            Bounds mesh = renderer.localBounds;
            Matrix4x4 toParcel = package.worldToLocalMatrix * renderer.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                    (corner & 1) == 0 ? -1f : 1f,
                    (corner & 2) == 0 ? -1f : 1f,
                    (corner & 4) == 0 ? -1f : 1f));
                point = toParcel.MultiplyPoint3x4(point);
                if (hasBounds) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); hasBounds = true; }
            }
        }

        Quaternion toParcelAxes = Quaternion.Inverse(carryLocalRotation);
        parcelLocalCenter = bounds.center;
        Vector3 side = toParcelAxes * Vector3.right;
        Vector3 forward = toParcelAxes * Vector3.forward;
        float halfWidth = Mathf.Abs(side.x) * bounds.extents.x + Mathf.Abs(side.z) * bounds.extents.z;
        float halfDepth = Mathf.Abs(forward.x) * bounds.extents.x + Mathf.Abs(forward.z) * bounds.extents.z;
        Vector3 lowCenter = bounds.center - Vector3.up * (bounds.extents.y * 0.55f) - forward * (halfDepth * 0.3f);
        leftGrip = CreateGrip("LeftGrip", lowCenter - side * halfWidth, forward, side);
        rightGrip = CreateGrip("RightGrip", lowCenter + side * halfWidth, forward, -side);
    }

    private Transform CreateGrip(string gripName, Vector3 position, Vector3 forward, Vector3 inward)
    {
        Transform grip = new GameObject(gripName).transform;
        grip.SetParent(package, false);
        grip.localPosition = position;
        grip.localRotation = Quaternion.LookRotation(forward, inward + Vector3.up * 0.25f);
        return grip;
    }

    private Quaternion CargoRotation()
    {
        Transform target = cargoBox != null ? cargoBox : scooter.transform;
        return target.rotation * Quaternion.Euler(0f, 90f, 0f);
    }

    private Vector3 CargoCenter()
    {
        Renderer r = cargoBox != null ? cargoBox.GetComponentInChildren<Renderer>() : null;
        return r != null ? r.bounds.center : scooter.transform.TransformPoint(0f, 1.05f, -0.65f);
    }

    private Vector3 CargoTop()
    {
        Renderer r = cargoBox != null ? cargoBox.GetComponentInChildren<Renderer>() : null;
        if (r == null) return scooter.transform.TransformPoint(0f, 1.35f, -0.65f);
        Vector3 p = r.bounds.center;
        p.y = r.bounds.max.y;
        return p;
    }

    private void BeginCharacterAction(Vector3 lookAt)
    {
        walker = rider.GetComponent<PlayerController>();
        characterAnimator = rider.GetComponent<CharacterAnimator>();
        riderBody = rider.GetComponent<Rigidbody>();
        if (riderBody != null)
        {
            Vector3 velocity = riderBody.linearVelocity;
            riderBody.linearVelocity = new Vector3(0f, velocity.y, 0f);
        }
        if (walker != null) walker.enabled = false;

        Vector3 direction = Vector3.ProjectOnPlane(lookAt - rider.position, Vector3.up);
        interactionFacing = direction.sqrMagnitude > 0.01f ? Quaternion.LookRotation(direction) : rider.rotation;
    }

    private void FaceInteraction(float dt)
    {
        // Rigidbody rotation is performed at the physics rate to avoid fighting interpolation.
        if (riderBody == null)
            rider.rotation = Quaternion.Slerp(rider.rotation, interactionFacing, 1f - Mathf.Exp(-8f * dt));
    }

    private void EndCharacterAction()
    {
        if (walker != null) walker.enabled = true;
        action01 = 0f;
    }

    private void UpdateCharacterPose()
    {
        if (characterAnimator == null && rider != null) characterAnimator = rider.GetComponent<CharacterAnimator>();
        if (characterAnimator == null) return;

        float reach = 0f, stow = 0f, bend = 0f, crouch = 0f;
        if (state == PackageState.PickingUp)
        {
            reach = Smooth(Mathf.InverseLerp(0f, PickupContact, action01)) *
                    (1f - Smooth(Mathf.InverseLerp(PickupContact, 0.92f, action01)));
            bend = reach * Mathf.Lerp(12f, 55f, pickupCrouch);
            crouch = reach * pickupCrouch * 0.55f;
        }
        else if (state == PackageState.Stowing)
        {
            stow = Mathf.Sin(action01 * Mathf.PI);
            bend = stow * 15f;
            crouch = stow * 0.05f;
        }
        characterAnimator.SetDeliveryPose(carryPose, reach, stow, bend, crouch);
    }

    private void SetLidOpen(float amount)
    {
        if (cargoLid == null) return;
        float eased = Smooth(Mathf.Clamp01(amount));
        // This asset's hinge meshes do not share a dependable rotation axis. Lift the lid cleanly
        // instead, keeping its authored rotation fixed so it can never spin or flip.
        cargoLid.localPosition = lidClosedPosition + new Vector3(0f, 0.32f, -0.08f) * eased;
        cargoLid.localRotation = lidClosedRotation;
    }

    private void SetPackageVisible(bool visible)
    {
        if (packageRenderers == null) return;
        foreach (Renderer r in packageRenderers) if (r != null) r.enabled = visible;
    }

    private void SetPackagePose(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (package == null) return;
        package.SetPositionAndRotation(position, rotation);
        package.localScale = scale;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(hint)) return;
        hintStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        Vector2 size = hintStyle.CalcSize(new GUIContent(hint)) + new Vector2(24f, 12f);
        GUI.Box(new Rect((Screen.width - size.x) * 0.5f, Screen.height - size.y - 82f, size.x, size.y), hint, hintStyle);
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
    private static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

    private static Bounds CombinedBounds(Renderer[] renderers, Vector3 fallback)
    {
        Bounds bounds = new Bounds(fallback, Vector3.zero);
        bool found = false;
        foreach (Renderer r in renderers)
        {
            if (r == null) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    private static Transform FindDeep(Transform root, string exactName)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == exactName) return t;
        return null;
    }
}
