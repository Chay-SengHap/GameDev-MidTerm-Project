using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SetupCityPedestrians
{
    private const string CollectionPath = "Assets/Resources/CityPedestrians.asset";
    private const int DefaultPopulation = 58;

    [InitializeOnLoadMethod]
    private static void AutoSetUpOnLoad()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorApplication.delayCall += CheckAndSetUp;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            EditorApplication.delayCall += CheckAndSetUp;
    }

    private static void CheckAndSetUp()
    {
        if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        var existing = AssetDatabase.LoadAssetAtPath<CityPedestrians>(CollectionPath);
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject sceneRoot = activeScene.IsValid() && activeScene.name == "CityScence"
            ? activeScene.GetRootGameObjects().FirstOrDefault(go => go != null && go.name == CityPedestrians.RootObjectName)
            : null;
        int currentCount = sceneRoot != null ? sceneRoot.GetComponentsInChildren<CityPedestrian>(true).Length : 0;

        if (existing == null || existing.characters == null || existing.characters.Length < 20 ||
            existing.population < DefaultPopulation || existing.setupVersion != CityPedestrians.CurrentSetupVersion ||
            currentCount < DefaultPopulation)
        {
            SetUpInternal(selectAsset: false);
        }
    }

    [MenuItem("Tools/City Driving/Set Up Casual Pedestrians")]
    public static void SetUp()
    {
        SetUpInternal(selectAsset: true);
    }

    private static void SetUpInternal(bool selectAsset)
    {
        var paths = AssetDatabase.FindAssets("", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
            {
                string norm = Normalize(path);
                return norm.Contains("casualcharactercollection3") ||
                       norm.Contains("hodaartlowpolycharactercollection3");
            })
            .ToArray();

        var casualChars = paths
            .Where(path => Normalize(path).Contains("casualcharactercollection3") &&
                           path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(IsCharacter)
            .OrderBy(prefab => prefab.name)
            .ToArray();

        var lowPolyFbxPaths = paths
            .Where(path => Normalize(path).Contains("hodaartlowpolycharactercollection3") &&
                           path.EndsWith("T-Pose.fbx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var fbxPath in lowPolyFbxPaths)
        {
            if (AssetImporter.GetAtPath(fbxPath) is ModelImporter importer && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        var lowPolyChars = paths
            .Where(path => Normalize(path).Contains("hodaartlowpolycharactercollection3") &&
                           path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(IsCharacter)
            .OrderBy(prefab => prefab.name)
            .ToArray();

        var interleaved = new List<GameObject>();
        int maxCount = Mathf.Max(casualChars.Length, lowPolyChars.Length);
        for (int i = 0; i < maxCount; i++)
        {
            if (i < casualChars.Length) interleaved.Add(casualChars[i]);
            if (i < lowPolyChars.Length) interleaved.Add(lowPolyChars[i]);
        }

        var characters = interleaved.ToArray();
        if (characters.Length == 0)
        {
            Debug.LogWarning("Import Hodaart's Character Collection 3 prefabs, then run this command again.");
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
        var greeting = clips.FirstOrDefault(clip => Normalize(clip.name).Contains("greet")) ??
            clips.FirstOrDefault(clip => Normalize(clip.name).EndsWith("hi"));

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
            collection.population = DefaultPopulation;
            AssetDatabase.CreateAsset(collection, CollectionPath);
        }

        Undo.RecordObject(collection, "Set up city pedestrians");
        collection.characters = characters;
        collection.walk = walk;
        collection.idle = idle;
        collection.greeting = greeting;
        if (collection.population <= 38)
            collection.population = DefaultPopulation;
        collection.setupVersion = CityPedestrians.CurrentSetupVersion;

        EditorUtility.SetDirty(collection);
        AssetDatabase.SaveAssets();

        int spawnedCount = 0;
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isLoaded && activeScene.name == "CityScence" && !Application.isPlaying)
        {
            spawnedCount = collection.PopulateScene(activeScene, replaceExisting: true);
            if (spawnedCount > 0)
                EditorSceneManager.MarkSceneDirty(activeScene);
        }

        if (selectAsset)
        {
            GameObject root = activeScene.IsValid()
                ? activeScene.GetRootGameObjects().FirstOrDefault(go => go != null && go.name == CityPedestrians.RootObjectName)
                : null;
            Selection.activeObject = root != null ? (UnityEngine.Object)root : collection;
        }

        Debug.Log($"City pedestrians configured with {characters.Length} character prefabs and {spawnedCount} citizens placed (Sitting on benches/restaurant chairs, Walking multi-corner routes, Talking with overhead speech bubbles, and Chilling).");
    }

    private static bool IsCharacter(GameObject prefab) =>
        prefab != null && prefab.GetComponentInChildren<Animator>(true) != null &&
        prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;

    private static string Normalize(string name) =>
        new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
