using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor menu that wires the driving game into the open scene (Tools > City Driving).
/// Safe to run again: anything that already exists is reused, only missing parts are created.
/// It never edits or removes the city environment objects.
/// </summary>
public static class CityDrivingSetup
{
    private const string Menu = "Tools/City Driving/";
    private const string CarPrefabPath =
        "Assets/SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Separated Wheels/Vehicle_Car_color01_separate.prefab";
    private const string MaterialFolder = "Assets/Game/Materials";
    private const string PlayerName = "Player Vehicle";

    // Wheel tuning used when creating the player car (all editable afterwards on the WheelColliders).
    private const float SuspensionDistance = 0.2f;
    private const float CarMass = 1200f;

    // =====================================================================================
    // Main setup
    // =====================================================================================

    [MenuItem(Menu + "Set Up Game Scene", priority = 0)]
    private static void SetUpGameScene()
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Up City Driving");
        var log = new List<string>();

        var player = Find<PlayerVehicleController>();
        if (player == null)
        {
            player = CreatePlayerVehicle(log);
            if (player == null) return;
        }
        else log.Add($"Player vehicle '{player.name}' already exists - kept as is.");

        DisableOnFootCharacters(log);
        SetUpCamera(player, log);

        // Game systems: GameManager + CountdownTimer on one object.
        var game = Find<GameManager>();
        if (game == null)
        {
            game = Undo.AddComponent<GameManager>(NewObject("Game Systems"));
            log.Add("Created 'Game Systems' (GameManager).");
        }
        var timer = Find<CountdownTimer>();
        if (timer == null)
        {
            timer = Undo.AddComponent<CountdownTimer>(game.gameObject);
            log.Add("Added CountdownTimer to 'Game Systems'.");
        }

        // Route.
        var route = Find<CheckpointManager>();
        if (route == null)
        {
            route = Undo.AddComponent<CheckpointManager>(NewObject("Route"));
            log.Add("Created 'Route' (CheckpointManager).");
        }
        if (route.GetComponentsInChildren<Checkpoint>(true).Length == 0)
            CreateDefaultRoute(route.transform, player.transform.position, 4, log);

        var arrow = Find<NavigationArrow>();
        if (arrow == null)
        {
            arrow = CreateNavigationArrow();
            log.Add("Created 'Navigation Arrow'.");
        }

        var ui = Find<UIManager>();
        if (ui == null)
        {
            ui = CreateHud();
            log.Add("Created 'HUD' canvas (UIManager).");
        }

        // Wire everything together.
        var collisions = player.GetComponent<VehicleCollisionHandler>();
        Wire(game, ("timer", timer), ("checkpoints", route), ("player", player), ("playerCollisions", collisions));
        Wire(arrow, ("checkpoints", route), ("follow", player.transform));
        Wire(ui, ("game", game), ("timer", timer), ("checkpoints", route), ("navigation", arrow), ("vehicle", player));

        AddSceneToBuild(log);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = player.gameObject;
        Undo.CollapseUndoOperations(undoGroup);

        Debug.Log("<b>City Driving setup done.</b>\n- " + string.Join("\n- ", log) +
                  "\nNext: check the player car's position/rotation, then arrange the checkpoints under 'Route' along your roads.");
    }

    // =====================================================================================
    // Extra tools
    // =====================================================================================

    [MenuItem(Menu + "Add Checkpoint At Scene View", priority = 20)]
    private static void AddCheckpointAtSceneView()
    {
        var route = Find<CheckpointManager>();
        if (route == null)
        {
            Debug.LogError("No CheckpointManager in the scene. Run Tools > City Driving > Set Up Game Scene first.");
            return;
        }
        var view = SceneView.lastActiveSceneView;
        Vector3 pos = view != null ? GroundPoint(view.pivot) : Vector3.zero;

        int number = route.GetComponentsInChildren<Checkpoint>(true).Length + 1;
        var cp = CreateCheckpoint(route.transform, pos, number);

        // If the route uses an explicit list instead of its children, append there too.
        var so = new SerializedObject(route);
        var list = so.FindProperty("checkpoints");
        if (list.arraySize > 0)
        {
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = cp;
            so.ApplyModifiedProperties();
        }

        Selection.activeGameObject = cp.gameObject;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    [MenuItem(Menu + "Make Selected Vehicles Stationary", priority = 40)]
    private static void MakeSelectedStationary() => SetSelectedTrafficBehaviour(TrafficBehaviour.Stationary);

    [MenuItem(Menu + "Make Selected Vehicles Drive", priority = 41)]
    private static void MakeSelectedDrive() => SetSelectedTrafficBehaviour(TrafficBehaviour.Drive);

    [MenuItem(Menu + "Make Selected Vehicles Stationary", true)]
    [MenuItem(Menu + "Make Selected Vehicles Drive", true)]
    private static bool HasSelection() => Selection.gameObjects.Length > 0;

    private static void SetSelectedTrafficBehaviour(TrafficBehaviour behaviour)
    {
        int count = 0;
        foreach (var go in Selection.gameObjects)
        {
            GameObject root = go.transform.root.gameObject;
            if (root.GetComponent<PlayerVehicleController>() != null) continue;
            var settings = root.GetComponent<TrafficVehicleSettings>();
            if (settings == null) settings = Undo.AddComponent<TrafficVehicleSettings>(root);
            Undo.RecordObject(settings, "Set Traffic Behaviour");
            settings.Behaviour = behaviour;
            EditorUtility.SetDirty(settings);
            count++;
        }
        Debug.Log($"Set {count} vehicle(s) to {behaviour}.");
    }

    // =====================================================================================
    // Player vehicle
    // =====================================================================================

    private static PlayerVehicleController CreatePlayerVehicle(List<string> log)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"Car prefab not found at '{CarPrefabPath}'. Update CarPrefabPath in CityDrivingSetup.cs.");
            return null;
        }

        var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, SceneManager.GetActiveScene());
        Undo.RegisterCreatedObjectUndo(car, "Create Player Vehicle");
        PrefabUtility.UnpackPrefabInstance(car, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        car.name = PlayerName;
        car.tag = "Player";
        car.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        car.transform.localScale = Vector3.one;

        // The asset's non-convex mesh colliders can't be used on a moving Rigidbody.
        foreach (var mc in car.GetComponentsInChildren<MeshCollider>(true)) Object.DestroyImmediate(mc);

        Transform fl = FindDeep(car.transform, "Wheel_FL"), fr = FindDeep(car.transform, "Wheel_FR");
        Transform rl = FindDeep(car.transform, "Wheel_RL"), rr = FindDeep(car.transform, "Wheel_RR");
        if (fl == null || fr == null || rl == null || rr == null)
        {
            Debug.LogError("Player car prefab needs children named Wheel_FL, Wheel_FR, Wheel_RL and Wheel_RR.");
            Object.DestroyImmediate(car);
            return null;
        }
        var wheels = new[] { fl, fr, rl, rr };

        // Body collider: a box around the body, starting at wheel-centre height so the wheels touch the road.
        Bounds body = LocalBounds(car.transform, car.GetComponentsInChildren<MeshFilter>()
            .Where(mf => !wheels.Any(w => mf.transform.IsChildOf(w))));
        float wheelRadius = LocalBounds(car.transform, fl.GetComponentsInChildren<MeshFilter>()).extents.y;
        float wheelY = car.transform.InverseTransformPoint(fl.position).y;
        float bottom = Mathf.Max(body.min.y, wheelY);
        var box = car.AddComponent<BoxCollider>();
        box.center = new Vector3(body.center.x, (bottom + body.max.y) * 0.5f, body.center.z);
        box.size = new Vector3(body.size.x, body.max.y - bottom, body.size.z);

        var rb = car.AddComponent<Rigidbody>();
        rb.mass = CarMass;
        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.5f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var holder = new GameObject("Wheel Colliders").transform;
        holder.SetParent(car.transform, false);
        var front = new PlayerVehicleController.Axle
        {
            leftWheel = CreateWheelCollider(holder, "FL", fl, wheelRadius),
            rightWheel = CreateWheelCollider(holder, "FR", fr, wheelRadius),
            leftVisual = fl, rightVisual = fr,
            steering = true,
        };
        var rear = new PlayerVehicleController.Axle
        {
            leftWheel = CreateWheelCollider(holder, "RL", rl, wheelRadius),
            rightWheel = CreateWheelCollider(holder, "RR", rr, wheelRadius),
            leftVisual = rl, rightVisual = rr,
            motor = true, handbrake = true,
        };

        var controller = car.AddComponent<PlayerVehicleController>();   // also adds VehicleInput
        controller.SetAxles(new[] { front, rear });
        car.AddComponent<VehicleCollisionHandler>();
        EditorUtility.SetDirty(controller);

        PlaceOnRoad(car.transform, log);
        log.Add($"Created '{PlayerName}' from {System.IO.Path.GetFileNameWithoutExtension(CarPrefabPath)} " +
                "(Rigidbody, BoxCollider, 4 WheelColliders, controller, collision handler).");
        return controller;
    }

    private static WheelCollider CreateWheelCollider(Transform holder, string name, Transform visual, float radius)
    {
        var go = new GameObject("Wheel Collider " + name);
        go.transform.SetParent(holder, false);
        Transform car = holder.parent;
        // Lift by half the suspension travel so the wheel rests where the mesh is.
        go.transform.localPosition = car.InverseTransformPoint(visual.position) + Vector3.up * (SuspensionDistance * 0.5f);

        var wc = go.AddComponent<WheelCollider>();
        wc.mass = 20f;
        wc.radius = radius > 0.05f ? radius : 0.45f;
        wc.wheelDampingRate = 0.25f;
        wc.suspensionDistance = SuspensionDistance;
        wc.forceAppPointDistance = 0.1f;

        JointSpring spring = wc.suspensionSpring;
        spring.spring = 35000f;
        spring.damper = 4500f;
        spring.targetPosition = 0.5f;
        wc.suspensionSpring = spring;

        WheelFrictionCurve forward = wc.forwardFriction;
        forward.stiffness = 1.5f;
        wc.forwardFriction = forward;
        WheelFrictionCurve side = wc.sidewaysFriction;
        side.stiffness = 2f;
        wc.sidewaysFriction = side;
        return wc;
    }

    /// Puts the car on the road tile nearest to the on-foot character (or the Scene view focus).
    private static void PlaceOnRoad(Transform car, List<string> log)
    {
        var character = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault();
        Vector3 reference = character != null ? character.transform.position
            : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;

        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        var vehicles = roots.Where(r => r.name.StartsWith("Vehicle") && r.transform != car).ToList();

        Transform best = null;
        float bestDist = float.MaxValue;
        Vector3 bestCenter = Vector3.zero;
        foreach (var r in roots)
        {
            if (!r.activeInHierarchy || !r.name.StartsWith("Road Lane")) continue;
            var rend = r.GetComponentInChildren<Renderer>();
            if (rend == null) continue;
            Vector3 c = rend.bounds.center;
            c.y = r.transform.position.y;
            if (vehicles.Any(v => Vector3.Distance(v.transform.position, c) < 7f)) continue;   // spot taken
            float d = Vector3.Distance(c, reference);
            if (d < bestDist) { bestDist = d; best = r.transform; bestCenter = c; }
        }

        if (best == null)
        {
            car.position = reference + Vector3.up * 0.5f;
            log.Add("No free 'Road Lane' tile found: placed the car at the reference point. Move it onto a road.");
            return;
        }
        car.SetPositionAndRotation(bestCenter + Vector3.up * 0.5f, Quaternion.Euler(0f, best.eulerAngles.y, 0f));
        log.Add($"Placed the car on '{best.name}'. Rotate it (Y) if it doesn't face along the road.");
    }

    private static void DisableOnFootCharacters(List<string> log)
    {
        // The on-foot PlayerController also drives the camera and locks the cursor, which would fight the car.
        foreach (var pc in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            Undo.RecordObject(pc.gameObject, "Disable On-Foot Character");
            pc.gameObject.SetActive(false);
            log.Add($"Deactivated on-foot character '{pc.name}' (it also controls the camera). Re-enable it any time; nothing was deleted.");
        }
    }

    private static void SetUpCamera(PlayerVehicleController player, List<string> log)
    {
        Camera cam = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            var go = NewObject("Main Camera");
            go.tag = "MainCamera";
            cam = Undo.AddComponent<Camera>(go);
            Undo.AddComponent<AudioListener>(go);
            log.Add("Created 'Main Camera'.");
        }
        var follow = cam.GetComponent<VehicleCameraFollow>();
        if (follow == null)
        {
            follow = Undo.AddComponent<VehicleCameraFollow>(cam.gameObject);
            log.Add($"Added VehicleCameraFollow to '{cam.name}'.");
        }
        Wire(follow, ("target", player.transform));
    }

    // =====================================================================================
    // Route / checkpoints
    // =====================================================================================

    /// Placeholder route through the nearest intersections (they are always on the road).
    private static void CreateDefaultRoute(Transform route, Vector3 start, int count, List<string> log)
    {
        var intersections = SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(r => r.activeInHierarchy &&
                        (r.name.StartsWith("Road Intersection") || r.name.StartsWith("Road T_Intersection")))
            .Select(r =>
            {
                var rend = r.GetComponentInChildren<Renderer>();
                Vector3 c = rend != null ? rend.bounds.center : r.transform.position;
                c.y = r.transform.position.y;
                return c;
            })
            .Where(c => Vector3.Distance(c, start) > 20f)
            .ToList();

        if (intersections.Count == 0)
        {
            log.Add("No road intersections found for a default route. Add checkpoints with 'Add Checkpoint At Scene View'.");
            return;
        }

        Vector3 from = start;
        for (int i = 1; i <= count && intersections.Count > 0; i++)
        {
            Vector3 next = intersections.OrderBy(c => Vector3.Distance(c, from)).First();
            intersections.Remove(next);
            CreateCheckpoint(route, next, i);
            from = next;
        }
        log.Add($"Created a placeholder route of {route.childCount} checkpoints at nearby intersections. Rearrange it as you like.");
    }

    private static Checkpoint CreateCheckpoint(Transform route, Vector3 position, int number)
    {
        var go = new GameObject($"Checkpoint {number:00}");
        Undo.RegisterCreatedObjectUndo(go, "Create Checkpoint");
        go.transform.SetParent(route, false);
        go.transform.position = position;

        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(12f, 6f, 12f);
        box.center = new Vector3(0f, 3f, 0f);

        // Marker: a light beam and a ground disc, shown only while this checkpoint is the target.
        var marker = new GameObject("Marker");
        marker.transform.SetParent(go.transform, false);
        Material mat = GetOrCreateMaterial("Checkpoint Marker", new Color(0.1f, 0.85f, 1f));
        AddVisualPrimitive(PrimitiveType.Cylinder, marker.transform, new Vector3(0f, 6f, 0f), new Vector3(0.6f, 6f, 0.6f), Quaternion.identity, mat);
        AddVisualPrimitive(PrimitiveType.Cylinder, marker.transform, new Vector3(0f, 0.06f, 0f), new Vector3(9f, 0.02f, 9f), Quaternion.identity, mat);

        var cp = go.AddComponent<Checkpoint>();
        Wire(cp, ("activeVisual", marker));
        return cp;
    }

    // =====================================================================================
    // Navigation arrow & HUD
    // =====================================================================================

    private static NavigationArrow CreateNavigationArrow()
    {
        var go = NewObject("Navigation Arrow");
        var visual = new GameObject("Visual");
        visual.transform.SetParent(go.transform, false);

        Material mat = GetOrCreateMaterial("Navigation Arrow", new Color(1f, 0.8f, 0.1f));
        // Shaft + a diamond head = arrow pointing along +Z.
        AddVisualPrimitive(PrimitiveType.Cube, visual.transform, new Vector3(0f, 0f, -0.35f), new Vector3(0.35f, 0.15f, 1.2f), Quaternion.identity, mat);
        AddVisualPrimitive(PrimitiveType.Cube, visual.transform, new Vector3(0f, 0f, 0.4f), new Vector3(0.9f, 0.15f, 0.9f), Quaternion.Euler(0f, 45f, 0f), mat);

        var arrow = Undo.AddComponent<NavigationArrow>(go);
        Wire(arrow, ("visual", visual));
        return arrow;
    }

    private static UIManager CreateHud()
    {
        var go = NewObject("HUD");
        go.layer = LayerMask.NameToLayer("UI");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Text timer = CreateText(go.transform, "Timer", new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(400f, 90f), 64, TextAnchor.UpperCenter);
        Text checkpoint = CreateText(go.transform, "Checkpoint", new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(700f, 50f), 34, TextAnchor.UpperLeft);
        Text distance = CreateText(go.transform, "Distance", new Vector2(0f, 1f), new Vector2(30f, -80f), new Vector2(700f, 40f), 28, TextAnchor.UpperLeft);
        Text speed = CreateText(go.transform, "Speed", new Vector2(1f, 0f), new Vector2(-30f, 30f), new Vector2(400f, 70f), 48, TextAnchor.LowerRight);
        Text message = CreateText(go.transform, "Message", new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1400f, 120f), 56, TextAnchor.MiddleCenter);

        // End screen.
        var panel = new GameObject("End Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(go.transform, false);
        var rt = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
        Text title = CreateText(panel.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1400f, 120f), 84, TextAnchor.MiddleCenter);
        Text body = CreateText(panel.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(1400f, 200f), 36, TextAnchor.MiddleCenter);
        panel.SetActive(false);

        var ui = go.AddComponent<UIManager>();
        Wire(ui, ("timerText", timer), ("speedText", speed), ("checkpointText", checkpoint), ("distanceText", distance),
             ("messageText", message), ("endPanel", panel), ("endTitleText", title), ("endBodyText", body));
        return ui;
    }

    private static Text CreateText(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size,
                                   int fontSize, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Outline));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;

        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "";

        var outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static T Find<T>() where T : Object =>
        Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);

    private static GameObject NewObject(string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        return go;
    }

    /// Assigns serialized object-reference fields by name (with undo support).
    private static void Wire(Object target, params (string field, Object value)[] fields)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in fields)
        {
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"{target.GetType().Name} has no field '{field}'."); continue; }
            prop.objectReferenceValue = value;
        }
        so.ApplyModifiedProperties();
    }

    private static void AddVisualPrimitive(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale,
                                           Quaternion rotation, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.DestroyImmediate(go.GetComponent<Collider>());   // purely visual
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = rotation;
        go.transform.localScale = scale;
        var rend = go.GetComponent<MeshRenderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Material GetOrCreateMaterial(string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder("Assets/Game")) AssetDatabase.CreateFolder("Assets", "Game");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Game", "Materials");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", color);
        mat.SetColor("_Color", color);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * 1.5f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    /// Bounds of the given meshes in the root's local space.
    private static Bounds LocalBounds(Transform root, IEnumerable<MeshFilter> meshes)
    {
        bool any = false;
        var result = new Bounds();
        foreach (var mf in meshes)
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 local = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(local, Vector3.zero); any = true; }
                else result.Encapsulate(local);
            }
        }
        return result;
    }

    /// Lowest collider surface under a point (so roofs and tree tops are skipped).
    private static Vector3 GroundPoint(Vector3 around)
    {
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(around + Vector3.up * 100f, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0) return new Vector3(around.x, 0f, around.z);
        return hits.OrderBy(h => h.point.y).First().point;
    }

    private static void AddSceneToBuild(List<string> log)
    {
        string path = SceneManager.GetActiveScene().path;
        if (string.IsNullOrEmpty(path)) return;
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        log.Add($"Added '{path}' to the build scene list (needed for restart).");
    }
}
