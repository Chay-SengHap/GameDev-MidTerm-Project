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

    /// The city piece an object belongs to: its topmost parent that is not a folder.
    public static Transform PieceRoot(Transform t)
    {
        if (t == null) return null;
        while (t.parent != null && !IsGroup(t.parent)) t = t.parent;
        return t;
    }

    public static bool IsGroup(Transform t) => t != null && t.GetComponent<HierarchyGroup>() != null;

    private static void Collect(Transform t, List<GameObject> result)
    {
        if (IsGroup(t))
        {
            foreach (Transform child in t) Collect(child, result);
        }
        else
        {
            result.Add(t.gameObject);
        }
    }
}
