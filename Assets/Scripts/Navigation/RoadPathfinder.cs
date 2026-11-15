using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shortest route along the city's roads, for the delivery guide (minimap line and pointer arrow).
///
/// The first time a route is asked for, the area covered by the "Road ..." pieces is sampled on a 2 m grid with
/// <see cref="TrafficSystem.IsRoad"/> (the same test the traffic uses). Routes are searched on that grid:
///  - moving along the road costs its length; cells near the curb cost a little extra, so the route runs down the
///    middle of the street instead of hugging the pavement;
///  - every turn costs extra, so the route keeps to straight streets and turns only at junctions.
/// The result is the list of corners (junction centres) to drive through, ending at the road point closest to the
/// destination. For each destination the costs are worked out once (<see cref="CreateField"/>); after that the
/// route from any position is found almost for free, so it can follow the courier every frame.
/// New or moved roads are picked up automatically when a scene loads.
/// </summary>
public static class RoadPathfinder
{
    private const float CellSize = 2f;
    private const float TurnCost = 6f;          // in cells: a turn has to save at least this much distance
    private const int CenterCells = 3;          // cells closer than this to the curb cost extra
    private const float EdgePenalty = 1.5f;     // extra cost per cell closer to the curb
    private const int SnapRadius = 15;          // how far (cells) a start / end point may be from the road
    private const float IgnoreCornersWithin = 8f;   // m: corners this close to the courier are not shown
    private const float UTurnCost = 25f;       // in cells: turning back is only suggested when it saves this much

    private static readonly Vector2Int[] Steps = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

    internal sealed class Grid
    {
        public Scene Scene;
        public Vector2 Origin;      // world x/z of cell (0,0)'s corner
        public int Width, Height;
        public bool[] Road;
        public float[] Penalty;
    }

    private static Grid grid;

    /// <summary>
    /// Prepares routing to one destination: works out, for every road cell and travel direction, the cost of the
    /// best route from there to the destination (one search, done once per destination). After that the shortest
    /// route from anywhere is a cheap downhill walk (<see cref="RouteField.TryRoute"/>), so it can be asked for
    /// every frame - the guide follows the courier instantly, wherever they drive.
    /// Null when there is no road network or the destination is too far from any road.
    /// </summary>
    public static RouteField CreateField(Vector3 destination)
    {
        Grid g = GetGrid();
        if (g == null) return null;
        if (!Nearest(g, destination, out int goal))
        {
            Debug.LogWarning($"RoadPathfinder: destination {destination} is more than {SnapRadius * CellSize} m from any road.");
            return null;
        }
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float[] cost = CostToGoal(g, goal);
        int reachable = 0;
        for (int i = 0; i < cost.Length; i += 4)
            if (!float.IsPositiveInfinity(cost[i]) || !float.IsPositiveInfinity(cost[i + 1]) ||
                !float.IsPositiveInfinity(cost[i + 2]) || !float.IsPositiveInfinity(cost[i + 3])) reachable++;
        Debug.Log($"RoadPathfinder: routes to {destination} ready in {watch.ElapsedMilliseconds} ms " +
                  $"({reachable} road cells connected to it).");
        return new RouteField(g, goal, cost);
    }

    /// <summary>Corners of the shortest road route from 'from' to 'to' (null if there is none).</summary>
    public static Vector3[] FindPath(Vector3 from, Vector3 to)
    {
        RouteField field = CreateField(to);
        var corners = new List<Vector3>();
        return field != null && field.TryRoute(from, Vector3.zero, corners) ? corners.ToArray() : null;
    }

    /// <summary>The best routes to one destination, from anywhere on the roads.</summary>
    public sealed class RouteField
    {
        private readonly Grid g;
        private readonly int goal;
        private readonly float[] toGoal;   // per (cell, direction of arrival)
        private readonly List<int> cells = new List<int>();

        internal RouteField(Grid grid, int goalCell, float[] costToGoal)
        {
            g = grid;
            goal = goalCell;
            toGoal = costToGoal;
        }

        /// <summary>
        /// Fills 'corners' with the turns of the shortest route from 'position' (not including the position itself;
        /// the last point is the road spot nearest the destination). 'heading' is the way the courier is moving:
        /// turning back costs extra, so the route keeps going ahead unless a U-turn really is shorter.
        /// </summary>
        public bool TryRoute(Vector3 position, Vector3 heading, List<Vector3> corners)
        {
            corners.Clear();
            if (!Nearest(g, position, out int cell)) return false;

            int ahead = -1;
            if (new Vector2(heading.x, heading.z).sqrMagnitude > 0.01f)
                ahead = Mathf.Abs(heading.x) >= Mathf.Abs(heading.z) ? (heading.x > 0f ? 0 : 1) : (heading.z > 0f ? 2 : 3);

            // Start in whichever direction is best; turning around costs extra.
            int dir = -1;
            float best = float.PositiveInfinity;
            for (int d = 0; d < 4; d++)
            {
                float c = toGoal[cell * 4 + d] + (ahead >= 0 && d == (ahead ^ 1) ? UTurnCost : 0f);
                if (c < best) { best = c; dir = d; }
            }
            if (dir < 0) return false;

            cells.Clear();
            cells.Add(cell);
            for (int guard = g.Width * g.Height; cell != goal && guard > 0; guard--)
            {
                int x = cell % g.Width, y = cell / g.Width, nextDir = -1, nextCell = -1;
                float bestNext = float.PositiveInfinity;
                for (int nd = 0; nd < 4; nd++)
                {
                    if (nd == (dir ^ 1)) continue;
                    int nx = x + Steps[nd].x, ny = y + Steps[nd].y;
                    if (nx < 0 || ny < 0 || nx >= g.Width || ny >= g.Height) continue;
                    int n = ny * g.Width + nx;
                    if (!g.Road[n]) continue;
                    float c = 1f + g.Penalty[n] + (nd != dir ? TurnCost : 0f) + toGoal[n * 4 + nd];
                    if (c < bestNext) { bestNext = c; nextDir = nd; nextCell = n; }
                }
                if (nextCell < 0 || float.IsPositiveInfinity(bestNext)) return false;
                cell = nextCell;
                dir = nextDir;
                cells.Add(cell);
            }
            if (cell != goal) return false;

            // Keep only the corners: cells where the direction changes, plus the end. Corners right next to the
            // courier (e.g. the small jog from their lane to the middle of the street) are left out, so the guide
            // points down the street instead of sideways.
            Vector3 here = CellCenter(g, cells[0]);
            for (int i = 1; i < cells.Count - 1; i++)
            {
                if (cells[i] - cells[i - 1] == cells[i + 1] - cells[i]) continue;
                Vector3 corner = CellCenter(g, cells[i]);
                if ((corner - here).sqrMagnitude >= IgnoreCornersWithin * IgnoreCornersWithin) corners.Add(corner);
            }
            corners.Add(CellCenter(g, cells[cells.Count - 1]));
            return true;
        }
    }

    /// <summary>Forget the sampled roads (e.g. after roads were moved at runtime).</summary>
    public static void Invalidate() => grid = null;

    // ------------------------------------------------------------------ grid

    private static Grid GetGrid()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (grid != null && grid.Scene == scene && scene.isLoaded) return grid;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        grid = Build(scene);
        if (grid == null)
        {
            Debug.LogWarning("RoadPathfinder: no road network (no TrafficSystem or no 'Road...' pieces) - using the hand-made routes.");
            return null;
        }
        int roads = 0;
        foreach (bool r in grid.Road) if (r) roads++;
        Debug.Log($"RoadPathfinder: sampled {grid.Width}x{grid.Height} cells ({roads} road) in {watch.ElapsedMilliseconds} ms.");
        return grid;
    }

    private static Grid Build(Scene scene)
    {
        TrafficSystem roads = TrafficSystem.Instance;
        if (roads == null) return null;

        Bounds area = default;
        bool any = false;
        foreach (GameObject piece in HierarchyGroup.AllPieces())
        {
            if (!piece.activeInHierarchy || !piece.name.StartsWith("Road")) continue;
            foreach (var renderer in piece.GetComponentsInChildren<Renderer>())
            {
                if (any) area.Encapsulate(renderer.bounds);
                else { area = renderer.bounds; any = true; }
            }
        }
        if (!any) return null;

        var g = new Grid
        {
            Scene = scene,
            Origin = new Vector2(area.min.x, area.min.z),
            Width = Mathf.CeilToInt(area.size.x / CellSize) + 1,
            Height = Mathf.CeilToInt(area.size.z / CellSize) + 1,
        };
        int count = g.Width * g.Height;
        var sampled = new bool[count];
        for (int y = 0; y < g.Height; y++)
            for (int x = 0; x < g.Width; x++)
            {
                Vector3 p = CellCenter(g, y * g.Width + x);
                sampled[y * g.Width + x] = roads.IsRoad(new Vector3(p.x, 0f, p.z));
            }

        // Close one-cell seams between road tiles (a sample that fell exactly on a gap between two colliders).
        g.Road = (bool[])sampled.Clone();
        for (int y = 1; y < g.Height - 1; y++)
            for (int x = 1; x < g.Width - 1; x++)
            {
                int i = y * g.Width + x;
                if (sampled[i]) continue;
                if ((sampled[i - 1] && sampled[i + 1]) || (sampled[i - g.Width] && sampled[i + g.Width])) g.Road[i] = true;
            }

        // Distance (in cells) from each road cell to the nearest non-road cell, to keep routes mid-street.
        var distance = new int[count];
        var queue = new Queue<int>();
        for (int i = 0; i < count; i++)
        {
            int x = i % g.Width, y = i / g.Width;
            bool edge = x == 0 || y == 0 || x == g.Width - 1 || y == g.Height - 1;
            if (!g.Road[i] || edge) { distance[i] = 0; queue.Enqueue(i); }
            else distance[i] = int.MaxValue;
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % g.Width, y = i / g.Width;
            foreach (Vector2Int s in Steps)
            {
                int nx = x + s.x, ny = y + s.y;
                if (nx < 0 || ny < 0 || nx >= g.Width || ny >= g.Height) continue;
                int n = ny * g.Width + nx;
                if (distance[n] <= distance[i] + 1) continue;
                distance[n] = distance[i] + 1;
                queue.Enqueue(n);
            }
        }
        g.Penalty = new float[count];
        for (int i = 0; i < count; i++)
            g.Penalty[i] = Mathf.Max(0, CenterCells - distance[i]) * EdgePenalty;

        return g;
    }

    private static Vector3 CellCenter(Grid g, int cell) =>
        new Vector3(g.Origin.x + (cell % g.Width + 0.5f) * CellSize, 0f, g.Origin.y + (cell / g.Width + 0.5f) * CellSize);

    /// The road cell closest to a world point (searching outwards up to SnapRadius cells).
    private static bool Nearest(Grid g, Vector3 world, out int cell)
    {
        int cx = Mathf.FloorToInt((world.x - g.Origin.x) / CellSize);
        int cy = Mathf.FloorToInt((world.z - g.Origin.y) / CellSize);
        cell = -1;
        int bestDistance = int.MaxValue;
        for (int r = 0; r <= SnapRadius; r++)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (Mathf.Max(Mathf.Abs(x - cx), Mathf.Abs(y - cy)) != r) continue;   // ring only
                    if (x < 0 || y < 0 || x >= g.Width || y >= g.Height) continue;
                    int i = y * g.Width + x;
                    if (!g.Road[i]) continue;
                    int d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                    if (d < bestDistance) { bestDistance = d; cell = i; }
                }
            // Anything on a later ring is at least r+1 away; stop once nothing closer can turn up.
            if (cell >= 0 && bestDistance <= (r + 1) * (r + 1)) break;
        }
        return cell >= 0;
    }

    // ------------------------------------------------------------------ search

    /// Dijkstra backwards from the goal over (cell, direction of arrival) states, so turns can cost extra:
    /// the cost of the best route to the goal for a courier who arrived in 'cell' moving in 'direction'.
    private static float[] CostToGoal(Grid g, int goal)
    {
        int states = g.Width * g.Height * 4;
        var cost = new float[states];
        for (int i = 0; i < states; i++) cost[i] = float.PositiveInfinity;
        var open = new MinHeap();
        for (int d = 0; d < 4; d++)
        {
            cost[goal * 4 + d] = 0f;
            open.Push(goal * 4 + d, 0f);
        }

        while (open.Count > 0)
        {
            float here = open.PeekKey;
            int state = open.Pop();
            if (here > cost[state]) continue;   // outdated entry
            int cell = state >> 2, dir = state & 3;

            // The courier got here by moving 'dir' from the previous cell.
            int px = cell % g.Width - Steps[dir].x, py = cell / g.Width - Steps[dir].y;
            if (px < 0 || py < 0 || px >= g.Width || py >= g.Height) continue;
            int previous = py * g.Width + px;
            if (!g.Road[previous]) continue;

            float step = here + 1f + g.Penalty[cell];
            for (int d = 0; d < 4; d++)
            {
                if (d == (dir ^ 1)) continue;   // no U-turns on the spot (0/1 and 2/3 are opposites)
                float c = step + (d != dir ? TurnCost : 0f);
                int s = previous * 4 + d;
                if (c >= cost[s]) continue;
                cost[s] = c;
                open.Push(s, c);
            }
        }
        return cost;
    }

    /// Small binary min-heap of (priority, state).
    private sealed class MinHeap
    {
        private readonly List<float> keys = new List<float>();
        private readonly List<int> values = new List<int>();
        public int Count => values.Count;
        public float PeekKey => keys[0];

        public void Push(int value, float key)
        {
            keys.Add(key);
            values.Add(value);
            int i = values.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (keys[parent] <= keys[i]) break;
                Swap(i, parent);
                i = parent;
            }
        }

        public int Pop()
        {
            int top = values[0];
            int last = values.Count - 1;
            Swap(0, last);
            keys.RemoveAt(last);
            values.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < values.Count && keys[l] < keys[smallest]) smallest = l;
                if (r < values.Count && keys[r] < keys[smallest]) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
            return top;
        }

        private void Swap(int a, int b)
        {
            (keys[a], keys[b]) = (keys[b], keys[a]);
            (values[a], values[b]) = (values[b], values[a]);
        }
    }
}
