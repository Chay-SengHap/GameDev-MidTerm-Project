using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>Renders an overhead view of the city and draws mission guidance in the same coordinates.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DeliveryMinimap : UnityEngine.UI.MaskableGraphic
{
    private static readonly Color32 RouteColor = new Color32(65, 200, 255, 255);
    private static readonly Color32 Ink = new Color32(9, 21, 34, 255);
    private static readonly Vector3[] NoWaypoints = System.Array.Empty<Vector3>();

    private RenderTexture cityImage;
    private Camera mapCamera;
    private Vector2 cityCenter;
    private Vector2 citySize;
    private bool captured;
    private Vector2 playerPosition;
    private Vector2 playerHeading;
    private Vector2 destinationPosition;
    private Vector3[] waypoints = NoWaypoints;
    private int nextWaypoint;
    private bool routeActive;

    internal bool Ready => mapCamera != null;

    internal bool Build(Scene scene, UnityEngine.UI.RawImage background, RectTransform frame)
    {
        Bounds city = default;
        bool found = false;
        foreach (GameObject piece in HierarchyGroup.Pieces(scene))
        {
            if (!piece.activeInHierarchy || (!piece.name.StartsWith("Road ") &&
                !piece.name.StartsWith("Building") && !piece.name.StartsWith("Natures_Grass"))) continue;

            foreach (var renderer in piece.GetComponentsInChildren<MeshRenderer>())
            {
                if (found) city.Encapsulate(renderer.bounds);
                else { city = renderer.bounds; found = true; }
            }
        }
        if (!found) return false;

        cityCenter = new Vector2(city.center.x, city.center.z);
        citySize = new Vector2(city.size.x + 12f, city.size.z + 12f);
        float mapScale = 268f / Mathf.Max(citySize.x, citySize.y);
        background.rectTransform.sizeDelta = rectTransform.sizeDelta = citySize * mapScale;
        frame.sizeDelta = background.rectTransform.sizeDelta + Vector2.one * 12f;
        float renderScale = 768f / Mathf.Max(citySize.x, citySize.y);
        int width = Mathf.RoundToInt(citySize.x * renderScale);
        int height = Mathf.RoundToInt(citySize.y * renderScale);
        cityImage = new RenderTexture(width, height, 24)
        {
            name = "City minimap image",
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Trilinear
        };
        background.texture = cityImage;

        var cameraObject = new GameObject("Overhead map camera", typeof(Camera));
        cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.SetPositionAndRotation(
            new Vector3(city.center.x, city.max.y + 65f, city.center.z), Quaternion.Euler(90f, 0f, 0f));
        mapCamera = cameraObject.GetComponent<Camera>();
        mapCamera.enabled = false;
        mapCamera.orthographic = true;
        mapCamera.orthographicSize = citySize.y * 0.5f;
        mapCamera.aspect = citySize.x / citySize.y;
        mapCamera.farClipPlane = city.size.y + 90f;
        mapCamera.clearFlags = CameraClearFlags.SolidColor;
        mapCamera.backgroundColor = Ink;
        mapCamera.allowHDR = false;
        mapCamera.useOcclusionCulling = false;
        var settings = mapCamera.GetUniversalAdditionalCameraData();
        settings.renderShadows = false;
        settings.renderPostProcessing = false;
        return true;
    }

    internal void Track(Transform courier, bool showRoute, Vector3[] route, int firstWaypoint, Vector3 destination)
    {
        if (!captured)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = cityImage };
            RenderPipeline.SubmitRenderRequest(mapCamera, request);
            cityImage.GenerateMips();
            captured = true;
        }

        Vector2 position = WorldPoint(courier.position);
        Vector2 heading = new Vector2(courier.forward.x, courier.forward.z).normalized;
        Vector2 target = WorldPoint(destination);
        if (playerPosition == position && playerHeading == heading && destinationPosition == target &&
            routeActive == showRoute && waypoints == route && nextWaypoint == firstWaypoint) return;

        playerPosition = position;
        playerHeading = heading;
        destinationPosition = target;
        routeActive = showRoute;
        waypoints = route;
        nextWaypoint = firstWaypoint;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if (!captured) return;

        Vector2 player = ClampMarker(playerPosition, 12f);
        if (routeActive)
        {
            DrawRoute(mesh, player, 7f, Ink);
            DrawRoute(mesh, player, 3.6f, RouteColor);
            Vector2 destination = ClampMarker(destinationPosition, 10f);
            Disc(mesh, destination, 8.5f, Ink);
            Disc(mesh, destination, 6.5f, RouteColor);
            Disc(mesh, destination, 2.5f, Ink);
        }

        Vector2 forward = playerHeading.sqrMagnitude > 0f ? playerHeading : Vector2.up;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Triangle(mesh, player + forward * 11f, player - forward * 8f - right * 8f, player - forward * 8f + right * 8f, Ink);
        Triangle(mesh, player + forward * 7.5f, player - forward * 6f - right * 5f, player - forward * 6f + right * 5f, Color.white);
    }

    private Vector2 WorldPoint(Vector3 world) => (new Vector2(world.x, world.z) - cityCenter) * (rectTransform.rect.width / citySize.x);

    private Vector2 ClampMarker(Vector2 point, float padding)
    {
        Rect rect = rectTransform.rect;
        return new Vector2(Mathf.Clamp(point.x, rect.xMin + padding, rect.xMax - padding),
                           Mathf.Clamp(point.y, rect.yMin + padding, rect.yMax - padding));
    }

    private void DrawRoute(UnityEngine.UI.VertexHelper mesh, Vector2 start, float width, Color32 color)
    {
        for (int i = nextWaypoint; i < waypoints.Length; i++)
        {
            Vector2 turn = WorldPoint(waypoints[i]);
            Segment(mesh, start, turn, width, color);
            Disc(mesh, turn, width * 0.5f, color);
            start = turn;
        }
        Segment(mesh, start, ClampMarker(destinationPosition, 10f), width, color);
    }

    private static void Segment(UnityEngine.UI.VertexHelper mesh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        Vector2 direction = (to - from).normalized;
        Vector2 side = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        int index = mesh.currentVertCount;
        mesh.AddVert(from - side, color, Vector2.zero);
        mesh.AddVert(from + side, color, Vector2.zero);
        mesh.AddVert(to + side, color, Vector2.zero);
        mesh.AddVert(to - side, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index, index + 2, index + 3);
    }

    private static void Disc(UnityEngine.UI.VertexHelper mesh, Vector2 center, float radius, Color32 color)
    {
        const int sides = 16;
        int index = mesh.currentVertCount;
        mesh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= sides; i++)
        {
            float angle = -i * (2f * Mathf.PI / sides);
            mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
            if (i > 0) mesh.AddTriangle(index, index + i, index + i + 1);
        }
    }

    private static void Triangle(UnityEngine.UI.VertexHelper mesh, Vector2 tip, Vector2 left, Vector2 right, Color32 color)
    {
        int index = mesh.currentVertCount;
        mesh.AddVert(tip, color, Vector2.zero);
        mesh.AddVert(right, color, Vector2.zero);
        mesh.AddVert(left, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
    }

    protected override void OnDestroy()
    {
        if (cityImage != null)
        {
            cityImage.Release();
            Destroy(cityImage);
        }
        base.OnDestroy();
    }
}
