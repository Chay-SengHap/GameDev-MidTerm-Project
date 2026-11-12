using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays game information. Only reads from the other systems (via events and properties);
/// it never changes game state. Every reference is optional, so the HUD can be trimmed freely.
/// </summary>
[DisallowMultipleComponent]
public class UIManager : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] private GameManager game;
    [SerializeField] private CountdownTimer timer;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private NavigationArrow navigation;
    [SerializeField] private PlayerVehicleController vehicle;

    [Header("HUD")]
    [SerializeField] private Text timerText;
    [SerializeField] private Text speedText;
    [SerializeField] private Text checkpointText;
    [SerializeField] private Text distanceText;
    [SerializeField] private Text messageText;

    [Header("End screen")]
    [SerializeField] private GameObject endPanel;
    [SerializeField] private Text endTitleText;
    [SerializeField] private Text endBodyText;

    [Header("Look")]
    [SerializeField] private float lowTimeWarning = 10f;
    [SerializeField] private Color normalTimeColor = Color.white;
    [SerializeField] private Color lowTimeColor = new Color(1f, 0.3f, 0.25f);
    [SerializeField] private float messageDuration = 1.6f;

    private float messageHideTime;

    private void OnEnable()
    {
        if (timer != null) timer.TimeChanged += UpdateTimer;
        if (checkpoints != null) checkpoints.TargetChanged += OnTargetChanged;
        if (game != null)
        {
            game.StateChanged += OnStateChanged;
            game.TimeAdjusted += OnTimeAdjusted;
        }
    }

    private void OnDisable()
    {
        if (timer != null) timer.TimeChanged -= UpdateTimer;
        if (checkpoints != null) checkpoints.TargetChanged -= OnTargetChanged;
        if (game != null)
        {
            game.StateChanged -= OnStateChanged;
            game.TimeAdjusted -= OnTimeAdjusted;
        }
    }

    private void Start()
    {
        if (endPanel != null) endPanel.SetActive(false);
        if (timer != null) UpdateTimer(timer.StartTime);
        UpdateCheckpointText();
        SetText(messageText, "");
    }

    private void Update()
    {
        if (vehicle != null) SetText(speedText, $"{Mathf.RoundToInt(vehicle.SpeedKmh)} km/h");

        if (navigation != null && navigation.DistanceToTarget >= 0f)
            SetText(distanceText, $"Next checkpoint: {Mathf.RoundToInt(navigation.DistanceToTarget)} m");
        else
            SetText(distanceText, "");

        if (game != null && game.State == GameState.Ready)
            ShowMessage(game.ReadyTimeLeft > 0.05f ? Mathf.CeilToInt(game.ReadyTimeLeft).ToString() : "GO!", 0.8f);

        if (messageText != null && messageText.text.Length > 0 && Time.time > messageHideTime)
            messageText.text = "";
    }

    public void ShowMessage(string message, float duration)
    {
        SetText(messageText, message);
        messageHideTime = Time.time + duration;
    }

    private void UpdateTimer(float remaining)
    {
        if (timerText == null) return;
        timerText.text = CountdownTimer.Format(remaining);
        timerText.color = remaining <= lowTimeWarning ? lowTimeColor : normalTimeColor;
    }

    private void OnTargetChanged(Checkpoint target) => UpdateCheckpointText();

    private void UpdateCheckpointText()
    {
        if (checkpoints == null || checkpoints.Count == 0) { SetText(checkpointText, ""); return; }
        int shown = checkpoints.IsComplete ? checkpoints.Count : Mathf.Max(checkpoints.CurrentIndex, 0) + 1;
        SetText(checkpointText, $"Checkpoint {shown} / {checkpoints.Count}");
    }

    private void OnTimeAdjusted(float amount, string reason)
    {
        string sign = amount >= 0f ? "+" : "-";
        ShowMessage($"{reason}  {sign}{Mathf.Abs(amount):0}s", messageDuration);
    }

    private void OnStateChanged(GameState state)
    {
        if (state == GameState.Playing) ShowMessage("GO!", 1f);
        if (state != GameState.Won && state != GameState.GameOver) return;

        SetText(messageText, "");
        if (endPanel != null) endPanel.SetActive(true);
        bool won = state == GameState.Won;
        SetText(endTitleText, won ? "Route complete!" : "Time's up!");
        string body = won && timer != null
            ? $"Time left: {CountdownTimer.Format(timer.Remaining)}"
            : "You ran out of time.";
        SetText(endBodyText, body + "\n\nPress R to restart");
    }

    private static void SetText(Text text, string value)
    {
        if (text != null && text.text != value) text.text = value;
    }
}
