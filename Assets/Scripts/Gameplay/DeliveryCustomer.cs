using System.Linq;
using UnityEngine;

public class DeliveryCustomer : MonoBehaviour
{
    internal Transform ParcelHold { get; private set; }

    private CityPedestrian pedestrian;
    private Animator body;
    private bool hasParcel;

    internal static DeliveryCustomer Create(Transform parking, Vector3? walkingDestination = null)
    {
        var collection = Resources.Load<CityPedestrians>("CityPedestrians");
        var prefab = collection != null ? collection.characters.FirstOrDefault(character => character != null) : null;
        if (prefab == null)
        {
            Debug.LogError("The restaurant customer needs a character prefab in the CityPedestrians collection.");
            return null;
        }

        var actor = Instantiate(prefab, parking, false);
        actor.name = "Delivery Customer";
        actor.transform.localPosition = new Vector3(1.2f, 0.2f, -4.5f);
        actor.transform.localRotation = Quaternion.identity;
        if (walkingDestination.HasValue) actor.transform.position = walkingDestination.Value;
        foreach (var collider in actor.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        var customer = actor.AddComponent<DeliveryCustomer>();
        customer.body = actor.GetComponentsInChildren<Animator>().FirstOrDefault(animator => animator.isHuman);
        customer.pedestrian = actor.GetComponent<CityPedestrian>();
        if (customer.pedestrian == null) customer.pedestrian = actor.AddComponent<CityPedestrian>();
        customer.pedestrian.Initialize(new[] { actor.transform.position }, parking.position,
            collection.idle.FirstOrDefault(clip => clip != null), null, null, 0);
        customer.ParcelHold = new GameObject("Parcel Hold").transform;
        customer.ParcelHold.SetParent(actor.transform, false);
        customer.ParcelHold.localPosition = new Vector3(0f, 1.12f, 0.37f);
        return customer;
    }

    internal void Face(Vector3 point)
    {
        Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
        if (direction.sqrMagnitude <= 0.01f) return;
        pedestrian.SetLookPoint(point);
        transform.rotation = Quaternion.LookRotation(direction);
    }

    internal void ReceiveParcel() => hasParcel = true;

    private void LateUpdate()
    {
        if (!hasParcel || body == null) return;
        PoseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, -1f);
        PoseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1f);
    }

    private void PoseArm(HumanBodyBones upperBone, HumanBodyBones lowerBone, HumanBodyBones handBone, float side)
    {
        Transform upper = body.GetBoneTransform(upperBone);
        Transform lower = body.GetBoneTransform(lowerBone);
        Transform hand = body.GetBoneTransform(handBone);
        if (upper == null || lower == null || hand == null) return;

        Vector3 elbow = transform.TransformPoint(new Vector3(side * 0.26f, 1.01f, 0.13f));
        Vector3 grip = ParcelHold.TransformPoint(new Vector3(side * 0.19f, -0.05f, 0.04f));
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, grip - lower.position) * lower.rotation;
    }
}
