using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>What the player's car hit, and how hard.</summary>
public struct VehicleImpact
{
    public Collider Other;
    /// Closing speed along the contact normal (m/s).
    public float ImpactSpeed;
    /// True when the other object is an AI traffic car.
    public bool IsTrafficVehicle;
    public Vector3 Point;
}

/// <summary>
/// Detects crashes of the vehicle it sits on and reports them as <see cref="Impact"/> events.
/// It contains no movement code and applies no gameplay rules itself: listeners such as
/// <see cref="GameManager"/> (time penalty) or the UI decide what a crash means.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class VehicleCollisionHandler : MonoBehaviour
{
    [Tooltip("Impacts slower than this (m/s) are ignored, e.g. scraping along a wall.")]
    [SerializeField] private float minImpactSpeed = 3f;
    [Tooltip("Minimum seconds between two reported impacts.")]
    [SerializeField] private float cooldown = 0.75f;
    [Tooltip("Contacts whose normal points up more than this are treated as driving over ground/curbs, not crashes.")]
    [SerializeField, Range(0f, 1f)] private float groundNormalThreshold = 0.6f;

    [Tooltip("Optional hook for designers (sound, particles...). Receives the impact speed.")]
    [SerializeField] private UnityEvent<float> onImpact = new UnityEvent<float>();

    public event Action<VehicleImpact> Impact;

    private float nextAllowedTime;

    private void OnCollisionEnter(Collision collision)
    {
        if (Time.time < nextAllowedTime || collision.contactCount == 0) return;

        ContactPoint contact = collision.GetContact(0);
        if (contact.normal.y > groundNormalThreshold) return;   // landed on / drove over something

        float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        if (speed < minImpactSpeed) return;

        nextAllowedTime = Time.time + cooldown;
        var impact = new VehicleImpact
        {
            Other = collision.collider,
            ImpactSpeed = speed,
            IsTrafficVehicle = collision.collider.GetComponentInParent<TrafficCar>() != null,
            Point = contact.point,
        };

        Impact?.Invoke(impact);
        onImpact.Invoke(speed);
    }
}
