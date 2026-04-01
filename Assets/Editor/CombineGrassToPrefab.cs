using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class CombineGrassToPrefab
{
    [MenuItem("Tools/Combine Selected Grass To Saved Prefab")]
    public static void CombineSelectedGrassToSavedPrefab()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogError("No GameObject selected.");
            return;
        }

        MeshFilter[] meshFilters = selected.GetComponentsInChildren<MeshFilter>(true);

        if (meshFilters.Length == 0)
        {
            Debug.LogError("No child MeshFilter found on selected object.");
            return;
        }

        List<CombineInstance> combineInstances = new List<CombineInstance>();
        Material sharedMaterial = null;

        Matrix4x4 rootWorldToLocal = selected.transform.worldToLocalMatrix;

        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.sharedMesh == null)
                continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null)
                continue;

            if (sharedMaterial == null && mr.sharedMaterial != null)
                sharedMaterial = mr.sharedMaterial;

            CombineInstance ci = new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = rootWorldToLocal * mf.transform.localToWorldMatrix
            };

            combineInstances.Add(ci);
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogError("No valid mesh + renderer pairs found.");
            return;
        }

        Mesh combinedMesh = new Mesh();
        combinedMesh.name = selected.name + "_CombinedMesh";

        // Use 32-bit indices in case mesh gets large
        combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
        combinedMesh.RecalculateBounds();
        combinedMesh.RecalculateNormals();

        string rootFolder = "Assets/CombinedGrass";
        if (!AssetDatabase.IsValidFolder(rootFolder))
        {
            AssetDatabase.CreateFolder("Assets", "CombinedGrass");
        }

        string safeName = MakeSafeFileName(selected.name);

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{rootFolder}/{safeName}_Combined.asset"
        );

        AssetDatabase.CreateAsset(combinedMesh, meshPath);
        AssetDatabase.SaveAssets();

        GameObject combinedGO = new GameObject(selected.name + "_CombinedPrefab");
        combinedGO.transform.position = Vector3.zero;
        combinedGO.transform.rotation = Quaternion.identity;
        combinedGO.transform.localScale = Vector3.one;

        MeshFilter newMF = combinedGO.AddComponent<MeshFilter>();
        newMF.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        MeshRenderer newMR = combinedGO.AddComponent<MeshRenderer>();
        if (sharedMaterial != null)
            newMR.sharedMaterial = sharedMaterial;

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{rootFolder}/{safeName}_Combined.prefab"
        );

        PrefabUtility.SaveAsPrefabAsset(combinedGO, prefabPath);

        Object prefabAsset = AssetDatabase.LoadAssetAtPath<Object>(prefabPath);

        GameObject.DestroyImmediate(combinedGO);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorGUIUtility.PingObject(prefabAsset);
        Selection.activeObject = prefabAsset;

        Debug.Log($"Combined mesh saved at: {meshPath}");
        Debug.Log($"Combined prefab saved at: {prefabPath}");
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(c, '_');
        }
        return fileName.Replace(":", "_").Replace("/", "_").Replace("\\", "_");
    }
}