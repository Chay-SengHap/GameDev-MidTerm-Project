using System;
using UnityEngine;

public class TimedDeliveryHud : MonoBehaviour
{
    private static readonly Color Blue = new Color(0.27f, 0.75f, 1f);
    private UnityEngine.UI.Text objective, clock, resultTitle, resultDetail;
    private UnityEngine.UI.Text cashLabel, rewardLabel;
    private RectTransform vehiclePanel;
    private float rewardVisibleUntil;
    private RectTransform resultPanel, pointer;
    private float pointerAngle;
    private RectTransform canvasRoot;
    private DeliveryMinimap minimap;
    private RectTransform minimapFrame;
    private GameMenu menu;
    private Font font;
    private int lastSecond = -1;

    internal void Build(Action retry)
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("Timed Delivery HUD", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        root.transform.SetParent(transform, false);
        canvasRoot = (RectTransform)root.transform;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var header = Rect(root.transform, "Objective panel", new Vector2(0.5f, 1f),
            new Vector2(0f, -48f), new Vector2(490f, 52f));
        header.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.04f, 0.09f, 0.14f, 0.9f);
        objective = Label(header, "Objective", Vector2.zero, new Vector2(470f, 50f), 24);
        clock = Label(root.transform, "Countdown", new Vector2(0f, -104f), new Vector2(150f, 50f), 38, new Vector2(0.5f, 1f));
        var cashPanel = Rect(root.transform, "Cash panel", new Vector2(0f, 1f),
            new Vector2(24f, -316f), new Vector2(280f, 58f));
        cashPanel.pivot = new Vector2(0f, 1f);
        var cashBackground = cashPanel.gameObject.AddComponent<UnityEngine.UI.Image>();
        cashBackground.color = new Color32(12, 23, 34, 244);
        cashBackground.raycastTarget = false;
        cashLabel = Label(cashPanel, "Cash balance", Vector2.zero, new Vector2(248f, 48f), 26);
        cashLabel.alignment = TextAnchor.MiddleLeft;
        cashLabel.color = new Color32(118, 235, 165, 255);
        cashLabel.resizeTextForBestFit = true;
        cashLabel.resizeTextMinSize = 18;
        cashLabel.resizeTextMaxSize = 26;
        var garageRect = Rect(root.transform, "Vehicles button", new Vector2(1f, 1f), new Vector2(-24f, -78f), new Vector2(180f, 46f));
        var garageImage = garageRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        garageImage.color = new Color(0.09f, 0.38f, 0.63f);
        var garageButton = garageRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        garageButton.targetGraphic = garageImage;
        garageButton.onClick.AddListener(ToggleVehiclePanel);
        Label(garageRect, "Vehicles label", Vector2.zero, garageRect.sizeDelta, 22).text = "Vehicles";
        BuildVehiclePanel(root.transform);
        rewardLabel = Label(root.transform, "Delivery reward", new Vector2(0f, -245f), new Vector2(520f, 72f), 30, new Vector2(0.5f, 1f));
        rewardLabel.gameObject.SetActive(false);
        pointer = Rect(root.transform, "Delivery pointer", new Vector2(0.5f, 1f),
            new Vector2(0f, -174f), new Vector2(78f, 78f));
        var graphic = pointer.gameObject.AddComponent<DeliveryPointerGraphic>();
        graphic.color = Blue;
        graphic.raycastTarget = false;
        SetNavigationVisible(false);

        resultPanel = Rect(root.transform, "Delivery result", new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(460f, 246f));
        resultPanel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.04f, 0.09f, 0.14f, 0.97f);
        resultTitle = Label(resultPanel, "Title", new Vector2(0f, 64f), new Vector2(420f, 50f), 36);
        resultDetail = Label(resultPanel, "Detail", new Vector2(0f, 12f), new Vector2(420f, 44f), 23);
        var retryRect = Rect(resultPanel, "Retry", new Vector2(0.5f, 0.5f),
            new Vector2(0f, -63f), new Vector2(164f, 54f));
        var background = retryRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.09f, 0.38f, 0.63f);
        var button = retryRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => retry());
        Label(retryRect, "Button label", Vector2.zero, retryRect.sizeDelta, 24).text = "Retry";
        resultPanel.gameObject.SetActive(false);
    }

    internal void BuildMinimap(UnityEngine.SceneManagement.Scene scene)
    {
        menu = FindFirstObjectByType<GameMenu>();
        var frame = minimapFrame = Rect(canvasRoot, "City minimap", new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(280f, 280f));
        frame.pivot = new Vector2(0f, 1f);
        var border = frame.gameObject.AddComponent<UnityEngine.UI.Image>();
        border.color = new Color32(12, 23, 34, 244);
        border.raycastTarget = false;
        var viewport = Rect(frame, "Map viewport", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(268f, 268f));
        viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        var background = viewport.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        background.raycastTarget = false;
        background.color = new Color32(191, 199, 209, 255);
        var overlay = Rect(viewport, "Route and markers", new Vector2(0.5f, 0.5f), Vector2.zero, viewport.sizeDelta);
        minimap = overlay.gameObject.AddComponent<DeliveryMinimap>();
        minimap.raycastTarget = false;
        if (!minimap.Build(scene, background, frame)) frame.gameObject.SetActive(false);
    }

    internal void UpdateMinimap(Transform courier, bool routeActive, Vector3[] waypoints, int nextWaypoint, Vector3 destination)
    {
        if (minimap == null || !minimap.Ready) return;
        GameObject frame = minimapFrame.gameObject;
        bool visible = (menu == null || !menu.IsOpen) && !resultPanel.gameObject.activeSelf;
        if (frame.activeSelf != visible) frame.SetActive(visible);
        if (visible) minimap.Track(courier, routeActive, waypoints, nextWaypoint, destination);
    }

    internal void ShowObjective(string text) => objective.text = text;

    internal void SetCashBalance(int balance) => cashLabel.text = "Cash  $" + balance.ToString("N0");

    private void BuildVehiclePanel(Transform parent)
    {
        vehiclePanel = Rect(parent, "Vehicle selection", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520f, 300f));
        vehiclePanel.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0.04f, 0.09f, 0.14f, 0.97f);
        Label(vehiclePanel, "Garage title", new Vector2(0f, 102f), new Vector2(480f, 46f), 30).text = "Choose a vehicle";
        Label(vehiclePanel, "Scooter option", new Vector2(0f, 38f), new Vector2(460f, 48f), 25).text = "Scooter   Available";
        Label(vehiclePanel, "Car option", new Vector2(0f, -22f), new Vector2(460f, 48f), 25).text = "Car   Coming Soon";
        var closeRect = Rect(vehiclePanel, "Close garage", new Vector2(0.5f, 0.5f), new Vector2(0f, -105f), new Vector2(150f, 48f));
        var closeImage = closeRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        closeImage.color = new Color(0.09f, 0.38f, 0.63f);
        var closeButton = closeRect.gameObject.AddComponent<UnityEngine.UI.Button>();
        closeButton.targetGraphic = closeImage;
        closeButton.onClick.AddListener(ToggleVehiclePanel);
        Label(closeRect, "Close label", Vector2.zero, closeRect.sizeDelta, 22).text = "Close";
        vehiclePanel.gameObject.SetActive(false);
    }

    private void ToggleVehiclePanel() => vehiclePanel.gameObject.SetActive(!vehiclePanel.gameObject.activeSelf);

    internal void ShowDeliveryReward(int stars, int reward, float remaining, int balance)
    {
        SetCashBalance(balance);
        rewardLabel.text = "Delivery complete   " + new string('★', stars) + new string('☆', 5 - stars) +
                           "   +$" + reward + "   " + Mathf.CeilToInt(remaining) + "s left";
        rewardVisibleUntil = Time.unscaledTime + 5f;
        rewardLabel.gameObject.SetActive(true);
    }

    private void Update()
    {
        if (rewardLabel != null && rewardLabel.gameObject.activeSelf && Time.unscaledTime >= rewardVisibleUntil)
            rewardLabel.gameObject.SetActive(false);
    }

    internal void ShowTime(float remaining)
    {
        int second = Mathf.CeilToInt(remaining);
        if (second == lastSecond) return;
        lastSecond = second;
        clock.text = CountdownTimer.Format(remaining);
        clock.color = second <= 15 ? new Color(1f, 0.34f, 0.25f) : Color.white;
    }

    internal void PointArrow(Vector3 direction, Camera camera)
    {
        direction.y = 0f;
        if (camera == null || direction.sqrMagnitude < 0.25f || (menu != null && menu.IsOpen))
        {
            HidePointer();
            return;
        }

        Vector3 right = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(right, Vector3.up);
        float target = -Mathf.Atan2(Vector3.Dot(direction, right), Vector3.Dot(direction, forward)) * Mathf.Rad2Deg;
        if (!pointer.gameObject.activeSelf)
            pointerAngle = target;
        else if (Mathf.Abs(Mathf.DeltaAngle(pointerAngle, target)) > 2f)
            pointerAngle = Mathf.MoveTowardsAngle(pointerAngle, target, 420f * Time.deltaTime);

        pointer.localRotation = Quaternion.Euler(0f, 0f, pointerAngle);
        pointer.gameObject.SetActive(true);
    }

    internal void HidePointer() => pointer.gameObject.SetActive(false);

    internal void SetNavigationVisible(bool visible)
    {
        clock.gameObject.SetActive(visible);
        if (!visible) HidePointer();
    }

    internal void ShowResult(string title, string detail)
    {
        objective.transform.parent.gameObject.SetActive(false);
        resultTitle.text = title;
        resultDetail.text = detail;
        resultPanel.gameObject.SetActive(true);
    }

    private UnityEngine.UI.Text Label(Transform parent, string objectName, Vector2 position, Vector2 size,
        int fontSize, Vector2? anchor = null)
    {
        var rect = Rect(parent, objectName, anchor ?? new Vector2(0.5f, 0.5f), position, size);
        var label = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
        label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;
        label.raycastTarget = false;
        var shadow = rect.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(1f, -2f);
        return label;
    }

    private static RectTransform Rect(Transform parent, string objectName, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
internal sealed class DeliveryPointerGraphic : UnityEngine.UI.Graphic
{
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        AddArrow(mesh, 1.17f, new Color32(9, 22, 35, 255));
        AddArrow(mesh, 1f, color);
    }

    private static void AddArrow(UnityEngine.UI.VertexHelper mesh, float scale, Color32 tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector2(0f, 31f) * scale, tint, Vector2.zero);
        mesh.AddVert(new Vector2(24f, -22f) * scale, tint, Vector2.zero);
        mesh.AddVert(new Vector2(0f, -10f) * scale, tint, Vector2.zero);
        mesh.AddVert(new Vector2(-24f, -22f) * scale, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
