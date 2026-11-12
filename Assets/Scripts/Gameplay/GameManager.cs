using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum GameState { Ready, Playing, Won, GameOver }

/// <summary>
/// Runs one round: short "get ready" phase -> driving against the clock -> win or game over.
/// It is the only class that connects the other systems (timer, route, player car, crashes),
/// so those stay independent of each other.
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    [Header("Systems")]
    [SerializeField] private CountdownTimer timer;
    [SerializeField] private CheckpointManager checkpoints;
    [SerializeField] private PlayerVehicle player;
    [Tooltip("Optional. Leave empty to disable crash penalties.")]
    [SerializeField] private VehicleCollisionHandler playerCollisions;

    [Header("Rules")]
    [Tooltip("Seconds the player waits before the round starts (car is held in place).")]
    [SerializeField, Min(0f)] private float startDelay = 3f;
    [Tooltip("Seconds added to the clock for each checkpoint reached.")]
    [SerializeField] private float timeBonusPerCheckpoint = 10f;
    [Tooltip("Seconds removed for a hard crash. 0 = crashes cost nothing.")]
    [SerializeField, Min(0f)] private float crashTimePenalty = 2f;
    [Tooltip("Only impacts at least this fast (m/s) cost time.")]
    [SerializeField] private float crashPenaltyMinSpeed = 6f;
    [SerializeField] private Key restartKey = Key.R;

    public GameState State { get; private set; } = GameState.Ready;
    /// Seconds left before the round starts (only meaningful in the Ready state).
    public float ReadyTimeLeft { get; private set; }

    public event Action<GameState> StateChanged;
    /// Raised when the rules change the clock: amount in seconds and a short reason for the UI.
    public event Action<float, string> TimeAdjusted;

    private void OnEnable()
    {
        if (timer != null) timer.Expired += OnTimeExpired;
        if (checkpoints != null)
        {
            checkpoints.CheckpointReached += OnCheckpointReached;
            checkpoints.RouteCompleted += OnRouteCompleted;
        }
        if (playerCollisions != null) playerCollisions.Impact += OnPlayerImpact;
    }

    private void OnDisable()
    {
        if (timer != null) timer.Expired -= OnTimeExpired;
        if (checkpoints != null)
        {
            checkpoints.CheckpointReached -= OnCheckpointReached;
            checkpoints.RouteCompleted -= OnRouteCompleted;
        }
        if (playerCollisions != null) playerCollisions.Impact -= OnPlayerImpact;
    }

    private void Start()
    {
        if (timer == null) Debug.LogError($"{name}: GameManager needs a CountdownTimer.", this);
        if (player == null) Debug.LogWarning($"{name}: GameManager has no player vehicle assigned.", this);

        ReadyTimeLeft = startDelay;
        SetPlayerControls(false);
        SetState(GameState.Ready);
    }

    private void Update()
    {
        switch (State)
        {
            case GameState.Ready:
                ReadyTimeLeft -= Time.deltaTime;
                if (ReadyTimeLeft <= 0f) BeginRound();
                break;

            case GameState.Won:
            case GameState.GameOver:
                var kb = Keyboard.current;
                if (kb != null && kb[restartKey].wasPressedThisFrame) Restart();
                break;
        }
    }

    public void Restart()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex < 0)
        {
            Debug.LogError($"Cannot restart: scene '{scene.name}' is not in File > Build Profiles > Scene List.", this);
            return;
        }
        SceneManager.LoadScene(scene.buildIndex);
    }

    private void BeginRound()
    {
        ReadyTimeLeft = 0f;
        if (checkpoints != null) checkpoints.BeginRoute();
        if (timer != null) timer.StartTimer();
        SetPlayerControls(true);
        SetState(GameState.Playing);
    }

    private void EndRound(GameState result)
    {
        if (State != GameState.Playing) return;
        if (timer != null) timer.Pause();
        SetPlayerControls(false);
        SetState(result);
    }

    private void OnTimeExpired() => EndRound(GameState.GameOver);

    private void OnRouteCompleted() => EndRound(GameState.Won);

    private void OnCheckpointReached(Checkpoint cp, int index)
    {
        if (State != GameState.Playing || timer == null || timeBonusPerCheckpoint == 0f) return;
        // No bonus for the final checkpoint: the round ends there anyway.
        if (checkpoints != null && index >= checkpoints.Count - 1) return;
        timer.AddTime(timeBonusPerCheckpoint);
        TimeAdjusted?.Invoke(timeBonusPerCheckpoint, "Checkpoint!");
    }

    private void OnPlayerImpact(VehicleImpact impact)
    {
        if (State != GameState.Playing || timer == null || crashTimePenalty <= 0f) return;
        if (impact.ImpactSpeed < crashPenaltyMinSpeed) return;
        timer.AddTime(-crashTimePenalty);
        TimeAdjusted?.Invoke(-crashTimePenalty, impact.IsTrafficVehicle ? "Crashed into traffic!" : "Crash!");
    }

    private void SetPlayerControls(bool enabled)
    {
        if (player != null) player.ControlsEnabled = enabled;
    }

    private void SetState(GameState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }
}
