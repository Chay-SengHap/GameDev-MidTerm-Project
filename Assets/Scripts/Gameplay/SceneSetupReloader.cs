using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity runs [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] methods only for the FIRST scene of a play session.
/// This project uses them to set the scene up at runtime (rideable scooter, traffic, game menu, spawn marker),
/// so after a scene reload (Restart / Main Menu) none of that came back and the game was stuck.
///
/// This re-runs those same setup methods whenever a scene is loaded again later. They all check first whether
/// their work is already done, so running them again is safe. Nothing else needs to know about this class.
/// </summary>
public static class SceneSetupReloader
{
    private static List<MethodInfo> setups;

    // Runs after the first scene's own setups, so subscribing here only catches the LATER loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;   // play mode without domain reload: don't subscribe twice
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        foreach (var setup in FindSetups())
        {
            try { setup.Invoke(null, null); }
            catch (System.Exception e) { Debug.LogException(e.InnerException ?? e); }
        }
    }

    /// Every parameterless static AfterSceneLoad method in this assembly, except this class's own.
    private static List<MethodInfo> FindSetups()
    {
        if (setups != null) return setups;
        setups = new List<MethodInfo>();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var type in typeof(SceneSetupReloader).Assembly.GetTypes())
        {
            if (type == typeof(SceneSetupReloader)) continue;
            foreach (var method in type.GetMethods(flags))
            {
                var attr = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                if (attr != null && attr.loadType == RuntimeInitializeLoadType.AfterSceneLoad &&
                    method.GetParameters().Length == 0)
                    setups.Add(method);
            }
        }
        return setups;
    }
}
