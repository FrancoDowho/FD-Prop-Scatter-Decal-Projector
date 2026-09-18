using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates the palette assets the FD painting tools read from.
///
/// Opens Unity's standard save dialog so the user picks where the palette
/// lands in their own project layout, instead of hard-coding a folder.
/// </summary>
public static class PaletteCreator
{
    [MenuItem("Tools/FD Painters/Create Decal Palette")]
    private static void CreateDecalPalette()
    {
        Create<DecalPalette>("New Decal Palette", "DecalPalette");
    }

    [MenuItem("Tools/FD Painters/Create Prop Palette")]
    private static void CreatePropPalette()
    {
        Create<PropPalette>("New Prop Palette", "PropPalette");
    }

    private static void Create<T>(string dialogTitle, string defaultName) where T : ScriptableObject
    {
        string path = EditorUtility.SaveFilePanelInProject(
            dialogTitle,
            defaultName,
            "asset",
            "Choose where to save the new palette");

        if (string.IsNullOrEmpty(path))
            return;

        var asset = ScriptableObject.CreateInstance<T>();

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        // Land on the new asset the way Unity's own Create menu behaves.
        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
        ProjectWindowUtil.ShowCreatedAsset(asset);

        Debug.Log("Created " + typeof(T).Name + " at " + path, asset);
    }
}
