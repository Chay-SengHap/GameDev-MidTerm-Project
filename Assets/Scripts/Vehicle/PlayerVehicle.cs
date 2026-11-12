using UnityEngine;

/// <summary>
/// What the rest of the game (GameManager, HUD, traffic) needs to know about the vehicle the player drives.
/// Implemented by the car (<see cref="PlayerVehicleController"/>) and the scooter (<see cref="ScooterController"/>),
/// so either one can be wired into the game.
/// </summary>
public abstract class PlayerVehicle : MonoBehaviour
{
    /// Set to false to lock the vehicle in place (e.g. before the start or after game over).
    public virtual bool ControlsEnabled { get; set; } = true;

    /// Signed speed along the vehicle's forward axis (m/s), negative while reversing.
    public abstract float ForwardSpeed { get; }
    public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
    public abstract Rigidbody Body { get; }

    /// Puts the vehicle upright at its current position, keeping its heading.
    public abstract void ResetOntoWheels();

    /// Moves the vehicle and removes all its momentum.
    public abstract void Teleport(Vector3 position, Quaternion rotation);
}
