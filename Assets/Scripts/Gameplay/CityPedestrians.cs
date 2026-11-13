using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(menuName = "Delivery Rush/Pedestrian Collection")]
public class CityPedestrians : ScriptableObject
{
    public GameObject[] characters = System.Array.Empty<GameObject>();
    public AnimationClip[] walk = System.Array.Empty<AnimationClip>();
    public AnimationClip[] idle = System.Array.Empty<AnimationClip>();
    public AnimationClip greeting;
    [Min(1)] public int population = 18;

    private const string ResourceName = "CityPedestrians";
    private const float Step = 1.5f;

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

        Physics.SyncTransforms();
        var spots = new Dictionary<Vector2Int, Vector3>();
        foreach (var piece in HierarchyGroup.Pieces(scene))
        {
            if (!IsPavementPiece(piece.name) || !piece.TryGetComponent(out Collider surface)) continue;
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
            return;
        }

        var keys = new List<Vector2Int>(spots.Keys);
        var occupied = new List<Vector3>();
        var root = new GameObject("City Pedestrians");
        SceneManager.MoveGameObjectToScene(root, scene);
        for (int i = keys.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (keys[i], keys[j]) = (keys[j], keys[i]);
        }

        int index = 0;
        foreach (var key in keys)
        {
            if (index >= collection.population) break;
            Vector3 position = spots[key];
            if (occupied.Exists(other => (other - position).sqrMagnitude < 7f * 7f)) continue;
            Vector2Int end = key;
            foreach (var direction in new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down })
            {
                for (int distance = 1; distance <= 12; distance++)
                {
                    Vector2Int next = key + direction * distance;
                    if (!spots.TryGetValue(next, out Vector3 point) ||
                        !TryPavement(point - new Vector3(direction.x, 0f, direction.y) * (Step * 0.5f), out _))
                        break;
                    if ((next - key).sqrMagnitude > (end - key).sqrMagnitude) end = next;
                }
            }

            if (index % 5 == 1 && index + 1 < collection.population && end != key)
            {
                Vector2Int offset = end - key;
                var neighbor = key + new Vector2Int(System.Math.Sign(offset.x), System.Math.Sign(offset.y));
                Vector3 partner = spots[neighbor];
                collection.Spawn(root.transform, position, position, partner, index++, true);
                collection.Spawn(root.transform, partner, partner, position, index++, true);
                occupied.Add(partner);
            }
            else
            {
                Vector3 destination = index % 5 == 0 ? position : spots[end];
                collection.Spawn(root.transform, position, destination, position + Vector3.forward, index++, false);
            }
            occupied.Add(position);
        }
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
        string piece = HierarchyGroup.PieceRoot(hit.transform).name;
        bool paving = piece.StartsWith("Road Concrete") || piece.StartsWith("Road Sidewalk");
        if (!IsPavementPiece(piece) || (!paving && hit.point.y <= 0.12f) || hit.normal.y < 0.85f)
            return false;
        ground = hit.point;
        return !Physics.CheckSphere(ground + Vector3.up * 0.75f, 0.35f, ~0, QueryTriggerInteraction.Ignore);
    }

    private void Spawn(Transform parent, Vector3 start, Vector3 end, Vector3 lookAt, int index, bool chatting)
    {
        var prefab = characters[index % characters.Length];
        if (prefab == null) return;
        var actor = Instantiate(prefab, start, Quaternion.identity, parent);
        actor.name = $"Pedestrian {index + 1} ({prefab.name})";
        foreach (var collider in actor.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var pedestrian = actor.AddComponent<CityPedestrian>();
        pedestrian.Initialize(start, end, lookAt, Pick(idle), Pick(walk), chatting ? greeting : null, index);
    }

    private static AnimationClip Pick(AnimationClip[] clips) =>
        clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
}
