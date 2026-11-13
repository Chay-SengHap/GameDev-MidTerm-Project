using System;
using UnityEngine;

/// <summary>
/// A standalone countdown. Knows nothing about the player or the UI: it only counts down and
/// raises <see cref="Expired"/> at zero. <see cref="GameManager"/> starts it and reacts to it.
/// </summary>
[DisallowMultipleComponent]
public class CountdownTimer : MonoBehaviour
{
    [Tooltip("Seconds on the clock when the timer starts.")]
    [SerializeField, Min(1f)] private float startTime = 120f;
    [Tooltip("Start automatically when the scene loads (leave off when a GameManager starts it).")]
    [SerializeField] private bool startOnAwake;

    public float Remaining { get; private set; }
    public float StartTime => startTime;
    public bool IsRunning { get; private set; }

    /// Raised every frame while running, and whenever time is added or removed.
    public event Action<float> TimeChanged;
    public event Action Expired;

    private void Awake()
    {
        Remaining = startTime;
        if (startOnAwake) StartTimer();
    }

    private void Update()
    {
        if (!IsRunning) return;

        Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
        TimeChanged?.Invoke(Remaining);
        if (Remaining <= 0f)
        {
            IsRunning = false;
            Expired?.Invoke();
        }
    }

    public void StartTimer() => StartTimer(startTime);

    public void StartTimer(float seconds)
    {
        Remaining = Mathf.Max(0f, seconds);
        IsRunning = true;
        TimeChanged?.Invoke(Remaining);
    }

    public void Pause() => IsRunning = false;

    public void Resume()
    {
        if (Remaining > 0f) IsRunning = true;
    }

    /// Adds (or with a negative value removes) time. Expires immediately if it drops to zero.
    public void AddTime(float seconds)
    {
        Remaining = Mathf.Max(0f, Remaining + seconds);
        TimeChanged?.Invoke(Remaining);
        if (IsRunning && Remaining <= 0f)
        {
            IsRunning = false;
            Expired?.Invoke();
        }
    }

    /// Formats seconds as m:ss.
    public static string Format(float seconds)
    {
        int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
        return $"{total / 60}:{total % 60:00}";
    }
}
