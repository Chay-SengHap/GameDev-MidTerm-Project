using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads the driver's input (keyboard or gamepad) and exposes it as simple values.
/// Kept separate from <see cref="PlayerVehicleController"/> so the vehicle physics never talk to
/// input devices directly (an AI driver or replay could feed the controller instead).
///
///   W / Up arrow   - accelerate          (gamepad: right trigger)
///   S / Down arrow - brake, then reverse (gamepad: left trigger)
///   A D / arrows   - steer               (gamepad: left stick)
///   Space          - handbrake           (gamepad: south button)
///   F              - put the car back on its wheels (gamepad: north button)
/// </summary>
[DisallowMultipleComponent]
public class VehicleInput : MonoBehaviour
{
    [Tooltip("Gamepad stick values below this are ignored.")]
    [SerializeField, Range(0f, 0.5f)] private float stickDeadZone = 0.15f;

    /// -1 (full reverse/brake) .. +1 (full throttle).
    public float Throttle { get; private set; }
    /// -1 (left) .. +1 (right).
    public float Steer { get; private set; }
    public bool Handbrake { get; private set; }
    /// True only on the frame the reset key was pressed.
    public bool ResetPressed { get; private set; }

    private void Update()
    {
        float throttle = 0f, steer = 0f;
        bool handbrake = false, reset = false;

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
            handbrake |= kb.spaceKey.isPressed;
            reset |= kb.fKey.wasPressedThisFrame;
        }

        var pad = Gamepad.current;
        if (pad != null)
        {
            throttle += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            float stick = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(stick) > stickDeadZone) steer += stick;
            handbrake |= pad.buttonSouth.isPressed;
            reset |= pad.buttonNorth.wasPressedThisFrame;
        }

        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = handbrake;
        ResetPressed = reset;
    }
}
