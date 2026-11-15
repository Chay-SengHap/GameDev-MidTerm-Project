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
    public GameObject coffeeChairPrefab;
    [Range(1, 120)] public int population = 78;
    [HideInInspector] public int setupVersion;

    public const int CurrentSetupVersion = 7;
    public const string RootObjectName = "City Pedestrians";
    public static string VersionMarkerName => $"SetupVersion_{CurrentSetupVersion}";
    private const string ResourceName = "CityPedestrians";
    private const float Step = 1.5f;

    private struct SeatSpot
    {
        public Vector3 groundPos;
        public Vector3 seatForward;
        public float seatSurfaceY;
        public Vector3 lookAt;
        public bool hasPartner;
        public Vector3 partnerGroundPos;
        public Vector3 partnerSeatForward;
        public float partnerSeatSurfaceY;
        public Vector3 partnerLookAt;
        public float priorityScore;
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
            bool hasVersionMarker = existingRoot.transform.Find(VersionMarkerName) != null;
            if (!replaceExisting && hasVersionMarker && existingCount >= population && setupVersion == CurrentSetupVersion)
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
                Vector3 samplePt = new Vector3(x * Step, bounds.min.y, z * Step);
                if (TryPavement(samplePt, out Vector3 spot) && HasWhiteSidewalkMargin(spot))
                    spots.Add(key, spot);
            }
        }

        if (spots.Count == 0)
        {
            Debug.LogWarning("City pedestrians could not find clear white sidewalk in CityScence.");
            return 0;
        }

        var root = new GameObject(RootObjectName);
        SceneManager.MoveGameObjectToScene(root, scene);
        var versionMarker = new GameObject(VersionMarkerName);
        versionMarker.transform.SetParent(root.transform, false);

        Random.State previousRandom = Random.state;
        Random.InitState(20261114);

        // Place patio tables/chairs outside Restaurants & Pizza shops that don't have one yet
        var dynamicChairProps = SpawnRestaurantPatioChairs(root.transform, allPieces);
        Physics.SyncTransforms();

        var occupied = new List<Vector3>();
        var stationaryOccupied = new List<Vector3>();
        int index = 0;

        // 1. Place Seated Citizens on Cafe/Restaurant Chairs & Park Benches
        var combinedPieces = new List<GameObject>(allPieces);
        combinedPieces.AddRange(dynamicChairProps);
        var seats = CollectSeatSpots(combinedPieces);
        int maxSeated = Mathf.Min(26, population / 2);

        foreach (var seat in seats)
        {
            if (index >= maxSeated || index >= population) break;
            if (occupied.Exists(o => (o - seat.groundPos).sqrMagnitude < 2.0f * 2.0f)) continue;

            if (seat.hasPartner && index + 1 < maxSeated && index + 1 < population)
            {
                Spawn(root.transform, new[] { seat.groundPos }, seat.seatForward, seat.lookAt, index++, true, true, seat.seatSurfaceY, "Sitting & Talking");
                Spawn(root.transform, new[] { seat.partnerGroundPos }, seat.partnerSeatForward, seat.partnerLookAt, index++, true, true, seat.partnerSeatSurfaceY, "Sitting & Talking");
                occupied.Add(seat.groundPos);
                occupied.Add(seat.partnerGroundPos);
                stationaryOccupied.Add(seat.groundPos);
            }
            else
            {
                Spawn(root.transform, new[] { seat.groundPos }, seat.seatForward, seat.lookAt, index++, false, true, seat.seatSurfaceY, "Sitting");
                occupied.Add(seat.groundPos);
                stationaryOccupied.Add(seat.groundPos);
            }
        }

        // 2. Spread Walking, Talking Pairs, and Chilling Citizens Evenly Across All White Sidewalks
        var availableKeys = new List<Vector2Int>(spots.Keys);
        for (int i = availableKeys.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (availableKeys[i], availableKeys[j]) = (availableKeys[j], availableKeys[i]);
        }

        int safety = 0;
        while (index < population && availableKeys.Count > 0 && safety++ < 2500)
        {
            int pattern = index % 5;
            bool wantsStationary = (pattern == 0 || pattern == 1);
            float minDist = wantsStationary ? 7.0f : 4.6f;

            int pickedIdx = PickWellSpreadCandidate(availableKeys, spots, occupied, stationaryOccupied, minDist, wantsStationary);
            if (pickedIdx < 0)
            {
                // Relax spacing slightly if the map is nearly full
                pickedIdx = PickWellSpreadCandidate(availableKeys, spots, occupied, stationaryOccupied, 3.6f, false);
                if (pickedIdx < 0) break;
            }

            Vector2Int key = availableKeys[pickedIdx];
            availableKeys.RemoveAt(pickedIdx);
            Vector3 position = spots[key];

            bool turnRightLoop = (index % 2) == 0;
            Vector3[] walkRoute = BuildWalkRoute(key, spots, maxCorners: 6, turnRight: turnRightLoop);
            bool hasLongRoute = walkRoute.Length >= 2 && (walkRoute[1] - position).sqrMagnitude > 2f;

            if (pattern == 1 && index + 1 < population && hasLongRoute)
            {
                // Standing Talking Pair on the white sidewalk
                Vector3 nextStep = walkRoute[1];
                Vector3 dir = (nextStep - position);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    dir.Normalize();
                    Vector3 spotA = position;
                    Vector3 candidateB = position + dir * 1.25f;
                    if (TryPavement(candidateB, out Vector3 spotB) && HasWhiteSidewalkMargin(spotB))
                    {
                        Spawn(root.transform, new[] { spotA }, (spotB - spotA).normalized, spotB, index++, true, false, 0f, "Talking");
                        Spawn(root.transform, new[] { spotB }, (spotA - spotB).normalized, spotA, index++, true, false, 0f, "Talking");
                        occupied.Add(spotA);
                        occupied.Add(spotB);
                        stationaryOccupied.Add(spotA);
                        stationaryOccupied.Add(spotB);
                        continue;
                    }
                }
            }

            if (pattern == 0 || !hasLongRoute)
            {
                // Chilling solo on the white sidewalk
                Vector3 lookDir = hasLongRoute
                    ? (walkRoute[1] - position).normalized
                    : Quaternion.Euler(0f, index * 73f, 0f) * Vector3.forward;
                Vector3 chillForward = Quaternion.Euler(0f, 90f, 0f) * lookDir;
                Vector3 chillLook = position + chillForward;
                Spawn(root.transform, new[] { position }, chillForward, chillLook, index++, false, false, 0f, "Chilling");
                occupied.Add(position);
                stationaryOccupied.Add(position);
            }
            else
            {
                // Continuous Block/Park Loop Walking Citizen on the white sidewalk
                Vector3 walkForward = (walkRoute[1] - position).normalized;
                string loopLabel = turnRightLoop ? "Walking - Turn Right" : "Walking - Turn Left";
                Spawn(root.transform, walkRoute, walkForward, walkRoute[1], index++, false, false, 0f, loopLabel);
                occupied.Add(position);
            }
        }

        Random.state = previousRandom;
        return index;
    }

    /// <summary>
    /// Farthest-point candidate selection so citizens spread out evenly across the entire city
    /// instead of clustering in one place.
    /// </summary>
    private static int PickWellSpreadCandidate(
        List<Vector2Int> candidates,
        Dictionary<Vector2Int, Vector3> spots,
        List<Vector3> occupied,
        List<Vector3> stationaryOccupied,
        float minSpacing,
        bool isStationary)
    {
        float minSqr = minSpacing * minSpacing;
        float minStatSqr = isStationary ? (8.5f * 8.5f) : (4.2f * 4.2f);

        int bestIdx = -1;
        float bestNearestSqr = -1f;
        int sampleCount = Mathf.Min(candidates.Count, 28);

        for (int s = 0; s < sampleCount; s++)
        {
            int idx = (s * 17) % candidates.Count;
            Vector3 pos = spots[candidates[idx]];

            float nearestAll = float.MaxValue;
            for (int i = 0; i < occupied.Count; i++)
            {
                float d = (occupied[i] - pos).sqrMagnitude;
                if (d < nearestAll) nearestAll = d;
            }
            if (nearestAll < minSqr) continue;

            if (isStationary)
            {
                float nearestStat = float.MaxValue;
                for (int i = 0; i < stationaryOccupied.Count; i++)
                {
                    float d = (stationaryOccupied[i] - pos).sqrMagnitude;
                    if (d < nearestStat) nearestStat = d;
                }
                if (nearestStat < minStatSqr) continue;
            }

            if (nearestAll > bestNearestSqr)
            {
                bestNearestSqr = nearestAll;
                bestIdx = idx;
            }
        }

        if (bestIdx >= 0) return bestIdx;

        // Fallback linear scan if none of the 28 samples passed
        for (int idx = 0; idx < candidates.Count; idx++)
        {
            Vector3 pos = spots[candidates[idx]];
            if (occupied.Exists(o => (o - pos).sqrMagnitude < minSqr)) continue;
            if (isStationary && stationaryOccupied.Exists(o => (o - pos).sqrMagnitude < minStatSqr)) continue;
            return idx;
        }
        return -1;
    }

    private List<GameObject> SpawnRestaurantPatioChairs(Transform parent, List<GameObject> pieces)
    {
        var spawned = new List<GameObject>();
        GameObject chairPrefab = coffeeChairPrefab;
#if UNITY_EDITOR
        if (chairPrefab == null)
        {
            chairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/SimplePoly City - Low Poly Assets/Prefab/Props/Props_Coffee shop chair.prefab");
        }
#endif
        if (chairPrefab == null) return spawned;

        var existingChairs = pieces
            .Where(p => p != null && p.name.StartsWith("Props_Coffee shop chair"))
            .Select(p => p.transform.position)
            .ToList();

        var roads = pieces
            .Where(p => p != null && p.name.StartsWith("Road Lane"))
            .Select(p => p.transform.position)
            .ToList();

        var eateries = pieces
            .Where(p => p != null && (p.name.StartsWith("Building_Restaurant") ||
                                      p.name.StartsWith("Building_Pizza") ||
                                      p.name.StartsWith("Building_Coffee Shop")))
            .ToList();

        foreach (var bld in eateries)
        {
            Vector3 bldPos = bld.transform.position;
            if (existingChairs.Exists(c => Vector3.Distance(c, bldPos) < 14f))
                continue;

            // Find direction from building toward the nearest street
            Vector3 toRoad = Vector3.forward;
            if (roads.Count > 0)
            {
                Vector3 nearestRoad = roads.OrderBy(r => (r - bldPos).sqrMagnitude).First();
                Vector3 diff = nearestRoad - bldPos;
                diff.y = 0f;
                if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.z))
                    toRoad = diff.x >= 0f ? Vector3.right : Vector3.left;
                else
                    toRoad = diff.z >= 0f ? Vector3.forward : Vector3.back;
            }

            Vector3 side = new Vector3(-toRoad.z, 0f, toRoad.x);
            Bounds bldBounds = bld.GetComponentInChildren<Renderer>()?.bounds ?? new Bounds(bldPos, new Vector3(8f, 4f, 8f));
            float frontExtent = Mathf.Abs(toRoad.x) > 0.5f ? bldBounds.extents.x : bldBounds.extents.z;

            // Place 1 patio chair/table set on the restaurant front terrace aligned with the sidewalk
            Quaternion chairRot = Quaternion.LookRotation(toRoad, Vector3.up);
            foreach (float sideOffset in new[] { -2.6f, 2.6f })
            {
                Vector3 tablePos = new Vector3(bldBounds.center.x, 0f, bldBounds.center.z)
                                   + toRoad * (frontExtent + 1.6f)
                                   + side * sideOffset;

                if (Physics.Raycast(tablePos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    // Must land on flat patio/terrace ground (not in the road or inside a building)
                    if (hit.point.y > 0.4f || hit.normal.y < 0.9f) continue;
                    tablePos.y = hit.point.y;
                }

                if (Physics.CheckSphere(tablePos + Vector3.up * 1.0f, 0.75f, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                GameObject instance;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(chairPrefab, parent);
                    instance.transform.SetPositionAndRotation(tablePos, chairRot);
                }
                else
#endif
                {
                    instance = Instantiate(chairPrefab, tablePos, chairRot, parent);
                }

                instance.name = $"Props_Coffee shop chair ({bld.name})";
                spawned.Add(instance);
                existingChairs.Add(tablePos);
                break; // 1 table (2 chairs) per restaurant so seats stay well spread
            }
        }

        return spawned;
    }

    private static Vector3[] BuildWalkRoute(Vector2Int startKey, Dictionary<Vector2Int, Vector3> spots, int maxCorners, bool turnRight)
    {
        var route = new List<Vector3> { spots[startKey] };
        Vector2Int current = startKey;
        Vector2Int currentDir = Vector2Int.zero;

        Vector2Int[] allDirs = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

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
                                      p.name.StartsWith("Building_Coffee Shop") ||
                                      p.name.StartsWith("Building_Bar")))
            .Select(p => p.transform.position)
            .ToList();

        var roads = pieces
            .Where(p => p != null && p.name.StartsWith("Road "))
            .Select(p => p.transform.position)
            .ToList();

        var results = new List<SeatSpot>();
        var westParkBenches = new List<GameObject>();
        var eastParkBenches = new List<GameObject>();
        var otherBenches = new List<GameObject>();

        foreach (var piece in pieces)
        {
            if (piece == null) continue;
            if (piece.name.StartsWith("Props_Coffee shop chair"))
            {
                float minRestDist = restaurants.Count > 0
                    ? restaurants.Min(r => Vector3.Distance(r, piece.transform.position))
                    : 999f;
                if (TryExtractCoffeeTableSeats(piece, minRestDist, out var chairSpot))
                    results.Add(chairSpot);
            }
            else if (piece.name.StartsWith("Props_Bench_1"))
            {
                Vector3 pos = piece.transform.position;
                if (pos.x >= 40f && pos.x <= 82f && pos.z >= 132f && pos.z <= 148f)
                    westParkBenches.Add(piece);
                else if (pos.x >= 160f && pos.x <= 202f && pos.z >= 132f && pos.z <= 148f)
                    eastParkBenches.Add(piece);
                else
                    otherBenches.Add(piece);
            }
        }

        // In each park, place 2 people sitting together on 1 bench + 2 people sitting alone on 2 separate benches
        AddParkBenchGroup(westParkBenches, roads, results);
        AddParkBenchGroup(eastParkBenches, roads, results);

        // Also allow sitting on other benches around the city
        foreach (var bench in otherBenches)
        {
            float minRestDist = restaurants.Count > 0
                ? restaurants.Min(r => Vector3.Distance(r, bench.transform.position))
                : 999f;
            bool makePair = minRestDist < 45f;
            if (TryExtractBenchSeat(bench, makePair, minRestDist, roads, out var benchSpot))
                results.Add(benchSpot);
        }

        return results
            .OrderBy(s => s.priorityScore)
            .ToList();
    }

    private static void AddParkBenchGroup(List<GameObject> parkBenches, List<Vector3> roads, List<SeatSpot> results)
    {
        if (parkBenches.Count == 0) return;

        var ordered = parkBenches
            .OrderBy(b => Mathf.RoundToInt(b.transform.position.x))
            .ThenBy(b => Mathf.RoundToInt(b.transform.position.z))
            .ToList();

        // Pick 1 bench for a talking pair (2 people sitting together) and 2 benches for solo sitters (2 sitting alone)
        int pairIdx = 0;
        int soloIdx1 = ordered.Count > 2 ? 2 : (ordered.Count > 1 ? 1 : -1);
        int soloIdx2 = ordered.Count > 5 ? 5 : (ordered.Count > 3 ? 3 : -1);

        if (TryExtractBenchSeat(ordered[pairIdx], makePair: true, priorityScore: -420f, roads, out var pairSpot))
            results.Add(pairSpot);

        if (soloIdx1 >= 0 && TryExtractBenchSeat(ordered[soloIdx1], makePair: false, priorityScore: -410f, roads, out var soloSpot1))
            results.Add(soloSpot1);

        if (soloIdx2 >= 0 && TryExtractBenchSeat(ordered[soloIdx2], makePair: false, priorityScore: -405f, roads, out var soloSpot2))
            results.Add(soloSpot2);
    }

    private static bool TryExtractCoffeeTableSeats(GameObject chairProp, float restDist, out SeatSpot spot)
    {
        spot = default;
        var col = chairProp.GetComponentInChildren<Collider>();
        if (col == null) return false;

        Bounds wb = col.bounds;
        float baseGroundY = wb.min.y;

        // In Props_Coffee shop chair.fbx:
        // - Left chair seat is at local X in [-1.087, -0.368], Z in [-0.35, +0.35], top surface at local Y = 0.534, facing +X
        // - Right chair seat is at local X in [+0.370, +1.089], Z in [-0.35, +0.35], top surface at local Y = 0.534, facing -X
        Vector3 chairRight = chairProp.transform.TransformDirection(Vector3.right);
        chairRight.y = 0f;
        if (chairRight.sqrMagnitude < 0.001f) chairRight = Vector3.right;
        chairRight.Normalize();

        Vector3 forwardA = chairRight;
        Vector3 forwardB = -chairRight;

        // ApplySeatedPose places hips at (transform.position - forward * 0.15f).
        // Placing groundPos at local X = -0.53 and +0.53 puts hips at local X = -0.68 and +0.68 (dead center on each wooden chair seat).
        Vector3 posA = chairProp.transform.TransformPoint(new Vector3(-0.53f, 0f, 0f));
        Vector3 posB = chairProp.transform.TransformPoint(new Vector3(0.53f, 0f, 0f));
        posA.y = baseGroundY;
        posB.y = baseGroundY;

        float seatTopY = chairProp.transform.TransformPoint(new Vector3(0f, 0.534f, 0f)).y;

        spot = new SeatSpot
        {
            groundPos = posA,
            seatForward = forwardA,
            seatSurfaceY = seatTopY,
            lookAt = posB,
            hasPartner = true,
            partnerGroundPos = posB,
            partnerSeatForward = forwardB,
            partnerSeatSurfaceY = seatTopY,
            partnerLookAt = posA,
            priorityScore = -500f + restDist,
            propName = chairProp.name
        };
        return true;
    }

    private static bool TryExtractBenchSeat(GameObject bench, bool makePair, float priorityScore, List<Vector3> roads, out SeatSpot spot)
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
                partnerSeatForward = worldForward,
                partnerSeatSurfaceY = topPlankY,
                partnerLookAt = leftPos,
                priorityScore = priorityScore,
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
                priorityScore = priorityScore,
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

    /// <summary>
    /// Only road pieces with raised white sidewalks are allowed (excludes grey Road Concrete Tile and Road Tile).
    /// </summary>
    private static bool IsPavementPiece(string name) =>
        name.StartsWith("Road Lane") || name.StartsWith("Road Corner") ||
        name.StartsWith("Road Intersection") || name.StartsWith("Road T_Intersection") ||
        name.StartsWith("Road Sidewalk");

    internal static bool TryPavement(Vector3 point, out Vector3 ground)
    {
        ground = default;
        // Pedestrians' own body colliders are left out, so a walker never blocks its own (or a neighbour's) path check.
        if (!Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, 4f,
            CityPedestrian.NotPedestrians, QueryTriggerInteraction.Ignore)) return false;
        Transform rootPiece = HierarchyGroup.PieceRoot(hit.transform);
        if (rootPiece == null) return false;
        string piece = rootPiece.name;

        // Strictly require the raised white sidewalk surface (y >= 0.15m, above the dark road and grey concrete tiles)
        if (!IsPavementPiece(piece) || hit.point.y < 0.15f || hit.point.y > 0.42f || hit.normal.y < 0.85f)
            return false;

        ground = hit.point;
        return !Physics.CheckSphere(ground + Vector3.up * 0.75f, 0.35f, CityPedestrian.NotPedestrians, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Ensures a sidewalk spawn spot is comfortably inside the white sidewalk band (not hanging off the edge onto grey concrete).
    /// </summary>
    private static bool HasWhiteSidewalkMargin(Vector3 spot)
    {
        const float margin = 0.24f;
        return TryPavement(spot + new Vector3(margin, 0f, 0f), out _) &&
               TryPavement(spot + new Vector3(-margin, 0f, 0f), out _) &&
               TryPavement(spot + new Vector3(0f, 0f, margin), out _) &&
               TryPavement(spot + new Vector3(0f, 0f, -margin), out _);
    }

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
