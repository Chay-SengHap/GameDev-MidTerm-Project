using UnityEngine;

public enum TrafficBehaviour
{
    /// Drives around the city following the road and traffic lights.
    Drive,
    /// Stays where it was placed (parked car, road block). Still a solid obstacle.
    Stationary,
}

/// <summary>
/// Optional per-vehicle settings for AI traffic. Add it to any "Vehicle..." object to override the
/// defaults from <see cref="TrafficSystem"/>. Vehicles without it simply drive with default settings.
/// Tip: select vehicles and use Tools > City Driving > Make Selected Vehicles Stationary / Driving.
/// </summary>
[DisallowMultipleComponent]
public class TrafficVehicleSettings : MonoBehaviour
{
    [SerializeField] private TrafficBehaviour behaviour = TrafficBehaviour.Drive;
    [Tooltip("Multiplies this driver's cruise speed.")]
    [SerializeField, Range(0.3f, 2f)] private float speedMultiplier = 1f;

    public TrafficBehaviour Behaviour
    {
        get => behaviour;
        set => behaviour = value;
    }

    public float SpeedMultiplier => speedMultiplier;
}
