using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Turns every vehicle in the scene into AI traffic. No setup needed: when a scene starts, this finds
/// all root objects whose name starts with "Vehicle" and adds a <see cref="TrafficCar"/> to each.
///
/// To tweak the settings, add a "Traffic System" object with this component to the scene yourself;
/// otherwise one is created automatically with the defaults below.
///
/// Cars find the road by looking at what is under them: tiles from the "Road ..." prefabs whose surface
/// is at street level (the raised sidewalk edge and the Sidewalk / Concrete tiles are not driveable).
/// </summary>
public class TrafficSystem : MonoBehaviour
{
    [Header("Which objects are vehicles")]
    [SerializeField] private bool autoAddToVehicles = true;
    [SerializeField] private string vehicleNamePrefix = "Vehicle";
    [Tooltip("Turn off if the cars drive backwards (model's front faces -Z).")]
    public bool modelFacesPositiveZ = true;

    [Header("Driving")]
    public float cruiseSpeed = 9f;          // m/s (~32 km/h)
    public float turnSpeed = 4.5f;          // speed through corners
    public float acceleration = 3f;
    public float braking = 9f;
    public float laneOffsetFromCurb = 3.2f; // distance from the right curb to the car's centre
    public float turnRadius = 7f;           // left turns / U-turns
    public float minTurnRadius = 4f;        // right turns hug the corner
    [Range(0f, 1f)] public float chanceStraight = 0.5f;

    [Header("Sensing")]
    public float maxCurbScan = 12f;         // how far sideways a car looks for the curb
    public float lookAhead = 9f;            // how far ahead it looks for the end of the road
    public float safeGap = 2.5f;            // gap kept to the car / person in front
    public float patience = 4f;             // seconds stuck before squeezing past
    public float senseInterval = 0.2f;

    [Header("Driver behaviour (Intelligent Driver Model)")]
    public float timeHeadway = 1.4f;        // seconds of gap kept to the car in front (varies per driver)
    public float comfortableBraking = 4f;   // normal braking; hard braking up to 'braking'
    [Range(0f, 0.5f)] public float driverVariation = 0.15f; // how much speed/gaps differ between drivers
    public bool honkAtPlayer = true;

    [Header("Traffic lights")]
    public bool trafficLights = true;
    public float greenTime = 12f;
    public float yellowTime = 3f;
    public float allRedTime = 2f;
    [SerializeField] private string[] intersectionPrefixes = { "Road Intersection", "Road T_Intersection" };
    [SerializeField] private string signalPrefix = "Props_Traffic Signal";

    [Header("Road detection")]
    [SerializeField] private string roadPrefix = "Road";
    [SerializeField] private string[] notDriveable = { "Sidewalk", "Concrete" };
    [SerializeField] private float maxRoadSurfaceHeight = 0.12f; // asphalt is at 0, curbs/sidewalks at ~0.2

    public static TrafficSystem Instance { get; private set; }

    private readonly Dictionary<Collider, bool> roadColliderCache = new Dictionary<Collider, bool>();
    private readonly RaycastHit[] hits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<TrafficSystem>() == null)
            new GameObject("Traffic System").AddComponent<TrafficSystem>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public List<TrafficIntersection> Intersections { get; } = new List<TrafficIntersection>();

    private void Start()
    {
        var roots = new List<GameObject>();
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
        }

        if (trafficLights) SetupTrafficLights(roots);

        if (!autoAddToVehicles) return;
        int count = 0;
        foreach (GameObject root in roots)
        {
            if (!root.activeInHierarchy || !root.name.StartsWith(vehicleNamePrefix)) continue;
            if (root.GetComponent<PlayerVehicleController>() != null) continue;   // the player drives this one
            if (root.GetComponentInChildren<MeshRenderer>() == null) continue;
            if (root.GetComponent<TrafficCar>() == null) root.AddComponent<TrafficCar>();
            count++;
        }
        if (count > 0)
            Debug.Log($"TrafficSystem: {count} vehicles are now driving, {Intersections.Count} intersections have traffic lights.");
    }

    private void SetupTrafficLights(List<GameObject> roots)
    {
        foreach (GameObject root in roots)
        {
            if (!root.activeInHierarchy) continue;
            foreach (string prefix in intersectionPrefixes)
            {
                if (!root.name.StartsWith(prefix)) continue;
                var ti = root.GetComponent<TrafficIntersection>();
                if (ti == null) ti = root.AddComponent<TrafficIntersection>();
                ti.greenTime = greenTime;
                ti.yellowTime = yellowTime;
                ti.allRedTime = allRedTime;
                Intersections.Add(ti);
                break;
            }
        }

        // Hook each signal prop to the nearest intersection. The way it faces tells which traffic it controls.
        foreach (GameObject root in roots)
        {
            if (!root.activeInHierarchy || !root.name.StartsWith(signalPrefix)) continue;
            TrafficIntersection nearest = null;
            float best = 16f;
            foreach (var ti in Intersections)
            {
                Vector3 d = ti.Center - root.transform.position;
                float dist = new Vector2(d.x, d.z).magnitude;
                if (dist < best) { best = dist; nearest = ti; }
            }
            if (nearest == null) continue;

            Vector3 f = root.transform.forward;
            bool northSouth = Mathf.Abs(f.z) >= Mathf.Abs(f.x);
            var lamps = root.GetComponent<TrafficSignalLamps>();
            if (lamps == null) lamps = root.AddComponent<TrafficSignalLamps>();
            lamps.Init(northSouth);
            nearest.AddSignal(lamps);
        }
    }

    /// <summary>
    /// The next intersection in front of a car driving along 'heading' from 'pos' (within 'range' metres),
    /// and the distance from 'pos' to its stop line. Returns null if there is none.
    /// </summary>
    public TrafficIntersection IntersectionAhead(Vector3 pos, Vector3 heading, float range, out float distToStopLine)
    {
        distToStopLine = 0f;
        TrafficIntersection result = null;
        float best = float.MaxValue;
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        foreach (var ti in Intersections)
        {
            Vector3 rel = ti.Center - pos;
            float along = Vector3.Dot(rel, heading);
            float lateral = Mathf.Abs(Vector3.Dot(rel, right));
            if (along <= 0f || along > range || lateral > ti.stopLineDistance) continue;
            if (along < best) { best = along; result = ti; distToStopLine = along - ti.stopLineDistance; }
        }
        return result;
    }

    // ---------------------------------------------------------------- shared visuals / audio
    private static readonly Dictionary<(Color, bool), Material> lampMaterials = new Dictionary<(Color, bool), Material>();

    /// <summary>Shared glowing (on) or dim (off) material for lamps.</summary>
    public static Material LampMaterial(Color color, bool on)
    {
        if (lampMaterials.TryGetValue((color, on), out var mat) && mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        mat = new Material(shader) { name = $"Lamp {color} {(on ? "on" : "off")}" };
        Color c = on ? color * 2f : color * 0.18f;   // >1 glows with bloom
        c.a = 1f;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        lampMaterials[(color, on)] = mat;
        return mat;
    }

    /// <summary>Creates a small sphere lamp (no collider) as a child of parent, sized in world metres.</summary>
    public static Renderer CreateLamp(Transform parent, Vector3 localPos, float worldDiameter)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Lamp";
        var col = go.GetComponent<Collider>();
        col.enabled = false;
        Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Vector3 s = parent.lossyScale;
        go.transform.localScale = new Vector3(worldDiameter / Mathf.Max(Mathf.Abs(s.x), 1e-4f),
                                              worldDiameter / Mathf.Max(Mathf.Abs(s.y), 1e-4f),
                                              worldDiameter / Mathf.Max(Mathf.Abs(s.z), 1e-4f));
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return r;
    }

    private static AudioClip hornClip;

    /// <summary>A short two-tone car horn, generated in code (no audio file needed).</summary>
    public static AudioClip HornClip
    {
        get
        {
            if (hornClip != null) return hornClip;
            const int rate = 44100;
            const float length = 0.45f;
            int n = (int)(rate * length);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((length - t) / 0.06f);
                float a = Mathf.Sin(2f * Mathf.PI * 392f * t), b = Mathf.Sin(2f * Mathf.PI * 494f * t);
                // Slightly clipped sum = brassy horn sound.
                data[i] = Mathf.Clamp((a + b) * 0.9f, -0.8f, 0.8f) * env * 0.6f;
            }
            hornClip = AudioClip.Create("Car Horn", n, 1, rate, false);
            hornClip.SetData(data, 0);
            return hornClip;
        }
    }

    /// <summary>True if the point (x/z) is on driveable road surface.</summary>
    public bool IsRoad(Vector3 point)
    {
        Vector3 origin = new Vector3(point.x, point.y + 5f, point.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 12f, ~0, QueryTriggerInteraction.Ignore);

        // Use the highest ground-level surface (ignore vehicles, the player, and anything tall like tree tops).
        float bestY = float.NegativeInfinity;
        Collider best = null;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.collider.attachedRigidbody != null) continue;     // cars, player, physics props
            if (h.point.y > point.y + 0.6f) continue;               // signs, canopies, roofs...
            if (h.point.y > bestY) { bestY = h.point.y; best = h.collider; }
        }
        if (best == null) return false;
        if (bestY > point.y + maxRoadSurfaceHeight) return false;  // raised curb / sidewalk
        return IsRoadCollider(best);
    }

    private bool IsRoadCollider(Collider c)
    {
        if (roadColliderCache.TryGetValue(c, out bool isRoad)) return isRoad;

        string name = c.transform.root.name;
        isRoad = name.StartsWith(roadPrefix);
        if (isRoad)
            foreach (string bad in notDriveable)
                if (name.Contains(bad)) { isRoad = false; break; }

        roadColliderCache[c] = isRoad;
        return isRoad;
    }
}
