using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CustomizeVETTerminal
{
    private const string Menu = "Tools/City Driving/Customize VET Terminal";
    private const string BuildingTexture = "Assets/SimplePoly City - Low Poly Assets/Textures/Building_Auto Service.png";
    private const string BuildingMaterial = "Assets/SimplePoly City - Low Poly Assets/Materials/Building_Auto Service.mat";
    private const string LogoSource = "Assets/Branding/VET_Logo_Source.png";
    private const string OutputFolder = "Assets/Branding";
    private const string LogoOutput = OutputFolder + "/VET_Logo_Transparent.png";
    private const string TextureOutput = OutputFolder + "/VET_Terminal_Facade.png";
    private const string MaterialOutput = OutputFolder + "/VET_Terminal_Facade.mat";

    [MenuItem(Menu, priority = 20)]
    public static void Apply()
    {
        if (!File.Exists(LogoSource))
        {
            Debug.LogError("The supplied VET logo source could not be found. Reattach the logo image and run this tool again.");
            return;
        }

        Directory.CreateDirectory(OutputFolder);
        Texture2D building = LoadPng(BuildingTexture);
        Texture2D logo = LoadPng(LogoSource);
        if (building == null || logo == null) return;

        RemoveConnectedBackground(logo);
        File.WriteAllBytes(LogoOutput, logo.EncodeToPNG());

        Color32[] pixels = building.GetPixels32();
        int width = building.width, height = building.height;
        Color32 coral = new Color32(196, 91, 78, 255);
        Color32 deepTerracotta = new Color32(105, 46, 43, 255);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            Color32 p = pixels[i];
            bool facade = p.r > 155 && p.g > 140 && p.b > 115 && Mathf.Abs(p.r - p.g) < 55;
            bool goldTrim = p.r > 190 && p.g > 105 && p.b < 75;
            if (facade) pixels[i] = coral;
            else if (goldTrim) pixels[i] = deepTerracotta;
        }

        // Paint over the old AUTO SERVICE lettering before placing the new mark.
        FillRect(pixels, width, height, 82, 348, 430, 448, coral);
        CompositeLogo(pixels, width, height, logo, 212, 358, 88, 88);
        building.SetPixels32(pixels);
        building.Apply();
        File.WriteAllBytes(TextureOutput, building.EncodeToPNG());
        Object.DestroyImmediate(building);
        Object.DestroyImmediate(logo);

        AssetDatabase.ImportAsset(LogoOutput, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(TextureOutput, ImportAssetOptions.ForceUpdate);
        ConfigureTexture(LogoOutput, true);
        ConfigureTexture(TextureOutput, false);

        Material source = AssetDatabase.LoadAssetAtPath<Material>(BuildingMaterial);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialOutput);
        if (material == null)
        {
            material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialOutput);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureOutput);
        material.color = Color.white;
        EditorUtility.SetDirty(material);

        GameObject buildingObject = GameObject.Find("Building_Auto Service");
        if (buildingObject == null)
        {
            Debug.LogError("Could not find 'Building_Auto Service' in the open scene.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(buildingObject, "Customize VET Terminal");
        foreach (Renderer renderer in buildingObject.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
        }

        EditorUtility.SetDirty(buildingObject);
        EditorSceneManager.MarkSceneDirty(buildingObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = buildingObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("Customized Building_Auto Service as the coral VET terminal with the supplied logo.", buildingObject);
    }

    private static Texture2D LoadPng(string path)
    {
        if (!File.Exists(path)) { Debug.LogError($"Missing image: {path}"); return null; }
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
    }

    private static void RemoveConnectedBackground(Texture2D texture)
    {
        int w = texture.width, h = texture.height;
        Color32[] pixels = texture.GetPixels32();
        var queue = new Queue<int>();
        var seen = new bool[pixels.Length];
        for (int x = 0; x < w; x++) { Enqueue(x, 0); Enqueue(x, h - 1); }
        for (int y = 0; y < h; y++) { Enqueue(0, y); Enqueue(w - 1, y); }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            Color32 c = pixels[i];
            pixels[i] = new Color32(c.r, c.g, c.b, 0);
            int x = i % w, y = i / w;
            Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
        }
        texture.SetPixels32(pixels);
        texture.Apply();

        void Enqueue(int x, int y)
        {
            int i = y * w + x;
            if (!seen[i] && IsOrangeBackground(pixels[i])) { seen[i] = true; queue.Enqueue(i); }
        }
        void Visit(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            Enqueue(x, y);
        }
    }

    private static bool IsOrangeBackground(Color32 c)
    {
        return c.r > 175 && c.g > 55 && c.g < 145 && c.b < 45;
    }

    private static void FillRect(Color32[] pixels, int w, int h, int x0, int y0, int x1, int y1, Color32 color)
    {
        for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
        for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++) pixels[y * w + x] = color;
    }

    private static void CompositeLogo(Color32[] target, int tw, int th, Texture2D logo, int x0, int y0, int dw, int dh)
    {
        Color32[] source = logo.GetPixels32();
        int sw = logo.width, sh = logo.height;
        for (int y = 0; y < dh; y++)
        for (int x = 0; x < dw; x++)
        {
            int tx = x0 + x, ty = y0 + y;
            if (tx < 0 || ty < 0 || tx >= tw || ty >= th) continue;
            int sx = Mathf.Clamp(Mathf.RoundToInt(x / (float)(dw - 1) * (sw - 1)), 0, sw - 1);
            int sy = Mathf.Clamp(Mathf.RoundToInt(y / (float)(dh - 1) * (sh - 1)), 0, sh - 1);
            Color32 src = source[sy * sw + sx];
            if (src.a == 0) continue;
            float a = src.a / 255f;
            Color32 dst = target[ty * tw + tx];
            target[ty * tw + tx] = new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(dst.r, src.r, a)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(dst.g, src.g, a)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(dst.b, src.b, a)), 255);
        }
    }

    private static void ConfigureTexture(string path, bool alpha)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        importer.alphaIsTransparency = alpha;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
    }
}
