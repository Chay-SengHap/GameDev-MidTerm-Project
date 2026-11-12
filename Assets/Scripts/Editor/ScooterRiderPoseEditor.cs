using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ScooterRiderPose))]
public class ScooterRiderPoseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        ScooterRiderPose rider = (ScooterRiderPose)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Scooter Rider Pose: Automatically positions the courier bones (hips, knees, arms, spine) into a seated scooter riding posture.",
            MessageType.Info
        );
        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
        {
            rider.ApplyPose();
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Sit on Scooter", GUILayout.Height(30)))
        {
            Undo.RecordObject(rider, "Sit on Scooter");
            rider.MountScooter();
            rider.ApplyPose();
            EditorUtility.SetDirty(rider);
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Stand Up", GUILayout.Height(30)))
        {
            Undo.RecordObject(rider, "Stand Up");
            rider.DismountScooter();
            EditorUtility.SetDirty(rider);
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Reset to Default Scooter Pose", GUILayout.Height(25)))
        {
            Undo.RecordObject(rider, "Reset Scooter Pose");
            rider.ResetToDefaultPose();
            rider.ApplyPose();
            EditorUtility.SetDirty(rider);
            SceneView.RepaintAll();
        }
    }

    [MenuItem("Tools/Scooter/Make Courier Sit on Scooter")]
    public static void AttachAndSit()
    {
        // 1. Check current selection
        GameObject targetGo = Selection.activeGameObject;

        // If nothing selected or selection isn't scooter/courier, search scene
        if (targetGo == null)
        {
            targetGo = GameObject.Find("SitPoint");
            if (targetGo == null) targetGo = GameObject.Find("delivery-scooter (1)");
            if (targetGo == null) targetGo = GameObject.Find("delivery-scooter");
            if (targetGo == null) targetGo = GameObject.Find("delivery-courier-327e30 (1)");
            if (targetGo == null) targetGo = GameObject.Find("delivery-courier-327e30");
        }

        if (targetGo == null)
        {
            EditorUtility.DisplayDialog("Scooter Pose", "Please select 'delivery-scooter' or 'delivery-courier' in the Hierarchy first.", "OK");
            return;
        }

        // Check if component already exists
        ScooterRiderPose pose = targetGo.GetComponentInChildren<ScooterRiderPose>();
        if (pose == null)
        {
            pose = Undo.AddComponent<ScooterRiderPose>(targetGo);
        }

        pose.isSitting = true;
        pose.FindBones();
        pose.ApplyPose();

        // If the courier has a legacy Animation component playing Idle, disable it so it doesn't force standing
        Animation anim = targetGo.GetComponentInChildren<Animation>();
        if (anim != null)
        {
            anim.enabled = false;
        }

        EditorUtility.SetDirty(pose);
        EditorUtility.SetDirty(targetGo);
        Selection.activeGameObject = targetGo;
        SceneView.RepaintAll();

        EditorUtility.DisplayDialog("Scooter Pose", $"Successfully applied ScooterRiderPose to '{targetGo.name}'! The player is now seated.", "OK");
    }
}
