using UnityEngine;

/// <summary>
/// Marks the player's spawn spot: a glowing, pulsing circle on the ground and a bobbing arrow sign
/// ("SPAWN") pointing down at it. No setup needed - when a scene starts it is placed where the player's
/// vehicle (the scooter) starts. To place it yourself, add this component to an object in the scene and
/// turn off "Follow Player Start".
///
/// The arrow fades out while the player is standing on the spot, so it doesn't sit over the rider's head.
/// </summary>
[DisallowMultipleComponent]
public class SpawnPointMarker : MonoBehaviour
{
    [Tooltip("Move to where the player vehicle is when the scene starts.")]
    [SerializeField] private bool followPlayerStart = true;
    [SerializeField] private Color color = new Color(0.1f, 0.85f, 1f);
    [SerializeField] private float radius = 1.8f;
    [SerializeField] private float lineWidth = 0.12f;
    [SerializeField] private string label = "SPAWN";
    [Tooltip("Height of the arrow tip above the ground (m).")]
    [SerializeField] private float arrowHeight = 2.6f;
    [Tooltip("The arrow hides while the player is closer than this (m). 0 = always show.")]
    [SerializeField] private float hideArrowWithin = 4f;

    private Transform ring, arrow;
    private TextMesh text;
    private Renderer[] arrowRenderers;
    private float arrowAlpha = 1f;
    private float findTimer;
    private Transform player, walker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<SpawnPointMarker>() != null) return;
        // Only in scenes with a player (vehicle or the scooter's rider).
        if (FindFirstObjectByType<PlayerVehicle>() == null && FindFirstObjectByType<ScooterRiderPose>() == null) return;
        var marker = new GameObject("Spawn Point Marker");
        // Keep the Hierarchy tidy: inside the "Game Systems" folder when the scene has one.
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Game Systems" && HierarchyGroup.IsGroup(root.transform))
            {
                marker.transform.SetParent(root.transform, false);
                break;
            }
        marker.AddComponent<SpawnPointMarker>();
    }

    private void Start()
    {
        if (followPlayerStart)
        {
            // Start runs after every Awake, so the scooter has already settled onto the road.
            Transform start = FindPlayerVehicle();
            if (start != null)
                transform.SetPositionAndRotation(start.position, Quaternion.Euler(0f, start.eulerAngles.y, 0f));
        }
        if (Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 6f, ~0,
                            QueryTriggerInteraction.Ignore) && hit.rigidbody == null)
            transform.position = hit.point;

        BuildRing();
        BuildArrow();
    }

    private Transform FindPlayerVehicle()
    {
        var vehicle = FindFirstObjectByType<PlayerVehicle>();
        if (vehicle != null) return vehicle.transform;
        var pose = FindFirstObjectByType<ScooterRiderPose>();
        return pose != null ? HierarchyGroup.PieceRoot(pose.transform) : null;
    }

    // ------------------------------------------------------------------ visuals

    private void BuildRing()
    {
        ring = new GameObject("Ring").transform;
        ring.SetParent(transform, false);
        ring.localPosition = Vector3.up * 0.04f;

        // Circle in the ring's X/Y plane, facing its Z axis; the ring is tipped over so that plane lies on the ground.
        ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var line = ring.gameObject.AddComponent<LineRenderer>();
        const int segments = 64;
        line.useWorldSpace = false;
        line.loop = true;
        line.alignment = LineAlignment.TransformZ;
        line.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
        }
        line.widthMultiplier = lineWidth;
        line.sharedMaterial = TrafficSystem.LampMaterial(color, true);
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
    }

    private void BuildArrow()
    {
        arrow = new GameObject("Arrow").transform;
        arrow.SetParent(transform, false);
        arrow.localPosition = Vector3.up * arrowHeight;

        // Down-pointing arrow head (a four-sided pyramid) and a shaft above it.
        var head = new GameObject("Head");
        head.transform.SetParent(arrow, false);
        head.AddComponent<MeshFilter>().sharedMesh = PyramidMesh(0.45f, 0.6f);
        var headRenderer = head.AddComponent<MeshRenderer>();

        var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaft.name = "Shaft";
        Destroy(shaft.GetComponent<Collider>());
        shaft.transform.SetParent(arrow, false);
        shaft.transform.localPosition = Vector3.up * 0.95f;
        shaft.transform.localScale = new Vector3(0.22f, 0.7f, 0.22f);

        Material mat = TrafficSystem.LampMaterial(color, true);
        headRenderer.sharedMaterial = mat;
        shaft.GetComponent<Renderer>().sharedMaterial = mat;

        // Label above the arrow, always turned towards the camera.
        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(arrow, false);
        labelGo.transform.localPosition = Vector3.up * 1.65f;
        text = labelGo.AddComponent<TextMesh>();
        text.text = label;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = 0.05f;
        text.fontStyle = FontStyle.Bold;
        text.color = color;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            text.font = font;
            labelGo.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        arrowRenderers = arrow.GetComponentsInChildren<Renderer>();
        foreach (var r in arrowRenderers) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// Pyramid with its tip at the origin pointing down, base 'size' wide at 'height' above it.
    private static Mesh PyramidMesh(float size, float height)
    {
        float s = size * 0.5f;
        Vector3 tip = Vector3.zero;
        Vector3 a = new Vector3(-s, height, -s), b = new Vector3(s, height, -s),
                c = new Vector3(s, height, s), d = new Vector3(-s, height, s);
        var mesh = new Mesh { name = "Spawn Arrow Head" };
        // Separate vertices per face for flat shading.
        // Wound so every face points outwards (sides out and down, base up).
        mesh.vertices = new[] { tip, a, b, tip, b, c, tip, c, d, tip, d, a, a, c, b, a, d, c };
        var tris = new int[18];
        for (int i = 0; i < 18; i++) tris[i] = i;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ------------------------------------------------------------------ animation

    private void Update()
    {
        if (ring == null || arrow == null) return;
        float t = Time.time;

        // Ring pulses gently.
        float pulse = 1f + 0.06f * Mathf.Sin(t * 3f);
        ring.localScale = new Vector3(pulse, pulse, 1f);

        // Arrow bobs and spins slowly.
        arrow.localPosition = Vector3.up * (arrowHeight + 0.25f * Mathf.Sin(t * 2.2f));
        arrow.localRotation = Quaternion.Euler(0f, t * 60f, 0f);

        var cam = Camera.main;
        if (cam != null && text != null)
            text.transform.rotation = Quaternion.LookRotation(text.transform.position - cam.transform.position, Vector3.up);

        // Hide the arrow while the player is right on the spot.
        if (hideArrowWithin > 0f)
        {
            float target = PlayerDistance() < hideArrowWithin ? 0f : 1f;
            arrowAlpha = Mathf.MoveTowards(arrowAlpha, target, Time.deltaTime * 3f);
            bool show = arrowAlpha > 0.01f;
            foreach (var r in arrowRenderers) if (r != null) r.enabled = show;
            arrow.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, arrowAlpha);
        }
    }

    /// Closest distance (flat) from the spot to the player vehicle or the courier on foot.
    private float PlayerDistance()
    {
        findTimer -= Time.deltaTime;
        if (findTimer <= 0f)
        {
            findTimer = 0.5f;
            var vehicle = FindFirstObjectByType<PlayerVehicle>();
            player = vehicle != null ? vehicle.transform : null;
            var onFoot = FindFirstObjectByType<PlayerController>();
            walker = onFoot != null ? onFoot.transform : null;
        }
        float best = float.MaxValue;
        foreach (var p in new[] { player, walker })
        {
            if (p == null) continue;
            Vector3 d = p.position - transform.position;
            d.y = 0f;
            best = Mathf.Min(best, d.magnitude);
        }
        return best;
    }
}
