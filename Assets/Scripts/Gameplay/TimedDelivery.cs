using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(CountdownTimer))]
[DefaultExecutionOrder(150)]
public class TimedDelivery : MonoBehaviour
{
    private enum MissionState { WaitingForPackage, Delivering, ReturningToPickup, Failed }

    [System.Serializable]
    private class DeliveryStop
    {
        public string label;
        public Vector3 parking;
        public float yaw;
        public Vector3[] route;
        public bool walkOnly;
        public Vector3 customerPosition;
    }

    [SerializeField] private Vector3[] waypoints = System.Array.Empty<Vector3>();
    [SerializeField] private Vector2 parkingSize = new Vector2(6f, 2.8f);
    [SerializeField] private Shader markerShader;
    [SerializeField, Min(0.5f)] private float scooterGuideDistance = 12f;
    [SerializeField] private DeliveryStop[] additionalStops = System.Array.Empty<DeliveryStop>();
    [SerializeField] private Vector3 pickupRoadApproach;
    [SerializeField] private Bounds pedestrianArea;
    [Header("Route guide")]
    [Tooltip("Plan the shortest route along the roads instead of the hand-made routes (they stay as a fallback).")]
    [SerializeField] private bool shortestRoute = true;
    [Tooltip("Write every route change to the Console (for tuning the guide).")]
    [SerializeField] private bool logRouteChanges = true;

    private MissionState state;
    private CountdownTimer timer;
    private ScooterController scooter;
    private DeliveryPackageController packages;
    private TimedDeliveryHud hud;
    private GameObject marker;
    private Material markerMaterial;
    private int waypointIndex;
    private Vector3 routeStart;
    private float parkedTime;
    private bool readyForHandoff;
    private DeliveryCustomer customer;
    private string objective;
    private int lastStop = -1;
    private string destinationLabel;
    private Vector3[] deliveryRoute;
    private Vector3[] activeRoute = System.Array.Empty<Vector3>();
    private Vector3 navigationTarget;
    private bool walkOnlyStop;
    private RoadPathfinder.RouteField routeField;
    private readonly List<Vector3> routeCorners = new List<Vector3>();
    private float lastNoRouteLog = -10f;

    private void Start()
    {
        foreach (var candidate in FindObjectsByType<ScooterController>(FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene != gameObject.scene) continue;
            scooter = candidate;
            break;
        }
        packages = scooter != null ? scooter.GetComponent<DeliveryPackageController>() : null;
        if (packages == null || markerShader == null)
        {
            Debug.LogError("Timed Delivery needs a scooter with a package controller and a destination marker shader.", this);
            enabled = false;
            return;
        }

        timer = GetComponent<CountdownTimer>();
        timer.Pause();
        timer.Expired += Fail;
        packages.PackagePickedUp += SelectDestination;
        scooter.PedestrianArea = pedestrianArea;
        packages.PackageStowed += Begin;
        packages.TryDeliver = TryHandOff;
        packages.CanUnload = () => state == MissionState.Delivering && readyForHandoff &&
                                   IsInParkingZone() && ScooterStopped();
        if (EventSystem.current == null)
        {
            var events = new GameObject("Delivery Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
        }
        hud = gameObject.AddComponent<TimedDeliveryHud>();
        hud.Build(Retry);
        hud.BuildMinimap(gameObject.scene);
        CreateDestinationMarker();
        ShowObjective("Pick up the parcel");
        if (packages.IsStowed) Begin();
    }

    private void OnDestroy()
    {
        if (timer != null) timer.Expired -= Fail;
        if (packages != null)
        {
            packages.PackageStowed -= Begin;
            packages.PackagePickedUp -= SelectDestination;
            packages.TryDeliver = null;
            packages.CanUnload = null;
        }
        if (markerMaterial != null) Destroy(markerMaterial);
    }

    private void Begin()
    {
        if (state != MissionState.WaitingForPackage) return;
        if (deliveryRoute == null) SelectDestination();
        state = MissionState.Delivering;
        SetRoute(deliveryRoute, marker.transform.position);
        timer.StartTimer();
        marker.SetActive(true);
        hud.SetNavigationVisible(true);
        hud.ShowTime(timer.Remaining);
        ShowObjective("Deliver to " + destinationLabel);
    }

    private void SelectDestination()
    {
        if (state != MissionState.WaitingForPackage || deliveryRoute != null) return;
        int count = additionalStops.Length + 1;
        int selected = Random.Range(0, count > 1 && lastStop >= 0 ? count - 1 : count);
        if (count > 1 && lastStop >= 0 && selected >= lastStop) selected++;
        lastStop = selected;

        DeliveryStop stop = selected == 0 ? null : additionalStops[selected - 1];
        destinationLabel = stop?.label ?? "the restaurant";
        deliveryRoute = stop?.route ?? waypoints;
        marker.transform.SetPositionAndRotation(stop?.parking ?? transform.position,
            stop == null ? transform.rotation : Quaternion.Euler(0f, stop.yaw, 0f));
        walkOnlyStop = stop != null && stop.walkOnly;
        customer = DeliveryCustomer.Create(marker.transform, walkOnlyStop ? stop.customerPosition : null);
        if (customer != null) customer.transform.SetParent(transform, true);
    }

    private void SetRoute(Vector3[] route, Vector3 target)
    {
        navigationTarget = target;
        waypointIndex = 0;
        routeStart = scooter.transform.position;
        // Work out the costs to this destination once; the route itself is then updated every frame.
        routeField = shortestRoute ? RoadPathfinder.CreateField(target) : null;
        activeRoute = route;
        if (routeField != null && routeField.TryRoute(routeStart, scooter.transform.forward, routeCorners))
            activeRoute = routeCorners.ToArray();
        else
        {
            if (shortestRoute)
                Debug.LogWarning($"TimedDelivery: no road route from {routeStart} to {target} - using the hand-made route.", this);
            routeField = null;   // no road route: use the hand-made one
        }
        if (logRouteChanges)
            Debug.Log($"[Route] t={Time.time:F2} new destination {target}, {(routeField != null ? "road routing" : "hand-made route")}, {activeRoute.Length} corners");
    }

    private void LateUpdate()
    {
        Transform courier = !scooter.IsMounted && scooter.Rider != null ? scooter.Rider : scooter.transform;
        bool delivering = state == MissionState.Delivering;
        Vector3 toScooter = FlatDelta(courier.position, scooter.transform.position);
        bool walkingToCustomer = customer != null && delivering && !scooter.IsMounted &&
            packages.IsCarrying && readyForHandoff;
        bool leavingWalkOnlyStop = walkOnlyStop && state == MissionState.ReturningToPickup;
        bool returnToScooter = !(walkOnlyStop && walkingToCustomer) && !scooter.IsMounted &&
            (toScooter.sqrMagnitude >= scooterGuideDistance * scooterGuideDistance || leavingWalkOnlyStop);
        if (delivering) UpdateDelivery(returnToScooter);
        if (state == MissionState.ReturningToPickup)
        {
            if (!returnToScooter) FollowRoute(courier.position, scooter.IsMounted ? ScooterHeading() : courier.forward);
            if (FlatDelta(courier.position, navigationTarget).sqrMagnitude <= 8f * 8f || packages.IsCarrying)
            {
                state = MissionState.WaitingForPackage;
                if (packages.IsCarrying) SelectDestination();
            }
            else
            {
                ShowObjective(returnToScooter ? "Return to the scooter" : "Return to the parcel shop");
            }
        }
        bool returning = state == MissionState.ReturningToPickup;
        hud.UpdateMinimap(courier, delivering || returning, activeRoute,
            walkingToCustomer || (delivering && IsInParkingZone()) ? activeRoute.Length : waypointIndex,
            walkingToCustomer ? customer.transform.position : navigationTarget);

        switch (state)
        {
            case MissionState.WaitingForPackage:
                ShowObjective(returnToScooter ? "Return to the scooter" :
                    packages.IsCarrying ? "Load parcel for " + destinationLabel : "Pick up the parcel");
                break;
            case MissionState.Delivering:
            case MissionState.ReturningToPickup: break;
            default:
                if (Keyboard.current?.rKey.wasPressedThisFrame == true) Retry();
                break;
        }

        if (walkingToCustomer && walkOnlyStop)
            hud.PointArrow(customer.transform.position - courier.position, Camera.main);
        else
            UpdatePointer(courier, delivering || returning, returnToScooter, toScooter);
    }

    private void UpdateDelivery(bool returnToScooter)
    {
        hud.ShowTime(timer.Remaining);
        bool inZone = IsInParkingZone();
        parkedTime = inZone && ScooterStopped() ? parkedTime + Time.deltaTime : 0f;
        readyForHandoff = parkedTime >= 0.5f;

        if (!inZone) FollowRoute(scooter.transform.position, ScooterHeading());
        if (returnToScooter)
        {
            ShowObjective("Return to the scooter");
        }
        else if (readyForHandoff)
        {
            ShowObjective(scooter.IsMounted ? (walkOnlyStop ? "Scooter parking. Press E to get off" : "Press E to get off the scooter") :
                packages.IsStowed ? "Press E beside the scooter to take the parcel" :
                NearCustomer() ? "Press E to hand over the parcel" : "Walk to the waiting customer");
        }
        else
        {
            ShowObjective(inZone ? "Stop the scooter in the delivery zone" : "Deliver to " + destinationLabel);
        }
    }

    private void UpdatePointer(Transform courier, bool routeActive, bool returnToScooter, Vector3 toScooter)
    {
        if (state != MissionState.Failed && returnToScooter)
        {
            hud.PointArrow(toScooter, Camera.main);
        }
        else if (routeActive && (state == MissionState.ReturningToPickup || !IsInParkingZone()))
        {
            Vector3 target = waypointIndex < activeRoute.Length ? activeRoute[waypointIndex] : navigationTarget;
            hud.PointArrow(target - courier.position, Camera.main);
        }
        else
        {
            hud.HidePointer();
        }
    }

    private void AdvanceWaypoints(Vector3 position)
    {
        // Keep route progress independent of camera rotation and scooter steering.
        while (waypointIndex < activeRoute.Length)
        {
            Vector3 junction = activeRoute[waypointIndex];
            Vector3 incoming = FlatDelta(waypointIndex == 0 ? routeStart : activeRoute[waypointIndex - 1], junction);
            Vector3 outgoing = FlatDelta(junction, waypointIndex + 1 < activeRoute.Length ? activeRoute[waypointIndex + 1] : navigationTarget);
            Vector3 offset = FlatDelta(junction, position);
            float alongExit = Vector3.Dot(offset, outgoing.normalized);
            bool onExit = alongExit >= 3f && alongExit <= outgoing.magnitude + 5f &&
                          (offset - outgoing.normalized * alongExit).sqrMagnitude <= 5f * 5f;
            if (offset.sqrMagnitude > 5f * 5f && !onExit && incoming.sqrMagnitude > 0.01f) break;
            waypointIndex++;
        }

    }

    /// Keeps the guide on the shortest route from wherever the courier is right now: missed a turn, took another
    /// street or turned around - the route changes the same frame. Without a road route, follows the hand-made one.
    private void FollowRoute(Vector3 position, Vector3 heading)
    {
        if (routeField == null || !routeField.TryRoute(position, heading, routeCorners))
        {
            if (routeField != null && logRouteChanges && Time.time - lastNoRouteLog > 1f)
            {
                lastNoRouteLog = Time.time;
                Debug.LogWarning($"[Route] t={Time.time:F2} no route from {position} (off the road network?)");
            }
            AdvanceWaypoints(position);
            return;
        }

        routeStart = position;
        waypointIndex = 0;
        if (SameCorners(activeRoute, routeCorners)) return;
        activeRoute = routeCorners.ToArray();   // a new array only when the route changed, so the minimap redraws
        if (logRouteChanges)
            Debug.Log($"[Route] t={Time.time:F2} rerouted at ({position.x:F0}, {position.z:F0}) heading ({heading.x:F1}, {heading.z:F1}): " +
                      $"{activeRoute.Length} corners, next ({activeRoute[0].x:F0}, {activeRoute[0].z:F0})");
    }

    private static bool SameCorners(Vector3[] current, List<Vector3> corners)
    {
        if (current.Length != corners.Count) return false;
        for (int i = 0; i < current.Length; i++)
            if (current[i] != corners[i]) return false;
        return true;
    }

    /// Direction the scooter is actually moving (its facing when nearly stopped).
    private Vector3 ScooterHeading()
    {
        Vector3 v = scooter.Body != null ? scooter.Body.linearVelocity : Vector3.zero;
        v.y = 0f;
        return v.sqrMagnitude > 1f ? v : scooter.transform.forward;
    }

    private static Vector3 FlatDelta(Vector3 from, Vector3 to) => Vector3.ProjectOnPlane(to - from, Vector3.up);

    private bool IsInParkingZone()
    {
        Vector3 local = marker.transform.InverseTransformPoint(scooter.transform.position);
        return Mathf.Abs(local.x) <= parkingSize.x * 0.5f &&
               Mathf.Abs(local.z) <= parkingSize.y * 0.5f && Mathf.Abs(local.y) < 1.5f;
    }

    private bool ScooterStopped() => scooter.Body != null && scooter.Body.linearVelocity.sqrMagnitude < 0.25f;

    private bool NearCustomer() => customer != null && scooter.Rider != null &&
        (scooter.Rider.position - customer.transform.position).sqrMagnitude <= 2f * 2f;

    private bool TryHandOff()
    {
        if (state != MissionState.Delivering || !readyForHandoff || scooter.IsMounted ||
            !IsInParkingZone() || !ScooterStopped() || !NearCustomer()) return false;
        customer.Face(scooter.Rider.position);
        if (!packages.GiveToCustomer(customer.ParcelHold)) return false;
        customer.ReceiveParcel();
        Destroy(customer.gameObject, 8f);
        customer = null;
        packages.RestockPickup();
        var returnRoute = new List<Vector3>(deliveryRoute);
        returnRoute.Reverse();
        if (pickupRoadApproach != Vector3.zero) returnRoute.Add(pickupRoadApproach);
        SetRoute(returnRoute.ToArray(), packages.PickupPosition);
        deliveryRoute = null;
        parkedTime = 0f;
        readyForHandoff = false;
        state = MissionState.ReturningToPickup;
        timer.Pause();
        marker.SetActive(false);
        hud.SetNavigationVisible(false);
        ShowObjective("Delivery complete. Return to the parcel shop");
        return true;
    }

    private void ShowObjective(string text)
    {
        if (objective == text) return;
        objective = text;
        hud.ShowObjective(text);
    }

    private void Fail()
    {
        if (state != MissionState.Delivering) return;
        state = MissionState.Failed;
        timer.Pause();
        marker.SetActive(false);
        scooter.ControlsEnabled = false;
        if (scooter.Rider != null && scooter.Rider.TryGetComponent<PlayerController>(out var walking))
            walking.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        hud.SetNavigationVisible(false);
        hud.ShowResult("Time's up", "Try the delivery again.");
    }

    private void Retry()
    {
        if (!enabled || state != MissionState.Failed) return;
        int sceneIndex = gameObject.scene.buildIndex;
        if (sceneIndex < 0)
        {
            Debug.LogError("Add CityScence to the build scene list to retry the delivery.", this);
            return;
        }
        enabled = false;
        SceneManager.LoadScene(sceneIndex);
    }

    private void CreateDestinationMarker()
    {
        marker = new GameObject("Blue delivery marker");
        marker.transform.SetParent(transform, false);
        markerMaterial = new Material(markerShader);
        markerMaterial.color = new Color(0.06f, 0.55f, 1f, 0.22f);
        AddMarkerPiece(PrimitiveType.Cylinder, "Destination beam", new Vector3(0f, 16f, 0f),
            new Vector3(1.8f, 16f, 1.8f));
        AddMarkerPiece(PrimitiveType.Cube, "Parking zone", new Vector3(0f, 0.02f, 0f),
            new Vector3(parkingSize.x, 0.02f, parkingSize.y));
        marker.SetActive(false);
    }

    private void AddMarkerPiece(PrimitiveType shape, string objectName, Vector3 position, Vector3 scale)
    {
        var piece = GameObject.CreatePrimitive(shape);
        piece.name = objectName;
        piece.transform.SetParent(marker.transform, false);
        piece.transform.localPosition = position;
        piece.transform.localScale = scale;
        var collider = piece.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        piece.GetComponent<Renderer>().sharedMaterial = markerMaterial;
    }
}
