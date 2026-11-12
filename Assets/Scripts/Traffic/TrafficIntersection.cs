using System.Collections.Generic;
using UnityEngine;

public enum LightState { Green, Yellow, Red }

/// <summary>
/// Traffic-light controller for one intersection tile (added automatically by <see cref="TrafficSystem"/>).
/// Two phases: north-south traffic, then east-west traffic, each Green -> Yellow -> all Red.
/// </summary>
public class TrafficIntersection : MonoBehaviour
{
    public float greenTime = 12f;
    public float yellowTime = 3f;
    public float allRedTime = 2f;
    [Tooltip("Distance from the intersection centre to the stop line.")]
    public float stopLineDistance = 10f;

    // Phases: 0 NS green, 1 NS yellow, 2 all red, 3 EW green, 4 EW yellow, 5 all red
    private int phase;
    private float timer;
    private readonly List<TrafficSignalLamps> signals = new List<TrafficSignalLamps>();

    public Vector3 Center => transform.position;

    private void Awake()
    {
        // Each intersection starts at a random point of its cycle so the city isn't in lockstep.
        phase = Random.value < 0.5f ? 0 : 3;
        timer = Random.Range(0f, greenTime);
    }

    public void AddSignal(TrafficSignalLamps s)
    {
        signals.Add(s);
        s.Show(GetState(s.NorthSouth));
    }

    private void Update()
    {
        timer -= Time.deltaTime;
        if (timer > 0f) return;

        phase = (phase + 1) % 6;
        timer = PhaseDuration(phase);
        foreach (var s in signals) s.Show(GetState(s.NorthSouth));
    }

    private float PhaseDuration(int p)
    {
        switch (p % 3)
        {
            case 0: return greenTime;
            case 1: return yellowTime;
            default: return allRedTime;
        }
    }

    /// <summary>Light shown to traffic travelling along the north-south (Z) axis, or east-west (X) axis.</summary>
    public LightState GetState(bool northSouth)
    {
        int own = northSouth ? 0 : 3;
        if (phase == own) return LightState.Green;
        if (phase == own + 1) return LightState.Yellow;
        return LightState.Red;
    }
}
