using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the route: an ordered list of <see cref="Checkpoint"/>s. Activates one at a time and
/// announces progress. To extend the route, add more Checkpoint objects as children of this
/// object (the child order is the route order) or assign them to the list manually.
/// </summary>
[DisallowMultipleComponent]
public class CheckpointManager : MonoBehaviour
{
    [Tooltip("Route in order. Leave empty to use the Checkpoint children of this object (in hierarchy order).")]
    [SerializeField] private List<Checkpoint> checkpoints = new List<Checkpoint>();
    [Tooltip("After the last checkpoint, start again from the first instead of finishing.")]
    [SerializeField] private bool loop;
    [Tooltip("Start the route when the scene starts. Leave off when a GameManager starts it.")]
    [SerializeField] private bool beginOnStart;

    public int CurrentIndex { get; private set; } = -1;
    public int Count => checkpoints.Count;
    public bool IsComplete { get; private set; }
    public Checkpoint Current =>
        CurrentIndex >= 0 && CurrentIndex < checkpoints.Count ? checkpoints[CurrentIndex] : null;

    /// The checkpoint just reached and its index in the route.
    public event Action<Checkpoint, int> CheckpointReached;
    /// The new target (null when the route is finished).
    public event Action<Checkpoint> TargetChanged;
    public event Action RouteCompleted;

    private void Awake()
    {
        if (checkpoints.Count == 0)
            GetComponentsInChildren(false, checkpoints);
        checkpoints.RemoveAll(c => c == null);

        foreach (var cp in checkpoints) cp.Reached += OnCheckpointReached;

        if (checkpoints.Count == 0)
            Debug.LogWarning($"{name}: no checkpoints on the route. Add some with Tools > City Driving > Add Checkpoint At Scene View.", this);
    }

    private void Start()
    {
        if (beginOnStart) BeginRoute();
    }

    private void OnDestroy()
    {
        foreach (var cp in checkpoints)
            if (cp != null) cp.Reached -= OnCheckpointReached;
    }

    public void BeginRoute()
    {
        IsComplete = false;
        foreach (var cp in checkpoints) cp.SetActive(false);
        SetTarget(checkpoints.Count > 0 ? 0 : -1);
    }

    /// Appends a checkpoint at runtime (e.g. a newly spawned city area).
    public void AddCheckpoint(Checkpoint cp)
    {
        if (cp == null || checkpoints.Contains(cp)) return;
        checkpoints.Add(cp);
        cp.Reached += OnCheckpointReached;
        cp.SetActive(false);
    }

    private void OnCheckpointReached(Checkpoint cp)
    {
        if (cp != Current) return;
        int reachedIndex = CurrentIndex;
        CheckpointReached?.Invoke(cp, reachedIndex);

        int next = reachedIndex + 1;
        if (next < checkpoints.Count) SetTarget(next);
        else if (loop) SetTarget(0);
        else
        {
            IsComplete = true;
            SetTarget(-1);
            RouteCompleted?.Invoke();
        }
    }

    private void SetTarget(int index)
    {
        CurrentIndex = index;
        if (Current != null) Current.SetActive(true);
        TargetChanged?.Invoke(Current);
    }

    private void OnDrawGizmos()
    {
        // Draw the route as a line in the Scene view.
        var list = checkpoints.Count > 0 ? checkpoints : new List<Checkpoint>(GetComponentsInChildren<Checkpoint>());
        Gizmos.color = Color.cyan;
        for (int i = 0; i + 1 < list.Count; i++)
            if (list[i] != null && list[i + 1] != null)
                Gizmos.DrawLine(list[i].transform.position + Vector3.up, list[i + 1].transform.position + Vector3.up);
    }
}
