using UnityEngine;

public class DeliveryCustomer : MonoBehaviour
{
    internal Transform ParcelHold { get; private set; }
    private Transform leftArm, rightArm;
    private Material[] materials;

    internal static DeliveryCustomer Create(Transform parking)
    {
        var actor = new GameObject("Restaurant Customer");
        actor.transform.SetParent(parking, false);
        actor.transform.localPosition = new Vector3(1.2f, 0.2f, -4.5f);
        var customer = actor.AddComponent<DeliveryCustomer>();
        customer.Build();
        customer.Face(parking.position);
        return customer;
    }

    internal void Face(Vector3 point)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(direction);
    }

    internal void ReceiveParcel()
    {
        leftArm.localRotation = Quaternion.Euler(-70f, 0f, 15f);
        rightArm.localRotation = Quaternion.Euler(-70f, 0f, -15f);
    }

    private void Build()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        materials = new[]
        {
            new Material(shader) { color = new Color(0.15f, 0.46f, 0.32f) },
            new Material(shader) { color = new Color(0.16f, 0.19f, 0.28f) },
            new Material(shader) { color = new Color(0.69f, 0.44f, 0.31f) },
            new Material(shader) { color = new Color(0.12f, 0.08f, 0.06f) }
        };
        Part(transform, "Jacket", new Vector3(0f, 1.12f, 0f), new Vector3(0.48f, 0.55f, 0.28f), 0);
        Part(transform, "Head", new Vector3(0f, 1.58f, 0f), new Vector3(0.31f, 0.36f, 0.29f), 2);
        Part(transform, "Hair", new Vector3(0f, 1.76f, -0.015f), new Vector3(0.33f, 0.11f, 0.3f), 3);
        foreach (float side in new[] { -1f, 1f })
        {
            Part(transform, "Trouser leg", new Vector3(side * 0.12f, 0.48f, 0f), new Vector3(0.19f, 0.7f, 0.22f), 1);
            Part(transform, "Shoe", new Vector3(side * 0.12f, 0.09f, 0.06f), new Vector3(0.2f, 0.18f, 0.32f), 3);
            Part(transform, "Eye", new Vector3(side * 0.069f, 1.62f, 0.149f), new Vector3(0.035f, 0.035f, 0.02f), 3);
            Transform arm = new GameObject("Shoulder").transform;
            arm.SetParent(transform, false);
            arm.localPosition = new Vector3(side * 0.31f, 1.35f, 0f);
            Part(arm, "Sleeve", new Vector3(0f, -0.19f, 0f), new Vector3(0.15f, 0.4f, 0.17f), 0);
            Part(arm, "Hand", new Vector3(0f, -0.44f, 0f), new Vector3(0.14f, 0.14f, 0.15f), 2);
            if (side < 0f) leftArm = arm;
            else rightArm = arm;
        }
        ParcelHold = new GameObject("Parcel Hold").transform;
        ParcelHold.SetParent(transform, false);
        ParcelHold.localPosition = new Vector3(0f, 1.28f, 0.48f);
    }

    private void Part(Transform parent, string partName, Vector3 position, Vector3 scale, int material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        part.GetComponent<Renderer>().sharedMaterial = materials[material];
    }

    private void OnDestroy()
    {
        if (materials == null) return;
        foreach (var material in materials) Destroy(material);
    }
}
