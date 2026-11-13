using UnityEngine;

/// <summary>
/// Manages invisible boundary walls (North, South, East, West) and optional Floor collider around the playable map area.
/// Uses BoxColliders without MeshRenderers to block players and vehicles from falling off or leaving.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class MapBoundaryManager : MonoBehaviour
{
    [Header("Boundary Dimensions")]
    [Tooltip("Center position of the boundary box in world space.")]
    public Vector3 center = Vector3.zero;

    [Tooltip("Total width (X axis) and length (Z axis) of the playable area.")]
    public Vector2 mapSize = new Vector2(200f, 200f);

    [Tooltip("Height of the boundary walls (Y axis).")]
    public float wallHeight = 30f;

    [Tooltip("Thickness of each boundary collider to prevent high-speed tunneling.")]
    public float wallThickness = 5f;

    [Header("Floor Protection")]
    [Tooltip("Generate an invisible floor collider so vehicles/players with Rigidbody never fall into the void.")]
    public bool includeFloorCollider = true;

    [Tooltip("Thickness of the floor collider beneath the road.")]
    public float floorThickness = 2f;

    [Tooltip("Y-offset for the floor surface relative to center.y.")]
    public float floorSurfaceOffset = 0f;

    [Header("Physics Settings")]
    [Tooltip("Physics material applied to walls (e.g. low-friction so players slide instead of stick).")]
    public PhysicsMaterial physicsMaterial;

    [Tooltip("Physics Layer to assign to the walls (leave as 0 for Default).")]
    public int wallLayer = 0;

    [Header("Gizmo Visualization (Scene View Only)")]
    [Tooltip("Draw boundary outlines in Scene View.")]
    public bool showGizmos = true;

    [Tooltip("Wireframe color in Scene View.")]
    public Color gizmoWireColor = new Color(0f, 0.85f, 1f, 0.9f);

    [Tooltip("Color of the semi-transparent boundary planes.")]
    public Color gizmoFillColor = new Color(0f, 0.85f, 1f, 0.12f);

    // Wall names
    public const string NorthWallName = "NorthWall";
    public const string SouthWallName = "SouthWall";
    public const string EastWallName = "EastWall";
    public const string WestWallName = "WestWall";
    public const string FloorColliderName = "FloorCollider";

    private void OnValidate()
    {
        // Enforce minimum sizes
        mapSize.x = Mathf.Max(mapSize.x, 1f);
        mapSize.y = Mathf.Max(mapSize.y, 1f);
        wallHeight = Mathf.Max(wallHeight, 1f);
        wallThickness = Mathf.Max(wallThickness, 0.5f);
        floorThickness = Mathf.Max(floorThickness, 0.5f);

#if UNITY_EDITOR
        // Defer GameObject creation/modification outside of OnValidate to avoid Unity warnings
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && gameObject != null)
            {
                RebuildWalls();
            }
        };
#endif
    }

    /// <summary>
    /// Creates or updates the boundary wall and floor child objects and their BoxColliders.
    /// </summary>
    public void RebuildWalls()
    {
        float halfWidth = mapSize.x * 0.5f;
        float halfLength = mapSize.y * 0.5f;
        float halfHeight = wallHeight * 0.5f;
        float halfThick = wallThickness * 0.5f;

        // North (+Z)
        Vector3 northPos = new Vector3(center.x, center.y + halfHeight, center.z + halfLength + halfThick);
        Vector3 northSize = new Vector3(mapSize.x + (wallThickness * 2f), wallHeight, wallThickness);
        SetupWall(NorthWallName, northPos, northSize);

        // South (-Z)
        Vector3 southPos = new Vector3(center.x, center.y + halfHeight, center.z - halfLength - halfThick);
        Vector3 southSize = new Vector3(mapSize.x + (wallThickness * 2f), wallHeight, wallThickness);
        SetupWall(SouthWallName, southPos, southSize);

        // East (+X)
        Vector3 eastPos = new Vector3(center.x + halfWidth + halfThick, center.y + halfHeight, center.z);
        Vector3 eastSize = new Vector3(wallThickness, wallHeight, mapSize.y);
        SetupWall(EastWallName, eastPos, eastSize);

        // West (-X)
        Vector3 westPos = new Vector3(center.x - halfWidth - halfThick, center.y + halfHeight, center.z);
        Vector3 westSize = new Vector3(wallThickness, wallHeight, mapSize.y);
        SetupWall(WestWallName, westPos, westSize);

        // Floor (prevents falling through if ground meshes lack colliders)
        if (includeFloorCollider)
        {
            float halfFloorThick = floorThickness * 0.5f;
            Vector3 floorPos = new Vector3(center.x, center.y + floorSurfaceOffset - halfFloorThick, center.z);
            Vector3 floorSize = new Vector3(mapSize.x + (wallThickness * 2f), floorThickness, mapSize.y + (wallThickness * 2f));
            SetupWall(FloorColliderName, floorPos, floorSize);
        }
        else
        {
            RemoveChild(FloorColliderName);
        }
    }

    private void SetupWall(string wallName, Vector3 worldPosition, Vector3 boxSize)
    {
        Transform child = transform.Find(wallName);
        GameObject wallGo;

        if (child == null)
        {
            wallGo = new GameObject(wallName);
            wallGo.transform.SetParent(transform, true);
        }
        else
        {
            wallGo = child.gameObject;
        }

        wallGo.layer = wallLayer;
        wallGo.transform.position = worldPosition;
        wallGo.transform.rotation = Quaternion.identity;
        wallGo.transform.localScale = Vector3.one;

        // Ensure BoxCollider exists
        BoxCollider box = wallGo.GetComponent<BoxCollider>();
        if (box == null)
        {
            box = wallGo.AddComponent<BoxCollider>();
        }

        box.center = Vector3.zero;
        box.size = boxSize;
        box.material = physicsMaterial;

        // Ensure NO MeshRenderer exists so it remains strictly invisible
        MeshRenderer mr = wallGo.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            DestroyImmediate(mr);
        }

        MeshFilter mf = wallGo.GetComponent<MeshFilter>();
        if (mf != null)
        {
            DestroyImmediate(mf);
        }
    }

    private void RemoveChild(string childName)
    {
        Transform child = transform.Find(childName);
        if (child != null)
        {
            DestroyImmediate(child.gameObject);
        }
    }

    /// <summary>
    /// Scans renderers in the scene (excluding boundary walls) to auto-detect map bounds.
    /// </summary>
    public bool AutoCalculateMapBounds(float margin = 2f)
    {
        Renderer[] allRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        if (allRenderers == null || allRenderers.Length == 0)
        {
            return false;
        }

        bool hasBounds = false;
        Bounds totalBounds = new Bounds();
        float roadSurfaceY = 0f;
        bool foundRoad = false;

        foreach (Renderer rend in allRenderers)
        {
            // Ignore particle systems, trail renderers, or the boundary's own children
            if (rend is ParticleSystemRenderer || rend is TrailRenderer) continue;
            if (rend.transform.IsChildOf(transform)) continue;

            // Ignore character/scooter renderers when calculating the static city map bounds
            if (rend.GetComponentInParent<ScooterRiderPose>() != null) continue;

            // Detect road surface height if road tiles exist
            if (!foundRoad && rend.gameObject.name.StartsWith("Road"))
            {
                roadSurfaceY = rend.bounds.max.y;
                foundRoad = true;
            }

            if (!hasBounds)
            {
                totalBounds = rend.bounds;
                hasBounds = true;
            }
            else
            {
                totalBounds.Encapsulate(rend.bounds);
            }
        }

        if (hasBounds)
        {
            float baseHeight = foundRoad ? roadSurfaceY : Mathf.Max(0f, totalBounds.min.y);
            center = new Vector3(totalBounds.center.x, baseHeight, totalBounds.center.z);
            mapSize = new Vector2(totalBounds.size.x + (margin * 2f), totalBounds.size.z + (margin * 2f));
            RebuildWalls();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Removes the 4 child boundary walls and floor collider.
    /// </summary>
    public void ClearWalls()
    {
        string[] names = { NorthWallName, SouthWallName, EastWallName, WestWallName, FloorColliderName };
        foreach (string wallName in names)
        {
            RemoveChild(wallName);
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGizmos) return;

        float halfWidth = mapSize.x * 0.5f;
        float halfLength = mapSize.y * 0.5f;
        float bottom = center.y;
        float top = center.y + wallHeight;

        Vector3 p1 = new Vector3(center.x - halfWidth, bottom, center.z - halfLength);
        Vector3 p2 = new Vector3(center.x + halfWidth, bottom, center.z - halfLength);
        Vector3 p3 = new Vector3(center.x + halfWidth, bottom, center.z + halfLength);
        Vector3 p4 = new Vector3(center.x - halfWidth, bottom, center.z + halfLength);

        Vector3 t1 = new Vector3(p1.x, top, p1.z);
        Vector3 t2 = new Vector3(p2.x, top, p2.z);
        Vector3 t3 = new Vector3(p3.x, top, p3.z);
        Vector3 t4 = new Vector3(p4.x, top, p4.z);

        // Draw wireframe outline
        Gizmos.color = gizmoWireColor;
        // Bottom loop
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p4);
        Gizmos.DrawLine(p4, p1);

        // Top loop
        Gizmos.DrawLine(t1, t2);
        Gizmos.DrawLine(t2, t3);
        Gizmos.DrawLine(t3, t4);
        Gizmos.DrawLine(t4, t1);

        // Vertical pillars
        Gizmos.DrawLine(p1, t1);
        Gizmos.DrawLine(p2, t2);
        Gizmos.DrawLine(p3, t3);
        Gizmos.DrawLine(p4, t4);

        // Draw semi-transparent fill on the 4 boundary walls
        Gizmos.color = gizmoFillColor;
        // North wall
        Gizmos.DrawCube(new Vector3(center.x, center.y + (wallHeight * 0.5f), center.z + halfLength), new Vector3(mapSize.x, wallHeight, 0.05f));
        // South wall
        Gizmos.DrawCube(new Vector3(center.x, center.y + (wallHeight * 0.5f), center.z - halfLength), new Vector3(mapSize.x, wallHeight, 0.05f));
        // East wall
        Gizmos.DrawCube(new Vector3(center.x + halfWidth, center.y + (wallHeight * 0.5f), center.z), new Vector3(0.05f, wallHeight, mapSize.y));
        // West wall
        Gizmos.DrawCube(new Vector3(center.x - halfWidth, center.y + (wallHeight * 0.5f), center.z), new Vector3(0.05f, wallHeight, mapSize.y));

        // Draw floor outline/tint if enabled
        if (includeFloorCollider)
        {
            Gizmos.color = new Color(0f, 1f, 0.4f, 0.08f);
            Gizmos.DrawCube(new Vector3(center.x, center.y + floorSurfaceOffset - (floorThickness * 0.5f), center.z),
                            new Vector3(mapSize.x, floorThickness, mapSize.y));
        }
    }
}

