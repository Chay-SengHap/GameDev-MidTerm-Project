using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > City Driving > Organize Hierarchy: sorts the open scene into folders.
///
///   Camera & Lighting      cameras, lights, post-processing volumes
///   Game Systems           GameManager, route, traffic system, HUD, event system
///   Player                 the scooter with the courier (and any on-foot character)
///   City
///     Roads / Buildings / Nature / Props / Boxes   - each split by type ("Street Light", "Fir Tree"...)
///   Traffic                parked / driving cars, split by type ("Taxi", "SUV"...)
///   Other                  anything that doesn't match
///
/// Folders carry a <see cref="HierarchyGroup"/> marker, so traffic and the setup tools still find everything.
/// Nothing moves in the world (folders sit at the origin), it is one Undo step, and it is safe to run again.
/// </summary>
public static class OrganizeHierarchy
{
    private static readonly string[] TopOrder = { "Camera & Lighting", "Game Systems", "Player", "City", "Traffic", "Other" };

    private static readonly (string prefix, string folder)[] CityPrefixes =
    {
        ("Road", "Roads"),
        ("Building", "Buildings"),
        ("Natures", "Nature"),
        ("Props", "Props"),
        ("SM_Boxes", "Boxes"),
    };

    [MenuItem("Tools/City Driving/Organize Hierarchy", priority = 60)]
    private static void Organize()
    {
        Scene scene = SceneManager.GetActiveScene();
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Organize Hierarchy");

        var pieces = HierarchyGroup.Pieces(scene);
        int moved = 0;
        foreach (var piece in pieces)
        {
            string[] path = FolderPath(piece);
            Transform folder = GetFolder(scene, path);
            if (piece.transform.parent != folder)
            {
                Undo.SetTransformParent(piece.transform, folder, "Organize Hierarchy");
                moved++;
            }
        }

        RemoveEmptyFolders(scene);
        SortFolders(scene);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Organize Hierarchy: {pieces.Count} objects sorted into folders ({moved} moved). Ctrl+Z undoes it.");
    }

    // ------------------------------------------------------------------ categories

    private static string[] FolderPath(GameObject go)
    {
        if (go.GetComponentInChildren<PlayerVehicle>(true) != null || go.GetComponentInChildren<ScooterRiderPose>(true) != null ||
            go.GetComponentInChildren<PlayerController>(true) != null)
            return new[] { "Player" };

        if (go.GetComponent<Camera>() != null || go.GetComponent<Light>() != null || go.name.Contains("Volume") ||
            go.GetComponent<ReflectionProbe>() != null || go.GetComponent<LightProbeGroup>() != null)
            return new[] { "Camera & Lighting" };

        if (go.GetComponentInChildren<GameManager>(true) != null || go.GetComponentInChildren<CountdownTimer>(true) != null ||
            go.GetComponentInChildren<CheckpointManager>(true) != null || go.GetComponentInChildren<TrafficSystem>(true) != null ||
            go.GetComponentInChildren<UIManager>(true) != null || go.GetComponentInChildren<NavigationArrow>(true) != null ||
            go.GetComponent<Canvas>() != null || go.GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
            return new[] { "Game Systems" };

        string name = go.name;
        if (name.StartsWith("Vehicle"))
            return new[] { "Traffic", TypeName(name, "Vehicle") };

        foreach (var (prefix, folder) in CityPrefixes)
            if (name.StartsWith(prefix))
                return folder == "Buildings" ? new[] { "City", folder } : new[] { "City", folder, TypeName(name, prefix) };

        return new[] { "Other" };
    }

    /// "Props_Street Light (12)" -> "Street Light", "Vehicle_SUV_color02 (3)" -> "SUV", "Road Lane_01 (7)" -> "Lane_01".
    private static string TypeName(string name, string prefix)
    {
        string n = Regex.Replace(name, @"\s*\(\d+\)$", "");            // instance number
        n = Regex.Replace(n, @"_color\d+$", "", RegexOptions.IgnoreCase); // colour variants share a folder
        if (n.StartsWith(prefix)) n = n.Substring(prefix.Length);
        n = n.TrimStart('_', ' ');
        return n.Length > 0 ? n : "Misc";
    }

    // ------------------------------------------------------------------ folders

    private static Transform GetFolder(Scene scene, string[] path)
    {
        Transform parent = null;
        foreach (string part in path)
        {
            Transform found = null;
            IEnumerable<Transform> candidates = parent == null
                ? scene.GetRootGameObjects().Select(g => g.transform)
                : parent.Cast<Transform>();
            foreach (var t in candidates)
                if (t.name == part && HierarchyGroup.IsGroup(t)) { found = t; break; }

            if (found == null)
            {
                var go = new GameObject(part);
                Undo.RegisterCreatedObjectUndo(go, "Organize Hierarchy");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.AddComponent<HierarchyGroup>();
                if (parent != null) go.transform.SetParent(parent, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                found = go.transform;
            }
            parent = found;
        }
        return parent;
    }

    private static void RemoveEmptyFolders(Scene scene)
    {
        bool removed;
        do
        {
            removed = false;
            foreach (var group in Object.FindObjectsByType<HierarchyGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (group.gameObject.scene != scene || group.transform.childCount > 0) continue;
                Undo.DestroyObjectImmediate(group.gameObject);
                removed = true;
            }
        } while (removed);
    }

    /// Top folders in a fixed order, everything inside alphabetically with numbers in natural order (2 before 10).
    private static void SortFolders(Scene scene)
    {
        var roots = scene.GetRootGameObjects().Select(g => g.transform).ToList();
        int index = 0;
        foreach (string name in TopOrder)
        {
            var t = roots.FirstOrDefault(r => r.name == name && HierarchyGroup.IsGroup(r));
            if (t == null) continue;
            Undo.SetSiblingIndex(t, index++, "Organize Hierarchy");
        }

        foreach (var group in Object.FindObjectsByType<HierarchyGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (group.gameObject.scene != scene) continue;
            var children = group.transform.Cast<Transform>().OrderBy(c => HierarchyGroup.IsGroup(c) ? 0 : 1)
                                .ThenBy(c => NaturalKey(c.name), System.StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < children.Count; i++)
                if (children[i].GetSiblingIndex() != i) Undo.SetSiblingIndex(children[i], i, "Organize Hierarchy");
        }
    }

    private static string NaturalKey(string s) => Regex.Replace(s, @"\d+", m => m.Value.PadLeft(6, '0'));
}
