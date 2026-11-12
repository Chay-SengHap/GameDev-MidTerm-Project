using UnityEngine;

/// <summary>
/// Poses a humanoid character (such as delivery-courier) into a seated scooter riding position.
/// Works in both the Unity Editor Scene View ([ExecuteAlways]) and during runtime.
/// Can be placed on the scooter (SitPoint / delivery-scooter) or directly on the courier GameObject.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ScooterRiderPose : MonoBehaviour
{
    [Header("Toggle")]
    [Tooltip("Enable or disable the seated riding pose.")]
    public bool isSitting = true;

    [Header("Hips / Saddle Placement")]
    [Tooltip("Offset applied to Hips to sit down onto the saddle.")]
    public Vector3 hipsOffset = new Vector3(0f, -0.20f, -0.05f);

    [Header("Thighs / Upper Legs")]
    [Range(0f, 110f)]
    [Tooltip("Pitches thighs forward into a sitting angle.")]
    public float thighPitch = 75f;

    [Range(-30f, 30f)]
    [Tooltip("Spreads knees slightly outward to straddle the scooter.")]
    public float thighSpread = 10f;

    [Header("Knees / Lower Legs")]
    [Range(0f, 120f)]
    [Tooltip("Bends knees so lower legs drop down towards the footboard.")]
    public float kneeBend = 80f;

    [Header("Feet")]
    [Range(-60f, 60f)]
    [Tooltip("Pitches feet flat onto the scooter footboard.")]
    public float footPitch = 10f;

    [Header("Torso & Posture")]
    [Range(-30f, 30f)]
    [Tooltip("Leans upper body forward towards the handlebars.")]
    public float spineForwardLean = 12f;

    [Range(-30f, 30f)]
    [Tooltip("Angles head to look forward down the road.")]
    public float headLookAngle = -5f;

    [Header("Arms (Shoulders to Elbows)")]
    [Range(-90f, 90f)]
    [Tooltip("Angles upper arms forward towards the handlebars.")]
    public float armPitch = 35f;

    [Range(-90f, 90f)]
    [Tooltip("Rotates upper arms down from T-pose to natural angle.")]
    public float armDown = 60f;

    [Range(-45f, 45f)]
    [Tooltip("Tucks elbows slightly inward towards the torso.")]
    public float armInward = 10f;

    [Header("Forearms (Elbows to Hands)")]
    [Range(-90f, 90f)]
    [Tooltip("Bends forearms to reach the handlebar grips.")]
    public float forearmBend = 30f;

    // Bone transforms
    [HideInInspector] public Transform hips;
    [HideInInspector] public Transform spine;
    [HideInInspector] public Transform spine1;
    [HideInInspector] public Transform head;
    [HideInInspector] public Transform leftUpLeg;
    [HideInInspector] public Transform rightUpLeg;
    [HideInInspector] public Transform leftLeg;
    [HideInInspector] public Transform rightLeg;
    [HideInInspector] public Transform leftFoot;
    [HideInInspector] public Transform rightFoot;
    [HideInInspector] public Transform leftArm;
    [HideInInspector] public Transform rightArm;
    [HideInInspector] public Transform leftForeArm;
    [HideInInspector] public Transform rightForeArm;

    // Standing (rest) position of the Hips. Saved with the scene so the seat offset is always applied to the
    // same base - re-reading the already lowered hips after a script reload used to stack the offset every time.
    [SerializeField, HideInInspector] private Vector3 restHipsLocalPos;
    [SerializeField, HideInInspector] private bool hasRestHips;
    [SerializeField, HideInInspector] private int restHipsVersion;
    private const int RestHipsVersion = 2;   // bump to make saved scenes recalculate the rest position
    private const float AnkleHeight = 0.09f; // ankle joint above the sole

    private void Awake()
    {
        FindBones();
    }

    private void OnEnable()
    {
        FindBones();
        ApplyPose();
    }

    private void OnValidate()
    {
        FindBones();
        ApplyPose();
    }

    private void Update()
    {
        // Continuously apply in Edit Mode so the pose is always visible in Scene View
        if (!Application.isPlaying)
        {
            ApplyPose();
        }
    }

    private void LateUpdate()
    {
        // At runtime, apply in LateUpdate after any animations
        if (Application.isPlaying)
        {
            ApplyPose();
        }
    }

    public void FindBones()
    {
        hips = FindDeepChild(transform, "Hips");
        spine = FindDeepChild(transform, "Spine");
        spine1 = FindDeepChild(transform, "Spine1");
        head = FindDeepChild(transform, "Head");

        leftUpLeg = FindDeepChild(transform, "LeftUpLeg");
        rightUpLeg = FindDeepChild(transform, "RightUpLeg");
        leftLeg = FindDeepChild(transform, "LeftLeg");
        rightLeg = FindDeepChild(transform, "RightLeg");
        leftFoot = FindDeepChild(transform, "LeftFoot");
        rightFoot = FindDeepChild(transform, "RightFoot");

        leftArm = FindDeepChild(transform, "LeftArm");
        rightArm = FindDeepChild(transform, "RightArm");
        leftForeArm = FindDeepChild(transform, "LeftForeArm");
        rightForeArm = FindDeepChild(transform, "RightForeArm");

        if (hips != null && (!hasRestHips || restHipsVersion < RestHipsVersion)) CaptureRestHips();
    }

    /// Works out where the Hips stand from the leg bones (hip joint + thigh + shin + ankle height), so it doesn't
    /// depend on the current - possibly already lowered - hips position. Falls back to the current position.
    private void CaptureRestHips()
    {
        if (leftUpLeg != null && leftLeg != null && leftFoot != null)
        {
            float height = Mathf.Abs(leftUpLeg.localPosition.y) + leftLeg.localPosition.magnitude
                         + leftFoot.localPosition.magnitude + AnkleHeight;
            restHipsLocalPos = new Vector3(0f, height, 0f);
        }
        else
        {
            restHipsLocalPos = hips.localPosition;
        }
        hasRestHips = true;
        restHipsVersion = RestHipsVersion;
    }

    /// Standing hips position (local to the hips' parent).
    public Vector3 RestHipsLocalPosition => restHipsLocalPos;

    private Transform FindDeepChild(Transform parent, string childName)
    {
        if (parent == null) return null;
        if (parent.name.Equals(childName, System.StringComparison.OrdinalIgnoreCase))
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            var res = FindDeepChild(parent.GetChild(i), childName);
            if (res != null) return res;
        }
        return null;
    }

    public void ApplyPose()
    {
        if (!isSitting) return;

        if (hips == null)
        {
            FindBones();
            if (hips == null) return;
        }

        // 1. Lower hips onto scooter saddle
        hips.localPosition = restHipsLocalPos + hipsOffset;

        // 2. Spine posture & head tilt
        if (spine != null)
        {
            spine.localRotation = Quaternion.Euler(spineForwardLean, 0f, 0f);
        }
        if (head != null)
        {
            head.localRotation = Quaternion.Euler(headLookAngle, 0f, 0f);
        }

        // 3. Thighs (Left and Right pitch forward and spread slightly)
        if (leftUpLeg != null)
        {
            leftUpLeg.localRotation = Quaternion.Euler(-thighPitch, -thighSpread, 0f);
        }
        if (rightUpLeg != null)
        {
            rightUpLeg.localRotation = Quaternion.Euler(-thighPitch, thighSpread, 0f);
        }

        // 4. Knees (Lower legs bend down towards footboard)
        if (leftLeg != null)
        {
            leftLeg.localRotation = Quaternion.Euler(kneeBend, 0f, 0f);
        }
        if (rightLeg != null)
        {
            rightLeg.localRotation = Quaternion.Euler(kneeBend, 0f, 0f);
        }

        // 5. Feet (Flat on footboard)
        if (leftFoot != null)
        {
            leftFoot.localRotation = Quaternion.Euler(-footPitch, 0f, 0f);
        }
        if (rightFoot != null)
        {
            rightFoot.localRotation = Quaternion.Euler(-footPitch, 0f, 0f);
        }

        // 6. Arms (Reach forward to handlebars)
        if (leftArm != null)
        {
            leftArm.localRotation = Quaternion.Euler(-armPitch, armInward, armDown);
        }
        if (rightArm != null)
        {
            rightArm.localRotation = Quaternion.Euler(-armPitch, -armInward, -armDown);
        }

        // 7. Forearms (Bend towards grips)
        if (leftForeArm != null)
        {
            leftForeArm.localRotation = Quaternion.Euler(0f, forearmBend, 0f);
        }
        if (rightForeArm != null)
        {
            rightForeArm.localRotation = Quaternion.Euler(0f, -forearmBend, 0f);
        }
    }

    /// <summary>
    /// Sit on the scooter.
    /// </summary>
    public void MountScooter()
    {
        isSitting = true;
    }

    /// <summary>
    /// Resets all pose values to their recommended default scooter riding position.
    /// </summary>
    public void ResetToDefaultPose()
    {
        hipsOffset = new Vector3(0f, -0.20f, -0.05f);
        thighPitch = 75f;
        thighSpread = 10f;
        kneeBend = 80f;
        footPitch = 10f;
        spineForwardLean = 12f;
        headLookAngle = -5f;
        armPitch = 35f;
        armDown = 60f;
        armInward = 10f;
        forearmBend = 30f;
        isSitting = true;
    }

    /// <summary>
    /// Stand up / restore default standing posture.
    /// </summary>
    public void DismountScooter()
    {
        isSitting = false;
        if (hips != null)
        {
            hips.localPosition = restHipsLocalPos;
            hips.localRotation = Quaternion.identity;
        }
        if (leftUpLeg != null) leftUpLeg.localRotation = Quaternion.identity;
        if (rightUpLeg != null) rightUpLeg.localRotation = Quaternion.identity;
        if (leftLeg != null) leftLeg.localRotation = Quaternion.identity;
        if (rightLeg != null) rightLeg.localRotation = Quaternion.identity;
        if (leftFoot != null) leftFoot.localRotation = Quaternion.identity;
        if (rightFoot != null) rightFoot.localRotation = Quaternion.identity;
        if (leftArm != null) leftArm.localRotation = Quaternion.identity;
        if (rightArm != null) rightArm.localRotation = Quaternion.identity;
        if (leftForeArm != null) leftForeArm.localRotation = Quaternion.identity;
        if (rightForeArm != null) rightForeArm.localRotation = Quaternion.identity;
        if (spine != null) spine.localRotation = Quaternion.identity;
        if (head != null) head.localRotation = Quaternion.identity;
    }
}
