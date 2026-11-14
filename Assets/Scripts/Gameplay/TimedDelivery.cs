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
    private enum MissionState { WaitingForPackage, Delivering, Completed, Failed }

    [SerializeField] private Vector3[] waypoints = System.Array.Empty<Vector3>();
    [SerializeField] private Vector2 parkingSize = new Vector2(6f, 2.8f);
    [SerializeField] private Shader markerShader;

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
        customer = DeliveryCustomer.Create(transform);
        ShowObjective("Pick up the parcel");
        if (packages.IsStowed) Begin();
    }

    private void OnDestroy()
    {
        if (timer != null) timer.Expired -= Fail;
        if (packages != null)
        {
            packages.PackageStowed -= Begin;
            packages.TryDeliver = null;
            packages.CanUnload = null;
        }
        if (markerMaterial != null) Destroy(markerMaterial);
    }

    private void Begin()
    {
        if (state != MissionState.WaitingForPackage) return;
        state = MissionState.Delivering;
        waypointIndex = 0;
        routeStart = scooter.transform.position;
        timer.StartTimer();
        marker.SetActive(true);
        hud.SetNavigationVisible(true);
        hud.ShowTime(timer.Remaining);
        ShowObjective("Ride to the blue marker");
    }

    private void LateUpdate()
    {
        Transform courier = !scooter.IsMounted && scooter.Rider != null ? scooter.Rider : scooter.transform;
        bool delivering = state == MissionState.Delivering;
        if (delivering) UpdateDelivery(courier);
        bool walkingToCustomer = customer != null && delivering && readyForHandoff && packages.IsCarrying;
        hud.UpdateMinimap(courier, delivering, waypoints,
            walkingToCustomer || (delivering && IsInParkingZone()) ? waypoints.Length : waypointIndex,
            walkingToCustomer ? customer.transform.position : transform.position);

        switch (state)
        {
            case MissionState.WaitingForPackage:
                ShowObjective(packages.IsCarrying ? "Load the parcel onto the scooter" : "Pick up the parcel");
                break;
            case MissionState.Delivering: break;
            default:
                if (Keyboard.current?.rKey.wasPressedThisFrame == true) Retry();
                break;
        }
    }

    private void UpdateDelivery(Transform courier)
    {
        hud.ShowTime(timer.Remaining);
        bool inZone = IsInParkingZone();
        parkedTime = inZone && ScooterStopped() ? parkedTime + Time.deltaTime : 0f;
        readyForHandoff = parkedTime >= 0.5f;

        if (!inZone) AdvanceWaypoints(courier.position);
        if (readyForHandoff)
        {
            hud.HidePointer();
            ShowObjective(scooter.IsMounted ? "Press E to get off the scooter" :
                packages.IsStowed ? "Press E beside the scooter to take the parcel" :
                NearCustomer() ? "Press E to hand over the parcel" : "Walk to the waiting customer");
        }
        else
        {
            if (inZone) hud.HidePointer();
            else
            {
                Vector3 target = waypointIndex < waypoints.Length ? waypoints[waypointIndex] : transform.position;
                hud.PointArrow(target - courier.position, Camera.main);
            }
            ShowObjective(inZone ? "Stop the scooter in the delivery zone" : "Ride to the blue marker");
        }
    }

    private void AdvanceWaypoints(Vector3 position)
    {
        // Keep route progress independent of camera rotation and scooter steering.
        while (waypointIndex < waypoints.Length)
        {
            Vector3 junction = waypoints[waypointIndex];
            Vector3 incoming = FlatDelta(waypointIndex == 0 ? routeStart : waypoints[waypointIndex - 1], junction);
            Vector3 outgoing = FlatDelta(junction, waypointIndex + 1 < waypoints.Length ? waypoints[waypointIndex + 1] : transform.position);
            Vector3 offset = FlatDelta(junction, position);
            float alongExit = Vector3.Dot(offset, outgoing.normalized);
            bool onExit = alongExit >= 3f && alongExit <= outgoing.magnitude + 5f &&
                          (offset - outgoing.normalized * alongExit).sqrMagnitude <= 5f * 5f;
            if (offset.sqrMagnitude > 5f * 5f && !onExit && incoming.sqrMagnitude > 0.01f) break;
            waypointIndex++;
        }

    }

    private static Vector3 FlatDelta(Vector3 from, Vector3 to) => Vector3.ProjectOnPlane(to - from, Vector3.up);

    private bool IsInParkingZone()
    {
        Vector3 local = transform.InverseTransformPoint(scooter.transform.position);
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
        Finish(MissionState.Completed, "Delivery complete", $"{CountdownTimer.Format(timer.Remaining)} remaining");
        return true;
    }

    private void ShowObjective(string text)
    {
        if (objective == text) return;
        objective = text;
        hud.ShowObjective(text);
    }

    private void Fail() => Finish(MissionState.Failed, "Time’s up", "Try the delivery again.");

    private void Finish(MissionState result, string title, string detail)
    {
        if (state != MissionState.Delivering) return;
        state = result;
        timer.Pause();
        marker.SetActive(false);
        scooter.ControlsEnabled = false;
        if (scooter.Rider != null && scooter.Rider.TryGetComponent<PlayerController>(out var walking))
            walking.enabled = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        hud.SetNavigationVisible(false);
        hud.ShowResult(title, detail);
    }

    private void Retry()
    {
        if (!enabled || (state != MissionState.Completed && state != MissionState.Failed)) return;
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
