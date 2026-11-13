using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SetupCityPedestrians
{
    private const string CollectionPath = "Assets/Resources/CityPedestrians.asset";

    [MenuItem("Tools/City Driving/Set Up Casual Pedestrians")]
    public static void SetUp()
    {
        var paths = AssetDatabase.FindAssets("", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path => Normalize(path).Contains("casualcharactercollection3"))
            .ToArray();
        var characters = paths
            .Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(IsCharacter)
            .OrderBy(prefab => prefab.name)
            .ToArray();
        if (characters.Length == 0)
        {
            Debug.LogWarning("Import Hodaart's Casual Character Collection 3, including its character prefabs, then run this command again.");
            return;
        }

        var clips = paths.Where(path => path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) ||
                                       path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath)
            .OfType<AnimationClip>()
            .Concat(characters.SelectMany(character =>
                character.GetComponentInChildren<Animator>(true).runtimeAnimatorController?.animationClips ??
                Array.Empty<AnimationClip>()))
            .Distinct()
            .ToArray();
        var walk = clips.Where(clip => Normalize(clip.name).Contains("walk")).ToArray();
        var idle = clips.Where(clip => Normalize(clip.name).Contains("idle") &&
            !Normalize(clip.name).Contains("facial")).ToArray();
        var bodyIdle = idle.Where(clip => Normalize(clip.name).EndsWith("idle1") ||
                                          Normalize(clip.name).EndsWith("idle2")).ToArray();
        if (bodyIdle.Length > 0) idle = bodyIdle;
        var greeting = clips.FirstOrDefault(clip => Normalize(clip.name) == "hi") ??
            clips.FirstOrDefault(clip => Normalize(clip.name).EndsWith("hi") ||
                                         Normalize(clip.name).Contains("greet"));
        if (walk.Length == 0 || idle.Length == 0)
        {
            Debug.LogWarning("The pedestrian setup needs the collection's Walk and Idle clips. Import those clips and run the command again.");
            return;
        }

        var collection = AssetDatabase.LoadAssetAtPath<CityPedestrians>(CollectionPath);
        if (collection == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            collection = ScriptableObject.CreateInstance<CityPedestrians>();
            AssetDatabase.CreateAsset(collection, CollectionPath);
        }
        Undo.RecordObject(collection, "Set up casual pedestrians");
        collection.characters = characters;
        collection.walk = walk;
        collection.idle = idle;
        collection.greeting = greeting;
        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();
        Selection.activeObject = collection;
        Debug.Log($"City pedestrians configured with {characters.Length} character prefabs. Open CityScence and press Play.");
    }

    private static bool IsCharacter(GameObject prefab) =>
        prefab != null && prefab.GetComponentInChildren<Animator>(true) != null &&
        prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
