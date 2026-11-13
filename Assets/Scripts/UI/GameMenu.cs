using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Main menu and pause menu, built in code - no scene setup needed.
///
///   Main menu   shown over the city when the scene starts (the camera slowly circles the player).
///               Play / Controls / Quit.  The round (GameManager) and the player's controls wait until Play.
///   Pause menu  Esc (gamepad: Start) while playing. The game freezes and the sound pauses.
///               Resume / Restart / Controls / Main Menu / Quit.
///
/// The menu only switches other systems on and off from the outside (vehicle ControlsEnabled, the chase camera,
/// GameManager, the on-foot PlayerController) and gives back exactly what it found when it closes.
/// To change the title or colours, add this component to an object in the scene yourself.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class GameMenu : MonoBehaviour
{
    [SerializeField] private string gameTitle = "DELIVERY RUSH";
    [SerializeField] private string subtitle = "Deliver across the city";
    [SerializeField] private bool showMainMenuOnStart = true;
    [SerializeField] private Color accent = new Color(1f, 0.62f, 0.1f);

    [Header("Main menu camera")]
    [SerializeField] private float orbitRadius = 11f;
    [SerializeField] private float orbitHeight = 5f;
    [Tooltip("Degrees per second.")]
    [SerializeField] private float orbitSpeed = 7f;

    private enum MenuScreen { None, Title, Pause, Controls }

    /// Restart reloads the scene straight into the game instead of the main menu.
    private static bool skipTitleOnNextLoad;

    private MenuScreen screen = MenuScreen.None;
    private MenuScreen controlsReturnTo;
    private GameObject titlePanel, pausePanel, controlsPanel;
    private GameObject titleFirst, pauseFirst, controlsFirst;
    private Font font;

    // What the menu switched off, to give back exactly as it was.
    private PlayerVehicle vehicle;
    private VehicleCameraFollow follow;
    private GameManager game;
    private bool savedControls, savedFollowEnabled, savedGameEnabled;
    private float savedTimeScale = 1f;
    private CursorLockMode savedLock;
    private bool savedCursorVisible;
    private readonly List<Behaviour> pausedWalkers = new List<Behaviour>();
    private float orbitAngle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<GameMenu>() != null) return;
        // Only in scenes with a player (vehicle or the scooter's rider).
        if (FindFirstObjectByType<PlayerVehicle>() == null && FindFirstObjectByType<ScooterRiderPose>() == null) return;
        var go = new GameObject("Game Menu");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "Game Systems" && HierarchyGroup.IsGroup(root.transform))
            {
                go.transform.SetParent(root.transform, false);
                break;
            }
        go.AddComponent<GameMenu>();
    }

    // Start runs after every Awake and AfterSceneLoad setup, so the scooter and its chase camera exist by now.
    private void Start()
    {
        vehicle = FindFirstObjectByType<PlayerVehicle>();
        game = FindFirstObjectByType<GameManager>();
        var cam = Camera.main;
        follow = cam != null ? cam.GetComponent<VehicleCameraFollow>() : null;

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        EnsureEventSystem();
        BuildUI();

        bool title = showMainMenuOnStart && !skipTitleOnNextLoad;
        skipTitleOnNextLoad = false;
        if (title) OpenTitle();
        else ShowScreen(MenuScreen.None);
    }

    private void OnDestroy()
    {
        // Never leave the game frozen or silent (e.g. the scene is unloaded while paused).
        if (screen == MenuScreen.Pause || (screen == MenuScreen.Controls && controlsReturnTo == MenuScreen.Pause))
        {
            Time.timeScale = savedTimeScale;
            AudioListener.pause = false;
        }
    }

    private void Update()
    {
        bool back = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                    (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
        if (back)
        {
            switch (screen)
            {
                case MenuScreen.None: Pause(); break;
                case MenuScreen.Pause: Resume(); break;
                case MenuScreen.Controls: CloseControls(); break;
            }
        }

        // Keyboard / gamepad: keep something selected so arrows and Enter work after a mouse click elsewhere.
        if (screen != MenuScreen.None && EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null)
            EventSystem.current.SetSelectedGameObject(FirstButton(screen));
    }

    private void LateUpdate()
    {
        if (screen != MenuScreen.Title && !(screen == MenuScreen.Controls && controlsReturnTo == MenuScreen.Title)) return;
        var cam = Camera.main;
        if (cam == null) return;

        // Slow circle around the player while the main menu is open.
        Vector3 center = vehicle != null ? vehicle.transform.position : transform.position;
        orbitAngle += orbitSpeed * Time.unscaledDeltaTime;
        Vector3 offset = Quaternion.Euler(0f, orbitAngle, 0f) * new Vector3(0f, orbitHeight, -orbitRadius);
        cam.transform.position = center + offset;
        cam.transform.LookAt(center + Vector3.up * 1.2f);
    }

    // ------------------------------------------------------------------ main menu

    private void OpenTitle()
    {
        if (vehicle != null)
        {
            savedControls = vehicle.ControlsEnabled;
            vehicle.ControlsEnabled = false;
            orbitAngle = vehicle.transform.eulerAngles.y + 150f;
        }
        if (follow != null) { savedFollowEnabled = follow.enabled; follow.enabled = false; }
        if (game != null) { savedGameEnabled = game.enabled; game.enabled = false; }   // round starts on Play
        SaveCursor();
        ShowCursor();
        ShowScreen(MenuScreen.Title);
    }

    private void Play()
    {
        ShowScreen(MenuScreen.None);
        if (follow != null)
        {
            follow.enabled = savedFollowEnabled;
            if (follow.enabled) follow.Target = follow.Target;   // snap back behind the player
        }
        if (game != null) game.enabled = savedGameEnabled;   // GameManager runs its countdown and enables controls
        else if (vehicle != null) vehicle.ControlsEnabled = savedControls;
        RestoreCursor();
    }

    // ------------------------------------------------------------------ pause

    private void Pause()
    {
        savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (vehicle != null) { savedControls = vehicle.ControlsEnabled; vehicle.ControlsEnabled = false; }
        // The on-foot character reads keys and the mouse itself (and re-locks the cursor on click): hold it.
        pausedWalkers.Clear();
        foreach (var walker in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
            if (walker.enabled) { walker.enabled = false; pausedWalkers.Add(walker); }

        SaveCursor();
        ShowCursor();
        ShowScreen(MenuScreen.Pause);
    }

    private void Resume()
    {
        ShowScreen(MenuScreen.None);
        Time.timeScale = savedTimeScale;
        AudioListener.pause = false;
        if (vehicle != null) vehicle.ControlsEnabled = savedControls;
        foreach (var walker in pausedWalkers) if (walker != null) walker.enabled = true;
        pausedWalkers.Clear();
        RestoreCursor();
    }

    private void Restart() => Reload(skipTitle: true);

    private void BackToMainMenu() => Reload(skipTitle: false);

    private void Reload(bool skipTitle)
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        screen = MenuScreen.None;
        skipTitleOnNextLoad = skipTitle;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0)
        {
            SceneManager.LoadScene(scene.buildIndex);
            return;
        }
#if UNITY_EDITOR
        // Scene not in the build list yet: still reload it when testing in the editor.
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
        Debug.LogError($"Cannot reload '{scene.name}': add it to File > Build Profiles > Scene List.");
#endif
    }

    private static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ controls screen

    private void OpenControls()
    {
        controlsReturnTo = screen;
        ShowScreen(MenuScreen.Controls);
    }

    private void CloseControls() => ShowScreen(controlsReturnTo);

    // ------------------------------------------------------------------ helpers

    private void ShowScreen(MenuScreen s)
    {
        screen = s;
        titlePanel.SetActive(s == MenuScreen.Title);
        pausePanel.SetActive(s == MenuScreen.Pause);
        controlsPanel.SetActive(s == MenuScreen.Controls);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s == MenuScreen.None ? null : FirstButton(s));
    }

    private GameObject FirstButton(MenuScreen s) =>
        s == MenuScreen.Title ? titleFirst : s == MenuScreen.Pause ? pauseFirst : s == MenuScreen.Controls ? controlsFirst : null;

    private void SaveCursor()
    {
        savedLock = Cursor.lockState;
        savedCursorVisible = Cursor.visible;
    }

    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreCursor()
    {
        Cursor.lockState = savedLock;
        Cursor.visible = savedCursorVisible;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
        var module = es.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
    }

    // ------------------------------------------------------------------ UI building

    private void BuildUI()
    {
        var canvasGo = new GameObject("Menu Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;   // above the HUD
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)canvasGo.transform;

        // Text only, centred, no panels or dimming: the look comes from outlines, shadows,
        // a decorative divider and the selected item glowing in the accent colour between » « markers.

        // Main menu
        titlePanel = NewColumn("Main Menu", root);
        AddTitle(titlePanel.transform, gameTitle, 110);
        AddLabel(titlePanel.transform, subtitle.ToUpperInvariant(), 26, SubtleColor, FontStyle.Italic, 40f);
        AddDivider(titlePanel.transform);
        titleFirst = AddItem(titlePanel.transform, "Play", Play);
        AddItem(titlePanel.transform, "Controls", OpenControls);
        AddItem(titlePanel.transform, "Quit", Quit);

        // Pause menu
        pausePanel = NewColumn("Pause Menu", root);
        AddTitle(pausePanel.transform, "PAUSED", 96);
        AddDivider(pausePanel.transform);
        pauseFirst = AddItem(pausePanel.transform, "Resume", Resume);
        AddItem(pausePanel.transform, "Restart", Restart);
        AddItem(pausePanel.transform, "Controls", OpenControls);
        AddItem(pausePanel.transform, "Main Menu", BackToMainMenu);
        AddItem(pausePanel.transform, "Quit", Quit);

        // Controls list
        controlsPanel = NewColumn("Controls", root);
        AddTitle(controlsPanel.transform, "CONTROLS", 80);
        AddDivider(controlsPanel.transform);
        string key = "#" + ColorUtility.ToHtmlStringRGB(accent);
        AddLabel(controlsPanel.transform,
            $"<size=30><color={key}>SCOOTER</color></size>\n" +
            "W / Shift  Throttle     S  Brake / Reverse     A / D  Steer\n" +
            "Space  Rear brake     E  Get on / off     H  Horn     L  Light     F  Upright\n\n" +
            $"<size=30><color={key}>ON FOOT</color></size>\n" +
            "W A S D  Walk     Shift  Run     Space  Jump     C / Ctrl  Crouch     Mouse  Look\n\n" +
            $"<size=30><color={key}>MENU</color></size>\n" +
            "Esc / Start  Pause",
            26, Color.white, FontStyle.Bold, 330f);
        AddSpacer(controlsPanel.transform, 10f);
        controlsFirst = AddItem(controlsPanel.transform, "Back", CloseControls);

        ShowScreen(MenuScreen.None);
    }

    private static readonly Color SubtleColor = new Color(1f, 1f, 1f, 0.85f);

    /// An invisible, centred column that stacks its children (no background).
    private static GameObject NewColumn(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(1400f, 0f);

        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return go;
    }

    /// Big title: accent colour, thick dark outline and a long drop shadow, letters spaced apart.
    private void AddTitle(Transform parent, string text, int size)
    {
        var t = AddLabel(parent, Spaced(text.ToUpperInvariant()), size, accent, FontStyle.Bold, size * 1.3f);
        AddOutline(t.gameObject, new Color(0.08f, 0.04f, 0f, 1f), 3f);
        var shadow = t.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        shadow.effectDistance = new Vector2(6f, -7f);
    }

    /// "———  • • •  ———" line between the title and the items.
    private void AddDivider(Transform parent)
    {
        string a = "#" + ColorUtility.ToHtmlStringRGB(accent);
        AddLabel(parent, $"———————   <color={a}>•   •   •</color>   ———————", 26, SubtleColor, FontStyle.Bold, 56f);
    }

    private Text AddLabel(Transform parent, string text, int size, Color color, FontStyle style, float height)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        go.AddComponent<LayoutElement>().preferredHeight = height;
        // Every text gets a dark edge so it reads over bright streets and sky without a background.
        AddOutline(go, new Color(0f, 0f, 0f, 0.85f), 2f);
        return t;
    }

    private static void AddOutline(GameObject go, Color color, float width)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(width, -width);
    }

    private static void AddSpacer(Transform parent, float height)
    {
        var go = new GameObject("Space", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().preferredHeight = height;
    }

    private static readonly Vector2 ItemSize = new Vector2(480f, 70f);
    private const int ItemCorner = 30;            // rounded corner radius (px) - nearly a pill
    private static Sprite fillSprite, ringSprite;

    /// One button: a dark rounded "pill" with an accent border and a soft shadow, the label on top.
    /// Each button has its own background; there is still no panel behind the menu.
    private GameObject AddItem(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        string upper = label.ToUpperInvariant();

        // Row in the column (full width); the button sits centred in it at a fixed size.
        var row = new GameObject(label + " Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        row.AddComponent<LayoutElement>().preferredHeight = ItemSize.y + 16f;

        var go = new GameObject(label + " Button", typeof(RectTransform));
        go.transform.SetParent(row.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = ItemSize;

        var fill = go.AddComponent<Image>();
        fill.sprite = RoundedSprite(ring: false);
        fill.type = Image.Type.Sliced;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        shadow.effectDistance = new Vector2(0f, -6f);

        var borderGo = new GameObject("Border", typeof(RectTransform));
        borderGo.transform.SetParent(go.transform, false);
        Stretch((RectTransform)borderGo.transform);
        var border = borderGo.AddComponent<Image>();
        border.sprite = RoundedSprite(ring: true);
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;

        var t = AddLabel(go.transform, upper, 36, Color.white, FontStyle.Bold, ItemSize.y);
        Destroy(t.GetComponent<LayoutElement>());
        Stretch(t.rectTransform);

        var button = go.AddComponent<Button>();
        button.transition = Selectable.Transition.None;   // MenuItem does the look
        button.targetGraphic = fill;
        button.onClick.AddListener(onClick);
        go.AddComponent<MenuItem>().Init(t, fill, border, upper, accent);
        return go;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    /// White rounded rectangle (filled, or just a 3 px ring), drawn in code and 9-sliced so it fits any size.
    private static Sprite RoundedSprite(bool ring)
    {
        if (!ring && fillSprite != null) return fillSprite;
        if (ring && ringSprite != null) return ringSprite;

        const int size = ItemCorner * 2 + 4;
        const float r = ItemCorner, thickness = 3f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = ring ? "Menu Button Ring" : "Menu Button Fill",
        };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance to the rounded rectangle's edge (negative inside), with 1 px anti-aliasing.
                float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - r;
                float a = Mathf.Clamp01(0.5f - d);
                if (ring) a *= Mathf.Clamp01(d + thickness + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px);
        tex.Apply();
        var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                                   SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
        if (ring) ringSprite = sprite; else fillSprite = sprite;
        return sprite;
    }

    private static string Spaced(string s) => string.Join(" ", s.ToCharArray());

    /// Look of one button.
    ///   Idle:     dark see-through pill, thin accent border, white text.
    ///   Selected: (keyboard, gamepad or mouse hover) solid accent pill, white border, dark text,
    ///             » « markers and a gentle pulse.
    /// Uses unscaled time so it animates while the game is paused.
    private class MenuItem : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler
    {
        private static readonly Color IdleFill = new Color(0.05f, 0.06f, 0.09f, 0.78f);
        private static readonly Color DarkText = new Color(0.1f, 0.06f, 0.02f);

        private Text text;
        private Image fill, border;
        private Outline textOutline;
        private string label;
        private Color accent;
        private bool selected;

        public void Init(Text t, Image fillImage, Image borderImage, string itemLabel, Color accentColor)
        {
            text = t;
            fill = fillImage;
            border = borderImage;
            textOutline = t.GetComponent<Outline>();
            label = itemLabel;
            accent = accentColor;
            Apply(false);
        }

        public void OnSelect(BaseEventData _) => Apply(true);
        public void OnDeselect(BaseEventData _) => Apply(false);

        // Hovering with the mouse selects the item, so mouse and keyboard never show two highlights.
        public void OnPointerEnter(PointerEventData _)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        private void OnDisable() { if (text != null) Apply(false); }

        private void Apply(bool on)
        {
            selected = on;
            text.text = on ? $"»  {label}  «" : label;
            text.color = on ? DarkText : Color.white;
            if (textOutline != null) textOutline.enabled = !on;   // dark text on orange needs no outline
            fill.color = on ? accent : IdleFill;
            border.color = on ? Color.white : new Color(accent.r, accent.g, accent.b, 0.9f);
            transform.localScale = Vector3.one;
        }

        private void Update()
        {
            if (!selected) return;
            float s = 1.06f + 0.02f * Mathf.Sin(Time.unscaledTime * 5f);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
