using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(menuName = "Delivery Rush/Pedestrian Collection")]
public class CityPedestrians : ScriptableObject
{
    public GameObject[] characters = System.Array.Empty<GameObject>();
    public AnimationClip[] walk = System.Array.Empty<AnimationClip>();
    public AnimationClip[] idle = System.Array.Empty<AnimationClip>();
    public AnimationClip greeting;
    [Range(1, 100)] public int population = 58;
    [HideInInspector] public int setupVersion;

    public const int CurrentSetupVersion = 4;
    public const string RootObjectName = "City Pedestrians";
    private const string ResourceName = "CityPedestrians";
    private const float Step = 1.5f;
    private const float MinGroupSpacing = 4.0f;

    private struct SeatSpot
    {
        public Vector3 groundPos;
        public Vector3 seatForward;
        public float seatSurfaceY;
        public Vector3 lookAt;
        public bool hasPartner;
        public Vector3 partnerGroundPos;
        public float partnerSeatSurfaceY;
        public Vector3 partnerLookAt;
        public float restaurantDistance;
        public string propName;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= Populate;
        SceneManager.sceneLoaded += Populate;
    }

    private static void Populate(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "CityScence") return;
        var collection = Resources.Load<CityPedestrians>(ResourceName);
        if (collection == null || collection.characters.Length == 0) return;
        collection.PopulateScene(scene, replaceExisting: false);
    }

    public int PopulateScene(Scene scene, bool replaceExisting)
    {
        if (!scene.IsValid() || !scene.isLoaded || characters == null || characters.Length == 0)
            return 0;

        GameObject existingRoot = FindRoot(scene);
        if (existingRoot != null)
        {
            int existingCount = existingRoot.GetComponentsInChildren<CityPedestrian>(true).Length;
            if (!replaceExisting && existingCount >= population && setupVersion == CurrentSetupVersion)
                return existingCount;

            if (Application.isPlaying)
                Destroy(existingRoot);
            else
                DestroyImmediate(existingRoot);
        }

        Physics.SyncTransforms();
        var allPieces = HierarchyGroup.Pieces(scene);
        var spots = new Dictionary<Vector2Int, Vector3>();
        foreach (var piece in allPieces)
        {
            if (piece == null || !IsPavementPiece(piece.name) || !piece.TryGetComponent(out Collider surface))
                continue;
            Bounds bounds = surface.bounds;
            for (int x = Mathf.CeilToInt(bounds.min.x / Step); x <= Mathf.FloorToInt(bounds.max.x / Step); x++)
            for (int z = Mathf.CeilToInt(bounds.min.z / Step); z <= Mathf.FloorToInt(bounds.max.z / Step); z++)
            {
                var key = new Vector2Int(x, z);
                if (spots.ContainsKey(key)) continue;
                if (TryPavement(new Vector3(x * Step, bounds.min.y, z * Step), out Vector3 spot))
                    spots.Add(key, spot);
            }
        }

        if (spots.Count == 0)
        {
            Debug.LogWarning("City pedestrians could not find clear pavement in CityScence.");
            return 0;
        }

        var root = new GameObject(RootObjectName);
        SceneManager.MoveGameObjectToScene(root, scene);

        Random.State previousRandom = Random.state;
        Random.InitState(20261114);

        var occupied = new List<Vector3>();
        int index = 0;

        // 1. Place Seated Citizens on Benches & Chairs (prioritizing near restaurants/coffee shops)
        var seats = CollectSeatSpots(allPieces);
        int maxSeated = Mathf.Min(12, population / 4);
        foreach (var seat in seats)
        {
            if (index >= maxSeated || index >= population) break;
            if (occupied.Exists(o => (o - seat.groundPos).sqrMagnitude < 2.2f * 2.2f)) continue;

            if (seat.hasPartner && index + 1 < maxSeated && index + 1 < population)
            {
                Spawn(root.transform, new[] { seat.groundPos }, seat.seatForward, seat.lookAt, index++, true, true, seat.seatSurfaceY, "Sitting & Talking");
                Spawn(root.transform, new[] { seat.partnerGroundPos }, seat.seatForward, seat.partnerLookAt, index++, true, true, seat.partnerSeatSurfaceY, "Sitting & Talking");
                occupied.Add(seat.groundPos);
                occupied.Add(seat.partnerGroundPos);
            }
            else
            {
                Spawn(root.transform, new[] { seat.groundPos }, seat.seatForward, seat.lookAt, index++, false, true, seat.seatSurfaceY, "Sitting");
                occupied.Add(seat.groundPos);
            }
        }

        // 2. Place Walking, Talking Pairs, and Chilling Citizens across the Sidewalks
        var keys = new List<Vector2Int>(spots.Keys);
        for (int i = keys.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (keys[i], keys[j]) = (keys[j], keys[i]);
        }

        foreach (var key in keys)
        {
            if (index >= population) break;
            Vector3 position = spots[key];
            if (occupied.Exists(other => (other - position).sqrMagnitude < MinGroupSpacing * MinGroupSpacing))
                continue;

            bool turnRightLoop = (index % 2) == 0;
            Vector3[] walkRoute = BuildWalkRoute(key, spots, maxCorners: 6, turnRight: turnRightLoop);
            bool hasLongRoute = walkRoute.Length >= 2 && (walkRoute[1] - position).sqrMagnitude > 2f;

            int pattern = index % 5;
            if (pattern == 1 && index + 1 < population && hasLongRoute)
            {
                // Standing Talking Pair (~30% of sidewalk citizens)
                Vector3 nextStep = walkRoute[1];
                Vector3 dir = (nextStep - position);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    dir.Normalize();
                    Vector3 spotA = position;
                    Vector3 candidateB = position + dir * 1.25f;
                    if (TryPavement(candidateB, out Vector3 spotB))
                    {
                        Spawn(root.transform, new[] { spotA }, (spotB - spotA).normalized, spotB, index++, true, false, 0f, "Talking");
                        Spawn(root.transform, new[] { spotB }, (spotA - spotB).normalized, spotA, index++, true, false, 0f, "Talking");
                        occupied.Add(spotA);
                        occupied.Add(spotB);
                        continue;
                    }
                }
            }

            if (pattern == 0 || !hasLongRoute)
            {
                // Chilling solo on the sidewalk (~20%)
                Vector3 lookDir = hasLongRoute
                    ? (walkRoute[1] - position).normalized
                    : Quaternion.Euler(0f, index * 73f, 0f) * Vector3.forward;
                Vector3 chillForward = Quaternion.Euler(0f, 90f, 0f) * lookDir;
                Vector3 chillLook = position + chillForward;
                Spawn(root.transform, new[] { position }, chillForward, chillLook, index++, false, false, 0f, "Chilling");
            }
            else
            {
                // Continuous Block/Park Loop Walking Citizen (~50%)
                Vector3 walkForward = (walkRoute[1] - position).normalized;
                string loopLabel = turnRightLoop ? "Walking - Turn Right" : "Walking - Turn Left";
                Spawn(root.transform, walkRoute, walkForward, walkRoute[1], index++, false, false, 0f, loopLabel);
            }
            occupied.Add(position);
        }

        Random.state = previousRandom;
        return index;
    }

    private static Vector3[] BuildWalkRoute(Vector2Int startKey, Dictionary<Vector2Int, Vector3> spots, int maxCorners, bool turnRight)
    {
        var route = new List<Vector3> { spots[startKey] };
        Vector2Int current = startKey;
        Vector2Int currentDir = Vector2Int.zero;

        Vector2Int[] allDirs = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

        // Pick best initial straight sidewalk direction
        int bestInitialReach = 0;
        foreach (var dir in allDirs)
        {
            int reach = MeasureSidewalkReach(current, dir, spots, 16);
            if (reach > bestInitialReach)
            {
                bestInitialReach = reach;
                currentDir = dir;
            }
        }

        if (bestInitialReach < 2)
            return route.ToArray();

        for (int leg = 0; leg < maxCorners; leg++)
        {
            int reach = MeasureSidewalkReach(current, currentDir, spots, 16);
            if (reach < 2) break;

            Vector2Int nextCorner = current + currentDir * reach;
            route.Add(spots[nextCorner]);
            current = nextCorner;

            // Consistently turn 90 degrees right (clockwise) or 90 degrees left (counter-clockwise) at each corner
            Vector2Int preferredTurn = turnRight
                ? new Vector2Int(currentDir.y, -currentDir.x)
                : new Vector2Int(-currentDir.y, currentDir.x);
            Vector2Int oppositeTurn = -preferredTurn;

            if (MeasureSidewalkReach(current, preferredTurn, spots, 16) >= 2)
                currentDir = preferredTurn;
            else if (MeasureSidewalkReach(current, oppositeTurn, spots, 16) >= 2)
                currentDir = oppositeTurn;
            else
                break;
        }

        return route.ToArray();
    }

    private static int MeasureSidewalkReach(Vector2Int from, Vector2Int dir, Dictionary<Vector2Int, Vector3> spots, int maxSteps)
    {
        int reach = 0;
        for (int dist = 1; dist <= maxSteps; dist++)
        {
            Vector2Int next = from + dir * dist;
            if (!spots.TryGetValue(next, out Vector3 pt) ||
                !TryPavement(pt - new Vector3(dir.x, 0f, dir.y) * (Step * 0.5f), out _))
                break;
            reach = dist;
        }
        return reach;
    }

    private static List<SeatSpot> CollectSeatSpots(List<GameObject> pieces)
    {
        var restaurants = pieces
            .Where(p => p != null && (p.name.StartsWith("Building_Restaurant") ||
                                      p.name.StartsWith("Building_Pizza") ||
                                      p.name.StartsWith("Building_Bar")))
            .Select(p => p.transform.position)
            .ToList();

        var roads = pieces
            .Where(p => p != null && p.name.StartsWith("Road "))
            .Select(p => p.transform.position)
            .ToList();

        var results = new List<SeatSpot>();

        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            if (!piece.name.StartsWith("Props_Bench_1")) continue;

            float minRestDist = restaurants.Count > 0
                ? restaurants.Min(r => Vector3.Distance(r, piece.transform.position))
                : 999f;

            if (TryExtractBenchSeat(piece, minRestDist, roads, out var benchSpot))
            {
                results.Add(benchSpot);
            }
        }

        return results
            .OrderBy(s => s.restaurantDistance)
            .ToList();
    }

    private static bool TryExtractBenchSeat(GameObject bench, float restDist, List<Vector3> roads, out SeatSpot spot)
    {
        spot = default;
        var mf = bench.GetComponentInChildren<MeshFilter>();
        var col = bench.GetComponentInChildren<Collider>();
        if (mf == null || mf.sharedMesh == null || col == null) return false;

        Mesh mesh = mf.sharedMesh;
        Bounds lb = mesh.bounds;
        Bounds wb = col.bounds;

        Vector3 worldX = bench.transform.TransformVector(new Vector3(lb.size.x, 0f, 0f));
        Vector3 worldZ = bench.transform.TransformVector(new Vector3(0f, 0f, lb.size.z));
        bool xIsWidth = worldX.magnitude >= worldZ.magnitude;

        Vector3 worldRight = (xIsWidth ? worldX : worldZ).normalized;
        worldRight.y = 0f;
        worldRight.Normalize();

        Vector3 depthVec = xIsWidth ? worldZ : worldX;
        float halfDepth = depthVec.magnitude * 0.5f;
        Vector3 worldForward = depthVec.normalized;
        worldForward.y = 0f;
        if (worldForward.sqrMagnitude < 0.001f) worldForward = bench.transform.forward;
        worldForward.Normalize();

        Vector3 benchCenterWorld = wb.center;

        if (roads != null && roads.Count > 0)
        {
            Vector3 nearestRoad = roads.OrderBy(r => (r - benchCenterWorld).sqrMagnitude).First();
            Vector3 toRoad = nearestRoad - benchCenterWorld;
            toRoad.y = 0f;
            if (Vector3.Dot(worldForward, toRoad) < 0f)
                worldForward = -worldForward;
        }

        float baseGroundY = wb.min.y;
        float topPlankY = wb.max.y;
        if (col.Raycast(new Ray(new Vector3(benchCenterWorld.x, wb.max.y + 0.5f, benchCenterWorld.z), Vector3.down), out RaycastHit hit, 1.5f))
        {
            topPlankY = Mathf.Max(hit.point.y, wb.max.y - 0.04f);
        }

        Vector3 frontEdgeCenter = benchCenterWorld + worldForward * (halfDepth * 0.85f);
        frontEdgeCenter.y = baseGroundY;

        bool makePair = restDist < 48f;
        if (makePair)
        {
            Vector3 leftPos = frontEdgeCenter - worldRight * 0.42f;
            Vector3 rightPos = frontEdgeCenter + worldRight * 0.42f;
            leftPos.y = baseGroundY;
            rightPos.y = baseGroundY;

            spot = new SeatSpot
            {
                groundPos = leftPos,
                seatForward = worldForward,
                seatSurfaceY = topPlankY,
                lookAt = rightPos,
                hasPartner = true,
                partnerGroundPos = rightPos,
                partnerSeatSurfaceY = topPlankY,
                partnerLookAt = leftPos,
                restaurantDistance = restDist,
                propName = bench.name
            };
        }
        else
        {
            Vector3 soloPos = frontEdgeCenter - worldRight * 0.20f;
            soloPos.y = baseGroundY;
            spot = new SeatSpot
            {
                groundPos = soloPos,
                seatForward = worldForward,
                seatSurfaceY = topPlankY,
                lookAt = soloPos + worldForward * 3f,
                hasPartner = false,
                restaurantDistance = restDist,
                propName = bench.name
            };
        }

        return true;
    }

    private static GameObject FindRoot(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root != null && root.name == RootObjectName)
                return root;
        }
        return null;
    }

    private static bool IsPavementPiece(string name) =>
        name.StartsWith("Road Lane") || name.StartsWith("Road Corner") ||
        name.StartsWith("Road Intersection") || name.StartsWith("Road T_Intersection") ||
        name.StartsWith("Road Sidewalk") || name.StartsWith("Road Concrete");

    internal static bool TryPavement(Vector3 point, out Vector3 ground)
    {
        ground = default;
        if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 4f,
            ~0, QueryTriggerInteraction.Ignore)) return false;
        Transform rootPiece = HierarchyGroup.PieceRoot(hit.transform);
        if (rootPiece == null) return false;
        string piece = rootPiece.name;
        bool paving = piece.StartsWith("Road Concrete") || piece.StartsWith("Road Sidewalk");
        if (!IsPavementPiece(piece) || (!paving && hit.point.y <= 0.12f) || hit.normal.y < 0.85f)
            return false;
        ground = hit.point;
        return !Physics.CheckSphere(ground + Vector3.up * 0.75f, 0.35f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Detects if a character mesh wears a skirt/dress (vertices crossing the midline x ≈ 0 at knee/mid-thigh height)
    /// vs trousers/jeans (two separate leg tubes with a gap at x ≈ 0).
    /// </summary>
    private static bool HasSkirtOrDress(GameObject prefab)
    {
        if (prefab == null) return true;
        var smr = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .FirstOrDefault(r => r.sharedMesh != null && r.sharedMesh.vertexCount > 100);
        if (smr == null || smr.sharedMesh == null) return false;
        if (!smr.sharedMesh.isReadable)
            return prefab.name == "Character 01" || prefab.name == "Character 04" || prefab.name == "Character 07";

        var verts = smr.sharedMesh.vertices;
        if (verts == null || verts.Length == 0) return false;

        float minY = float.MaxValue, maxY = float.MinValue;
        var rootPts = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 wp = smr.transform.TransformPoint(verts[i]);
            Vector3 rp = prefab.transform.InverseTransformPoint(wp);
            rootPts[i] = rp;
            if (rp.y < minY) minY = rp.y;
            if (rp.y > maxY) maxY = rp.y;
        }

        float height = maxY - minY;
        if (height < 0.5f) return false;

        // Check mid-thigh to knee band (20% to 38% of character height, below the pelvis crotch)
        float bandLow = minY + height * 0.20f;
        float bandHigh = minY + height * 0.38f;
        int midlineSkirtVerts = 0;
        for (int i = 0; i < rootPts.Length; i++)
        {
            Vector3 p = rootPts[i];
            if (p.y >= bandLow && p.y <= bandHigh && Mathf.Abs(p.x) < 0.032f && Mathf.Abs(p.z) > 0.05f)
            {
                midlineSkirtVerts++;
                if (midlineSkirtVerts >= 3)
                    return true;
            }
        }
        return false;
    }

    private GameObject SelectPrefab(int index, bool sitting)
    {
        if (!sitting)
            return characters[index % characters.Length];

        var sitCandidates = characters
            .Where(c => c != null &&
                        c.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("mixamorig:")) &&
                        !HasSkirtOrDress(c))
            .ToArray();

        if (sitCandidates.Length > 0)
            return sitCandidates[index % sitCandidates.Length];

        return characters[index % characters.Length];
    }

    private void Spawn(
        Transform parent,
        Vector3[] route,
        Vector3 bodyForward,
        Vector3 lookAt,
        int index,
        bool chatting,
        bool sitting,
        float seatY,
        string roleLabel)
    {
        var prefab = SelectPrefab(index, sitting);
        if (prefab == null) return;

        Vector3 start = route[0];
        GameObject actor;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            actor.transform.position = start;
        }
        else
#endif
        {
            actor = Instantiate(prefab, start, Quaternion.identity, parent);
        }

        Vector3 faceDirection = bodyForward;
        faceDirection.y = 0f;
        if (faceDirection.sqrMagnitude < 0.001f)
            faceDirection = ((!sitting && route.Length > 1) ? route[1] : lookAt) - start;
        faceDirection.y = 0f;
        if (faceDirection.sqrMagnitude > 0.001f)
            actor.transform.rotation = Quaternion.LookRotation(faceDirection.normalized);

        actor.name = $"Citizen {index + 1:00} [{roleLabel}] ({prefab.name})";
        foreach (var collider in actor.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        var pedestrian = actor.GetComponent<CityPedestrian>();
        if (pedestrian == null)
            pedestrian = actor.AddComponent<CityPedestrian>();

        pedestrian.Initialize(route, lookAt, Pick(idle), Pick(walk), chatting ? greeting : null, index, chatting, sitting, seatY);
    }

    private static AnimationClip Pick(AnimationClip[] clips) =>
        clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
}
