using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MapBoundaryManager))]
public class MapBoundaryEditor : Editor
{
    private float customMargin = 2f;

    public override void OnInspectorGUI()
    {
        MapBoundaryManager manager = (MapBoundaryManager)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Map Boundary Manager: Generates invisible BoxCollider walls and floor protection around the playable area to prevent players and vehicles from falling off or escaping.",
            MessageType.Info
        );
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(manager, "Modify Map Boundary Settings");
            manager.RebuildWalls();
            EditorUtility.SetDirty(manager);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Boundary Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        customMargin = EditorGUILayout.FloatField("Auto-Fit Margin", customMargin);
        if (GUILayout.Button("Auto-Fit to Scene Meshes", GUILayout.Height(24)))
        {
            Undo.RecordObject(manager, "Auto-Fit Map Boundaries");
            bool success = manager.AutoCalculateMapBounds(customMargin);
            if (success)
            {
                EditorUtility.SetDirty(manager);
                SceneView.RepaintAll();
                Debug.Log($"[MapBoundary] Successfully auto-fit boundaries to scene: Center={manager.center}, Size={manager.mapSize}");
            }
            else
            {
                EditorUtility.DisplayDialog("Map Boundary", "No active mesh renderers found in scene to calculate bounds.", "OK");
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild / Refresh Walls", GUILayout.Height(28)))
        {
            Undo.RecordObject(manager, "Rebuild Boundary Walls");
            manager.RebuildWalls();
            EditorUtility.SetDirty(manager);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Clear Walls", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("Clear Walls", "Are you sure you want to delete the boundary colliders?", "Yes", "Cancel"))
            {
                Undo.RecordObject(manager, "Clear Boundary Walls");
                manager.ClearWalls();
                EditorUtility.SetDirty(manager);
                SceneView.RepaintAll();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Player / Scooter Quick Setup", EditorStyles.boldLabel);

        if (GUILayout.Button("Configure Player / Scooter Physics (SitPoint)", GUILayout.Height(30)))
        {
            ConfigurePlayerScooterPhysics();
        }
    }

    [MenuItem("Tools/Map Boundaries/Setup Invisible Walls")]
    public static void SetupMapBoundariesMenuItem()
    {
        MapBoundaryManager manager = FindFirstObjectByType<MapBoundaryManager>();
        GameObject rootGo;

        if (manager == null)
        {
            rootGo = new GameObject("[MapBoundaries]");
            Undo.RegisterCreatedObjectUndo(rootGo, "Create Map Boundaries");
            manager = rootGo.AddComponent<MapBoundaryManager>();
        }
        else
        {
            rootGo = manager.gameObject;
        }

        Undo.RecordObject(manager, "Setup Map Boundaries");
        bool fitted = manager.AutoCalculateMapBounds(2f);
        if (!fitted)
        {
            manager.RebuildWalls();
        }

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(rootGo);
        Selection.activeGameObject = rootGo;
        SceneView.RepaintAll();

        Debug.Log("[MapBoundary] Invisible walls & floor setup complete. Selected [MapBoundaries] in Hierarchy.");
    }

    [MenuItem("Tools/Map Boundaries/Setup Scooter Physics (SitPoint)")]
    public static void ConfigurePlayerScooterPhysics()
    {
        // Search for SitPoint in scene
        GameObject scooter = GameObject.Find("SitPoint (1)");
        if (scooter == null) scooter = GameObject.Find("SitPoint");
        if (scooter == null)
        {
            ScooterRiderPose pose = FindFirstObjectByType<ScooterRiderPose>();
            if (pose != null) scooter = pose.gameObject;
        }

        if (scooter == null)
        {
            EditorUtility.DisplayDialog("Player Setup", "Could not find 'SitPoint (1)' or 'SitPoint' in the active scene.", "OK");
            return;
        }

        Undo.RegisterCompleteObjectUndo(scooter, "Configure Scooter Physics");

        // 1. BoxCollider setup
        BoxCollider box = scooter.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = Undo.AddComponent<BoxCollider>(scooter);
        }

        // Automatically compute the exact combined local bounds of all scooter + character meshes
        Renderer[] childRenderers = scooter.GetComponentsInChildren<Renderer>();
        if (childRenderers != null && childRenderers.Length > 0)
        {
            Bounds worldBounds = childRenderers[0].bounds;
            for (int i = 1; i < childRenderers.Length; i++)
            {
                worldBounds.Encapsulate(childRenderers[i].bounds);
            }

            // Convert world bounds to scooter's local space
            Vector3 localCenter = scooter.transform.InverseTransformPoint(worldBounds.center);
            Vector3 localSize = scooter.transform.InverseTransformVector(worldBounds.size);
            localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

            box.center = localCenter;
            box.size = localSize;
        }
        else
        {
            box.center = new Vector3(0f, 0.75f, 0f);
            box.size = new Vector3(0.8f, 1.5f, 1.8f);
        }

        // 2. Rigidbody setup
        Rigidbody rb = scooter.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = Undo.AddComponent<Rigidbody>(scooter);
        }

        rb.mass = 120f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        // Freeze X and Z rotations so the scooter stays upright
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        EditorUtility.SetDirty(scooter);
        Selection.activeGameObject = scooter;
        SceneView.RepaintAll();

        Debug.Log($"[MapBoundary] Successfully configured Rigidbody and BoxCollider on '{scooter.name}'.");
        EditorUtility.DisplayDialog("Player Setup", $"Configured BoxCollider and Rigidbody on '{scooter.name}'.\n\n- Mass: 120kg\n- Freeze Rotation: X & Z (stays upright)\n- Collision: Continuous", "OK");
    }
}

