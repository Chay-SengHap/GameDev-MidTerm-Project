using System.Linq;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[ExecuteAlways]
public class CityPedestrian : MonoBehaviour
{
    [SerializeField] private Vector3 start;
    [SerializeField] private Vector3 end;
    [SerializeField] private Vector3[] waypoints;
    [SerializeField] private Vector3 lookAt;
    [SerializeField] private AnimationClip idle;
    [SerializeField] private AnimationClip walk;
    [SerializeField] private AnimationClip greeting;
    [SerializeField] private bool chatting;
    [SerializeField] private bool sitting;
    [SerializeField] private float seatSurfaceY;
    [SerializeField] private int index;
    [SerializeField] private bool configured;

    private static readonly string[][] ConversationPairs =
    {
        new[] { "Hey! How's your day?", "Pretty good! Nice weather today." },
        new[] { "Did you try the restaurant?", "Yeah! Their food is amazing." },
        new[] { "Waiting for my delivery!", "Those delivery scooters are fast!" },
        new[] { "Let's grab coffee after this.", "Sounds great, my treat!" },
        new[] { "Traffic looks much smoother now.", "Totally, love walking around here." },
        new[] { "Have you seen the new pizza place?", "Best slices in the whole city!" },
        new[] { "Great spot to chill and talk.", "For sure! Glad I ran into you." },
    };

    private Vector3 walkDir;
    private Vector3 stepTarget;
    private float distanceSinceTurn;
    private float minLegDistance;
    private bool alwaysTurnRight;

    private AnimationClip current;
    private Animator animator;
    private Animator facialAnimator;
    private PlayableGraph graph;
    private AnimationClipPlayable motion;
    private float waitUntil, nextGreeting, speed;
    private Transform speechBubble;
    private TextMesh speechText;
    private Renderer speechBgRenderer;
    private static Material sharedBgMaterial;

    // Humanoid bones for sitting pose
    private Transform hips;
    private Transform spine;
    private Transform neck;
    private Transform head;
    private Transform leftUpLeg, rightUpLeg;
    private Transform leftLeg, rightLeg;
    private Transform leftFoot, rightFoot;
    private Transform leftArm, rightArm;
    private Transform leftForeArm, rightForeArm;
    private bool bonesCached;

    // Rest local rotations so Edit Mode never accumulates rotations across frames
    private Quaternion restSpine, restNeck, restHead;
    private Quaternion restLeftUpLeg, restRightUpLeg;
    private Quaternion restLeftLeg, restRightLeg;
    private Quaternion restLeftFoot, restRightFoot;
    private Quaternion restLeftArm, restRightArm;
    private Quaternion restLeftForeArm, restRightForeArm;

    internal void Initialize(
        Vector3[] route,
        Vector3 lookPoint,
        AnimationClip idleClip,
        AnimationClip walkClip,
        AnimationClip greetingClip,
        int slotIndex,
        bool isChatting = false,
        bool isSitting = false,
        float seatY = 0f)
    {
        waypoints = route != null && route.Length > 0 ? route : new[] { transform.position };
        start = waypoints[0];
        end = waypoints[waypoints.Length - 1];
        lookAt = lookPoint;
        idle = idleClip;
        walk = walkClip;
        greeting = greetingClip;
        chatting = isChatting;
        sitting = isSitting;
        seatSurfaceY = seatY;
        index = slotIndex;
        configured = true;

        if (!sitting)
        {
            bool isStationary = waypoints.Length <= 1 || (start - end).sqrMagnitude < 0.01f;
            Face(isStationary ? lookPoint : waypoints[Mathf.Min(1, waypoints.Length - 1)], 360f);
        }

        if (sitting)
        {
            CacheBones();
            ApplySeatedPose();
        }

        if (chatting)
            EnsureSpeechBubble();

        if (Application.isPlaying)
            SetupRuntime();
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            if (sitting)
            {
                CacheBones();
                ApplySeatedPose();
            }
            if (chatting)
                EnsureSpeechBubble();
            return;
        }

        if (!configured)
        {
            start = transform.position;
            end = transform.position;
            waypoints = new[] { start };
            lookAt = transform.position + transform.forward;
            configured = true;
        }
        SetupRuntime();
    }

    private void SetupRuntime()
    {
        if (graph.IsValid()) return;
        if (waypoints == null || waypoints.Length == 0)
            waypoints = (start - end).sqrMagnitude > 0.01f ? new[] { start, end } : new[] { start };

        speed = Random.Range(1.15f, 1.52f);
        waitUntil = Time.time + Random.Range(0f, 1.2f);
        nextGreeting = Time.time + (chatting ? (index % 2 == 0 ? 1.2f : 4.2f) : Random.Range(8f, 15f));

        // Half the walkers always turn RIGHT at corners (clockwise around a block/park),
        // and the other half always turn LEFT at corners (counter-clockwise around a block/park).
        alwaysTurnRight = (Mathf.Abs(index) % 2) == 0;
        minLegDistance = Random.Range(11f, 18f);
        distanceSinceTurn = 0f;

        bool isStationary = sitting || waypoints.Length <= 1 || (start - end).sqrMagnitude < 0.01f;
        if (!isStationary)
        {
            Vector3 initialDir = waypoints[1] - transform.position;
            initialDir.y = 0f;
            walkDir = SnapToCardinal(initialDir.sqrMagnitude > 0.01f ? initialDir : transform.forward);
            stepTarget = transform.position;
            PickNextSidewalkStep(forceTurn: false);
            Face(stepTarget, 360f);
        }
        else if (!sitting)
        {
            Face(lookAt, 360f);
        }

        var animators = GetComponentsInChildren<Animator>(true);
        animator = animators.FirstOrDefault(a => a.avatar != null) ?? animators.FirstOrDefault();
        facialAnimator = animators.FirstOrDefault(a => a != animator && a.avatar == null);

        if (animator == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        ResolveOwnClips();

        if (facialAnimator != null && facialAnimator.runtimeAnimatorController != null)
            SetTriggerOrBool(facialAnimator, chatting ? "Talk" : "Smile 01", true);

        if (sitting)
            CacheBones();

        if (chatting)
            EnsureSpeechBubble();

        Play(idle);
        if (graph.IsValid() && idle != null && idle.length > 0f)
            motion.SetTime(Random.Range(0f, idle.length));
    }

    private void ResolveOwnClips()
    {
        var controller = animator.runtimeAnimatorController;
        if (controller == null || controller.animationClips == null || controller.animationClips.Length == 0)
            return;

        var clips = controller.animationClips;
        var ownWalks = clips.Where(c => c != null && Normalize(c.name).Contains("walk")).ToArray();
        var ownIdles = clips.Where(c => c != null && Normalize(c.name).Contains("idle") &&
            !Normalize(c.name).Contains("facial")).ToArray();
        var ownGreet = clips.FirstOrDefault(c => c != null &&
            (Normalize(c.name).Contains("greet") || Normalize(c.name).EndsWith("hi")));

        if (ownIdles.Length > 0)
            idle = ownIdles[Mathf.Abs(index) % ownIdles.Length];
        if (ownWalks.Length > 0)
            walk = ownWalks[Mathf.Abs(index) % ownWalks.Length];
        if (ownGreet != null && (chatting || greeting != null))
            greeting = ownGreet;
    }

    private static void SetTriggerOrBool(Animator anim, string paramName, bool value)
    {
        foreach (var param in anim.parameters)
        {
            if (param.name != paramName) continue;
            if (param.type == AnimatorControllerParameterType.Bool)
                anim.SetBool(paramName, value);
            break;
        }
    }

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private void Update()
    {
        if (!Application.isPlaying)
        {
            UpdateSpeechBubbleEditor();
            return;
        }

        if (animator == null) return;

        if (chatting)
            UpdateSpeechBubbleRuntime();

        bool isStationary = sitting || waypoints == null || waypoints.Length <= 1 || (start - end).sqrMagnitude < 0.01f;
        if (isStationary)
        {
            if (!sitting)
                Face(lookAt, 180f * Time.deltaTime);

            if (greeting != null && Time.time >= nextGreeting)
            {
                Play(greeting);
                nextGreeting = Time.time + (chatting ? Random.Range(4.5f, 7.5f) : Random.Range(10f, 18f));
            }
            else if (current == greeting && greeting != null && graph.IsValid() &&
                     motion.GetTime() >= greeting.length)
            {
                Play(idle);
            }
            Loop();
            return;
        }

        if (Time.time < waitUntil)
        {
            Loop();
            return;
        }

        Vector3 position = transform.position;
        Vector3 flatDiff = stepTarget - position;
        flatDiff.y = 0f;

        if (flatDiff.sqrMagnitude < 0.04f)
        {
            PickNextSidewalkStep(forceTurn: false);
            return;
        }

        float moveStep = speed * Time.deltaTime;
        Vector3 next = Vector3.MoveTowards(position, stepTarget, moveStep);
        if (!CityPedestrians.TryPavement(next, out Vector3 ground))
        {
            PickNextSidewalkStep(forceTurn: true);
            return;
        }

        distanceSinceTurn += Vector3.Distance(new Vector3(position.x, 0f, position.z), new Vector3(ground.x, 0f, ground.z));
        Face(stepTarget, 340f * Time.deltaTime);
        transform.position = ground;
        Play(walk);
        Loop();
    }

    /// <summary>
    /// Chooses the next sidewalk step so the citizen walks straight along the sidewalk and
    /// consistently turns 90 degrees (always left or always right) at block/park corners.
    /// </summary>
    private void PickNextSidewalkStep(bool forceTurn)
    {
        const float stepLen = 1.5f;
        Vector3 pos = transform.position;
        Vector3 preferredTurn = Turn90(walkDir, alwaysTurnRight);
        Vector3 oppositeTurn = Turn90(walkDir, !alwaysTurnRight);

        // 1. If we've walked along a block and reached a corner where our preferred 90-degree sidewalk exists,
        //    turn 90 degrees around the corner (so we orbit around the park / building block).
        if (!forceTurn && distanceSinceTurn >= minLegDistance)
        {
            if (HasSidewalkCorner(pos, preferredTurn, stepLen, out Vector3 cornerStep))
            {
                walkDir = preferredTurn;
                stepTarget = cornerStep;
                distanceSinceTurn = 0f;
                minLegDistance = Random.Range(11f, 18f);
                return;
            }
        }

        // 2. Otherwise keep walking straight along the current sidewalk if clear
        if (!forceTurn &&
            CityPedestrians.TryPavement(pos + walkDir * (stepLen * 0.5f), out _) &&
            CityPedestrians.TryPavement(pos + walkDir * stepLen, out Vector3 straightSpot))
        {
            stepTarget = straightSpot;
            return;
        }

        // 3. Reached the end/corner of the sidewalk! Turn 90 degrees in our preferred direction (Right or Left)
        if (HasSidewalkCorner(pos, preferredTurn, stepLen, out Vector3 turnSpot))
        {
            walkDir = preferredTurn;
            stepTarget = turnSpot;
            distanceSinceTurn = 0f;
            minLegDistance = Random.Range(11f, 18f);
            return;
        }

        // 4. If preferred 90-degree turn is not available at this corner, try the other 90-degree turn
        if (HasSidewalkCorner(pos, oppositeTurn, stepLen, out Vector3 altTurnSpot))
        {
            walkDir = oppositeTurn;
            stepTarget = altTurnSpot;
            distanceSinceTurn = 0f;
            minLegDistance = Random.Range(11f, 18f);
            return;
        }

        // 5. Only reverse 180 degrees if at a true dead end, with a brief idle pause
        walkDir = -walkDir;
        distanceSinceTurn = 0f;
        if (CityPedestrians.TryPavement(pos + walkDir * stepLen, out Vector3 backSpot))
            stepTarget = backSpot;
        waitUntil = Time.time + Random.Range(1.0f, 2.2f);
        Play(idle);
    }

    private static bool HasSidewalkCorner(Vector3 origin, Vector3 dir, float stepLen, out Vector3 firstStep)
    {
        firstStep = default;
        if (!CityPedestrians.TryPavement(origin + dir * (stepLen * 0.5f), out _))
            return false;
        if (!CityPedestrians.TryPavement(origin + dir * stepLen, out firstStep))
            return false;
        // Ensure the sidewalk continues at least 2 steps in the new direction so we don't turn into a tiny curb edge
        return CityPedestrians.TryPavement(origin + dir * (stepLen * 2f), out _);
    }

    private static Vector3 SnapToCardinal(Vector3 dir)
    {
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
            return dir.x >= 0f ? Vector3.right : Vector3.left;
        return dir.z >= 0f ? Vector3.forward : Vector3.back;
    }

    private static Vector3 Turn90(Vector3 dir, bool clockwise) =>
        clockwise ? new Vector3(dir.z, 0f, -dir.x).normalized : new Vector3(-dir.z, 0f, dir.x).normalized;

    private void LateUpdate()
    {
        if (!sitting) return;
        if (!bonesCached) CacheBones();
        ApplySeatedPose();
    }

    private void CacheBones()
    {
        var anim = GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.avatar != null && a.isHuman);
        if (anim != null)
        {
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            leftUpLeg = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            rightUpLeg = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            leftLeg = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            rightLeg = anim.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            leftFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            rightFoot = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            leftArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            rightArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftForeArm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            rightForeArm = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);

            if (spine != null) restSpine = spine.localRotation;
            if (neck != null) restNeck = neck.localRotation;
            if (head != null) restHead = head.localRotation;
            if (leftUpLeg != null) restLeftUpLeg = leftUpLeg.localRotation;
            if (rightUpLeg != null) restRightUpLeg = rightUpLeg.localRotation;
            if (leftLeg != null) restLeftLeg = leftLeg.localRotation;
            if (rightLeg != null) restRightLeg = rightLeg.localRotation;
            if (leftFoot != null) restLeftFoot = leftFoot.localRotation;
            if (rightFoot != null) restRightFoot = rightFoot.localRotation;
            if (leftArm != null) restLeftArm = leftArm.localRotation;
            if (rightArm != null) restRightArm = rightArm.localRotation;
            if (leftForeArm != null) restLeftForeArm = leftForeArm.localRotation;
            if (rightForeArm != null) restRightForeArm = rightForeArm.localRotation;
        }
        bonesCached = hips != null && leftUpLeg != null && rightUpLeg != null;
    }

    private void ApplySeatedPose()
    {
        if (!bonesCached || hips == null) return;

        // In Edit Mode, reset to rest local rotations first so LateUpdate is idempotent
        if (!Application.isPlaying)
        {
            if (spine != null) spine.localRotation = restSpine;
            if (neck != null) neck.localRotation = restNeck;
            if (head != null) head.localRotation = restHead;
            if (leftUpLeg != null) leftUpLeg.localRotation = restLeftUpLeg;
            if (rightUpLeg != null) rightUpLeg.localRotation = restRightUpLeg;
            if (leftLeg != null) leftLeg.localRotation = restLeftLeg;
            if (rightLeg != null) rightLeg.localRotation = restRightLeg;
            if (leftFoot != null) leftFoot.localRotation = restLeftFoot;
            if (rightFoot != null) rightFoot.localRotation = restRightFoot;
            if (leftArm != null) leftArm.localRotation = restLeftArm;
            if (rightArm != null) rightArm.localRotation = restRightArm;
            if (leftForeArm != null) leftForeArm.localRotation = restLeftForeArm;
            if (rightForeArm != null) rightForeArm.localRotation = restRightForeArm;
        }

        Vector3 right = transform.right;
        Vector3 up = transform.up;
        Vector3 forward = transform.forward;

        // 1. Place pelvis (Hips) ON TOP of the bench planks, shifted slightly back from the front edge
        float topPlankY = seatSurfaceY > 0.01f ? seatSurfaceY : transform.position.y + 0.45f;
        Vector3 hipWorld = transform.position - forward * 0.16f;
        hipWorld.y = topPlankY + 0.06f;
        hips.position = hipWorld;

        // 2. Pitch upper legs forward (-78 deg) with knees tucked neatly parallel (no outward spread)
        if (leftUpLeg != null)
            leftUpLeg.rotation = Quaternion.AngleAxis(-78f, right) * leftUpLeg.rotation;
        if (rightUpLeg != null)
            rightUpLeg.rotation = Quaternion.AngleAxis(-78f, right) * rightUpLeg.rotation;

        // 3. Bend knees (+78 deg) so shins hang straight down outside the front edge of the bench
        if (leftLeg != null)
            leftLeg.rotation = Quaternion.AngleAxis(78f, right) * leftLeg.rotation;
        if (rightLeg != null)
            rightLeg.rotation = Quaternion.AngleAxis(78f, right) * rightLeg.rotation;

        if (leftFoot != null)
            leftFoot.rotation = Quaternion.AngleAxis(-2f, right) * leftFoot.rotation;
        if (rightFoot != null)
            rightFoot.rotation = Quaternion.AngleAxis(-2f, right) * rightFoot.rotation;

        // 4. Keep torso upright; if chatting on a bench, turn head/neck toward partner without twisting legs
        if (chatting)
        {
            Vector3 toPartner = lookAt - transform.position;
            toPartner.y = 0f;
            if (toPartner.sqrMagnitude > 0.01f)
            {
                float signedAngle = Vector3.SignedAngle(forward, toPartner.normalized, Vector3.up);
                float headYaw = Mathf.Clamp(signedAngle * 0.45f, -32f, 32f);
                if (neck != null)
                    neck.rotation = Quaternion.AngleAxis(headYaw * 0.4f, Vector3.up) * neck.rotation;
                if (head != null)
                    head.rotation = Quaternion.AngleAxis(headYaw * 0.6f, Vector3.up) * head.rotation;
            }
        }

        // 5. In Edit Mode, relax arms down from T-pose onto lap
        if (!Application.isPlaying)
        {
            if (leftArm != null)
                leftArm.rotation = Quaternion.AngleAxis(-22f, right) * Quaternion.AngleAxis(-68f, forward) * leftArm.rotation;
            if (rightArm != null)
                rightArm.rotation = Quaternion.AngleAxis(-22f, right) * Quaternion.AngleAxis(68f, forward) * rightArm.rotation;
            if (leftForeArm != null)
                leftForeArm.rotation = Quaternion.AngleAxis(-38f, right) * Quaternion.AngleAxis(18f, up) * leftForeArm.rotation;
            if (rightForeArm != null)
                rightForeArm.rotation = Quaternion.AngleAxis(-38f, right) * Quaternion.AngleAxis(-18f, up) * rightForeArm.rotation;
        }

        // Keep speech bubble cleanly above head when sitting on high benches
        if (speechBubble != null && head != null)
        {
            float headLocalY = transform.InverseTransformPoint(head.position).y;
            speechBubble.localPosition = new Vector3(0f, headLocalY + 0.42f, 0f);
        }
    }

    private void EnsureSpeechBubble()
    {
        Transform existing = transform.Find("Speech Bubble");
        if (existing != null)
        {
            speechBubble = existing;
            speechText = existing.GetComponentInChildren<TextMesh>(true);
            Transform bg = existing.Find("BubbleBg");
            if (bg != null) speechBgRenderer = bg.GetComponent<Renderer>();
        }
        else
        {
            var bubbleGo = new GameObject("Speech Bubble");
            speechBubble = bubbleGo.transform;
            speechBubble.SetParent(transform, false);

            var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgGo.name = "BubbleBg";
            bgGo.transform.SetParent(speechBubble, false);
            bgGo.transform.localPosition = new Vector3(0f, 0.02f, 0.01f);
            bgGo.transform.localScale = new Vector3(1.55f, 0.30f, 1f);
            var col = bgGo.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            speechBgRenderer = bgGo.GetComponent<Renderer>();
            if (sharedBgMaterial == null)
            {
                Shader unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                if (unlit != null)
                {
                    sharedBgMaterial = new Material(unlit);
                    sharedBgMaterial.color = new Color(0.08f, 0.11f, 0.16f, 0.92f);
                    if (sharedBgMaterial.HasProperty("_BaseColor"))
                        sharedBgMaterial.SetColor("_BaseColor", new Color(0.08f, 0.11f, 0.16f, 0.92f));
                }
            }
            if (speechBgRenderer != null && sharedBgMaterial != null)
            {
                speechBgRenderer.sharedMaterial = sharedBgMaterial;
                speechBgRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(speechBubble, false);
            textGo.transform.localPosition = Vector3.zero;
            speechText = textGo.AddComponent<TextMesh>();
            speechText.fontSize = 46;
            speechText.characterSize = 0.026f;
            speechText.anchor = TextAnchor.MiddleCenter;
            speechText.alignment = TextAlignment.Center;
            speechText.fontStyle = FontStyle.Bold;
            speechText.color = new Color(1f, 0.96f, 0.78f, 1f);
            var textRenderer = textGo.GetComponent<MeshRenderer>();
            if (textRenderer != null)
                textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        float baseHeight = 2.08f;
        if (sitting && head != null)
            baseHeight = transform.InverseTransformPoint(head.position).y + 0.42f;
        speechBubble.localPosition = new Vector3(0f, baseHeight, 0f);
        UpdateSpeechText(0);
    }

    private void UpdateSpeechText(int cycle)
    {
        if (speechText == null) return;
        int pairGroup = Mathf.Abs(index / 2) + cycle;
        string[] lines = ConversationPairs[pairGroup % ConversationPairs.Length];
        int speakerSlot = Mathf.Abs(index) % 2;
        string line = lines[speakerSlot];
        if (speechText.text != line)
        {
            speechText.text = line;
            if (speechBgRenderer != null)
            {
                float width = Mathf.Clamp(line.Length * 0.068f + 0.25f, 1.1f, 2.2f);
                speechBgRenderer.transform.localScale = new Vector3(width, 0.30f, 1f);
            }
        }
    }

    private void UpdateSpeechBubbleEditor()
    {
        if (!chatting || speechBubble == null) return;
        bool showInEditor = (Mathf.Abs(index) % 2) == 0;
        if (speechBubble.gameObject.activeSelf != showInEditor)
            speechBubble.gameObject.SetActive(showInEditor);
    }

    private void UpdateSpeechBubbleRuntime()
    {
        if (speechBubble == null) return;

        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = speechBubble.position - cam.transform.position;
            float sqrDist = toCam.sqrMagnitude;
            if (sqrDist > 34f * 34f)
            {
                if (speechBubble.gameObject.activeSelf)
                    speechBubble.gameObject.SetActive(false);
                return;
            }
            speechBubble.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        }

        float pairOffset = (index / 2) * 1.3f;
        float period = 7.6f;
        float t = (Time.time + pairOffset) % period;
        int speakerSlot = Mathf.Abs(index) % 2;
        bool isMyTurn = speakerSlot == 0 ? (t < 3.4f) : (t >= 3.8f && t < 7.2f);

        if (speechBubble.gameObject.activeSelf != isMyTurn)
            speechBubble.gameObject.SetActive(isMyTurn);

        if (isMyTurn)
        {
            int cycle = Mathf.FloorToInt((Time.time + pairOffset) / period);
            UpdateSpeechText(cycle);
            float baseHeight = sitting && head != null
                ? transform.InverseTransformPoint(head.position).y + 0.42f
                : 2.08f;
            float bob = Mathf.Sin((Time.time + index) * 4f) * 0.025f;
            speechBubble.localPosition = new Vector3(0f, baseHeight + bob, 0f);
        }
    }

    private void Face(Vector3 point, float degrees)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), degrees);
    }

    private void Play(AnimationClip clip)
    {
        if (animator == null || clip == null || current == clip) return;
        if (graph.IsValid()) graph.Destroy();
        current = clip;
        graph = PlayableGraph.Create("Pedestrian animation");
        motion = AnimationClipPlayable.Create(graph, clip);
        motion.SetApplyFootIK(!sitting);
        var output = AnimationPlayableOutput.Create(graph, "Body", animator);
        output.SetSourcePlayable(motion);
        graph.Play();
    }

    private void Loop()
    {
        if (graph.IsValid() && current != null && current != greeting &&
            motion.GetTime() >= current.length && current.length > 0f)
        {
            motion.SetTime(motion.GetTime() % current.length);
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
