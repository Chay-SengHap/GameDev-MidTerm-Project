using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Marks an empty object as a folder in the Hierarchy (e.g. "City/Roads/Road Lane").
/// Scripts that look for city pieces by name (traffic, setup tools) see through these folders, so the scene
/// can be organised freely. Create them with Tools > City Driving > Organize Hierarchy.
/// Keep folders at position 0, rotation 0, scale 1 so they never move their contents.
/// </summary>
[DisallowMultipleComponent]
public class HierarchyGroup : MonoBehaviour
{
    /// The top-level city pieces of a scene: root objects, plus the contents of folders (recursively).
    public static List<GameObject> Pieces(Scene scene)
    {
        var result = new List<GameObject>();
        if (!scene.isLoaded) return result;
        foreach (var root in scene.GetRootGameObjects()) Collect(root.transform, result);
        return result;
    }

    /// Pieces of every loaded scene.
    public static List<GameObject> AllPieces()
    {
        var result = new List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++) result.AddRange(Pieces(SceneManager.GetSceneAt(i)));
        return result;
    }

    private static readonly string[] PiecePrefixes =
    {
        "Road", "Vehicle", "Building", "Props", "Natures", "SM_Boxes", "Citizen"
    };

    /// The city piece an object belongs to: its topmost parent that is not a folder or container.
    public static Transform PieceRoot(Transform t)
    {
        if (t == null) return null;
        while (t.parent != null && !IsContainer(t.parent)) t = t.parent;
        return t;
    }

    public static bool IsGroup(Transform t) => t != null && t.GetComponent<HierarchyGroup>() != null;

    /// True for HierarchyGroup folders and plain container GameObjects (e.g. prefab road rows) holding city pieces.
    public static bool IsContainer(Transform t)
    {
        if (t == null) return false;
        if (IsGroup(t)) return true;
        if (IsPieceName(t.name) || t.GetComponents<Component>().Length != 1) return false;
        foreach (Transform child in t)
            if (IsPieceName(child.name)) return true;
        return false;
    }

    private static bool IsPieceName(string name)
    {
        foreach (string prefix in PiecePrefixes)
            if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void Collect(Transform t, List<GameObject> result)
    {
        if (IsContainer(t))
        {
            foreach (Transform child in t) Collect(child, result);
        }
        else
        {
            result.Add(t.gameObject);
        }
    }
}
