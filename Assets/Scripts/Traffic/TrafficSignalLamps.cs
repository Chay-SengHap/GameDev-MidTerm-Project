using UnityEngine;

/// <summary>
/// Glowing red / yellow / green lamps placed on a "Props_Traffic Signal" model's three lamp housings.
/// </summary>
public class TrafficSignalLamps : MonoBehaviour
{
    public bool NorthSouth { get; private set; }
    private Renderer red, yellow, green;

    public void Init(bool northSouth)
    {
        NorthSouth = northSouth;

        // The arm with the three lamps sticks out ~2.5 m to one side of the pole, 5.3 m up.
        // Work out which side from the mesh so it also works if the model is mirrored.
        var mf = GetComponentInChildren<MeshFilter>();
        Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(new Vector3(-1.2f, 3f, 0f), new Vector3(2.5f, 6f, 0.6f));
        float side = Mathf.Abs(b.min.x) > Mathf.Abs(b.max.x) ? -1f : 1f;
        float y = b.min.y + b.size.y * 0.877f;      // 5.28 m on the 6.02 m model
        float z = b.max.z - 0.03f;                    // on the lens side
        Transform parent = mf != null ? mf.transform : transform;

        red = TrafficSystem.CreateLamp(parent, new Vector3(side * 2.23f, y, z), 0.3f);
        yellow = TrafficSystem.CreateLamp(parent, new Vector3(side * 1.80f, y, z), 0.3f);
        green = TrafficSystem.CreateLamp(parent, new Vector3(side * 1.36f, y, z), 0.3f);
    }

    public void Show(LightState state)
    {
        if (red == null) return;
        red.sharedMaterial = TrafficSystem.LampMaterial(Color.red, state == LightState.Red);
        yellow.sharedMaterial = TrafficSystem.LampMaterial(new Color(1f, 0.65f, 0f), state == LightState.Yellow);
        green.sharedMaterial = TrafficSystem.LampMaterial(new Color(0.1f, 1f, 0.3f), state == LightState.Green);
    }
}
