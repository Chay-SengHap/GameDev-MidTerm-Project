using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Navigation-style minimap (like a phone's map app while driving): an overhead picture of the city, zoomed in on
/// the courier, turned so the direction they are moving points up, and moved every frame so the courier stays near
/// the bottom-centre. The route line is drawn from the courier along the turns to the destination, so it changes
/// the moment the route does. It zooms out a little at speed to see further ahead.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class DeliveryMinimap : UnityEngine.UI.MaskableGraphic
{
    private static readonly Color32 RouteColor = new Color32(65, 200, 255, 255);
    private static readonly Color32 Ink = new Color32(9, 21, 34, 255);
    private static readonly Vector3[] NoWaypoints = System.Array.Empty<Vector3>();

    // View
    private const float CloseView = 110f;          // metres across the minimap when standing still
    private const float FarView = 170f;            // ...and at full speed
    private const float FullSpeed = 15f;           // m/s
    private const float CourierBelowCentre = 0.22f; // courier sits this far (fraction of the view) below the centre

    private RenderTexture cityImage;
    private Camera mapCamera;
    private Vector2 cityCenter;
    private Vector2 citySize;
    private bool captured;
    private RectTransform viewport, map;
    private float viewMetres = CloseView, mapAngle;
    private Vector3 lastCourierPosition;
    private bool hasLastPosition;

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

        // Sharp enough to zoom in on: about 6 pixels per metre.
        float renderScale = Mathf.Min(6f, 4096f / Mathf.Max(citySize.x, citySize.y));
        cityImage = new RenderTexture(Mathf.RoundToInt(citySize.x * renderScale), Mathf.RoundToInt(citySize.y * renderScale), 24)
        {
            name = "City minimap image",
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };

        // The fixed window (masked) holds a moving, turning "map" with the city picture and this route overlay.
        viewport = (RectTransform)transform.parent;
        background.texture = null;
        background.color = Ink;   // shows outside the city

        map = new GameObject("Moving map", typeof(RectTransform)).GetComponent<RectTransform>();
        map.SetParent(viewport, false);
        map.anchorMin = map.anchorMax = map.pivot = new Vector2(0.5f, 0.5f);
        map.SetAsFirstSibling();

        var picture = new GameObject("City picture", typeof(RectTransform)).AddComponent<UnityEngine.UI.RawImage>();
        picture.rectTransform.SetParent(map, false);
        Stretch(picture.rectTransform);
        picture.texture = cityImage;
        picture.color = new Color32(191, 199, 209, 255);
        picture.raycastTarget = false;

        rectTransform.SetParent(map, false);
        Stretch(rectTransform);
        SetZoom(CloseView);

        var cameraObject = new GameObject("Overhead map camera", typeof(Camera));
        cameraObject.transform.SetParent(viewport, false);
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

        FollowCourier(courier);

        // The overlay (route, pin, courier arrow) is in map coordinates: rebuild it only when something moved.
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

    /// Zoom with speed, turn the map so the courier's direction is up and slide it so the courier stays in view.
    private void FollowCourier(Transform courier)
    {
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        Vector3 now = courier.position;
        float speed = hasLastPosition ? Vector3.ProjectOnPlane(now - lastCourierPosition, Vector3.up).magnitude / dt : 0f;
        Vector3 moved = Vector3.ProjectOnPlane(now - lastCourierPosition, Vector3.up);
        lastCourierPosition = now;
        hasLastPosition = true;

        float wantedView = Mathf.Lerp(CloseView, FarView, Mathf.Clamp01(speed / FullSpeed));
        SetZoom(Mathf.Lerp(viewMetres, wantedView, 1f - Mathf.Exp(-1.5f * dt)));

        // Heading up: the direction of travel while moving, the way the courier faces when (nearly) still.
        Vector3 facing = speed > 1.5f && moved.sqrMagnitude > 0.0001f ? moved : courier.forward;
        if (new Vector2(facing.x, facing.z).sqrMagnitude > 0.0001f)
        {
            float wanted = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            mapAngle = Mathf.LerpAngle(mapAngle, wanted, 1f - Mathf.Exp(-8f * dt));
        }
        map.localRotation = Quaternion.Euler(0f, 0f, mapAngle);

        Vector2 courierOnMap = WorldPoint(now);
        Vector2 focus = new Vector2(0f, -viewport.rect.height * CourierBelowCentre);
        map.anchoredPosition = focus - (Vector2)(map.localRotation * courierOnMap);
    }

    private void SetZoom(float metresAcross)
    {
        viewMetres = metresAcross;
        float pixelsPerMetre = viewport.rect.width / metresAcross;
        map.sizeDelta = citySize * pixelsPerMetre;
    }

    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        if (!captured) return;

        // Sizes are in screen pixels (the map is resized, never scaled), so they look the same at any zoom.
        Vector2 player = playerPosition;
        if (routeActive)
        {
            DrawRoute(mesh, player, 9f, Ink);
            DrawRoute(mesh, player, 5.5f, RouteColor);
            Disc(mesh, destinationPosition, 10f, Ink);
            Disc(mesh, destinationPosition, 8f, RouteColor);
            Disc(mesh, destinationPosition, 3f, Ink);
        }

        Vector2 forward = playerHeading.sqrMagnitude > 0f ? playerHeading : Vector2.up;
        Vector2 right = new Vector2(forward.y, -forward.x);
        Disc(mesh, player, 13f, new Color32(65, 200, 255, 70));
        Triangle(mesh, player + forward * 12f, player - forward * 8f - right * 9f, player - forward * 8f + right * 9f, Ink);
        Triangle(mesh, player + forward * 8.5f, player - forward * 6f - right * 6f, player - forward * 6f + right * 6f, Color.white);
    }

    private Vector2 WorldPoint(Vector3 world) => (new Vector2(world.x, world.z) - cityCenter) * (rectTransform.rect.width / citySize.x);

    private void DrawRoute(UnityEngine.UI.VertexHelper mesh, Vector2 start, float width, Color32 color)
    {
        for (int i = nextWaypoint; i < waypoints.Length; i++)
        {
            Vector2 turn = WorldPoint(waypoints[i]);
            Segment(mesh, start, turn, width, color);
            Disc(mesh, turn, width * 0.5f, color);
            start = turn;
        }
        Segment(mesh, start, destinationPosition, width, color);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
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
