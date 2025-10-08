// Assets/Editor/RemoveShadowChildren.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RemoveShadowChildren
{
    // 메뉴: Tools/Prefabs/Remove "shadow" children (in selected folders)
    [MenuItem("Tools/Prefabs/Remove 'shadow' children (in selected folders)")]
    public static void RemoveShadowChildrenInSelectedFolders()
    {
        // 프로젝트 창에서 선택된 폴더 경로들
        var selectedGUIDs = Selection.assetGUIDs;
        if (selectedGUIDs == null || selectedGUIDs.Length == 0)
        {
            EditorUtility.DisplayDialog("Remove 'shadow'", "프로젝트 창에서 폴더를 선택하세요.", "OK");
            return;
        }

        // 선택항목 중 폴더만 추출
        var folderPaths = new List<string>();
        foreach (var guid in selectedGUIDs)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(path))
                folderPaths.Add(path);
        }

        if (folderPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("Remove 'shadow'", "선택된 항목에 폴더가 없습니다.", "OK");
            return;
        }

        // 폴더들에서 프리팹 GUID 모으기 (하위폴더 포함)
        var prefabGUIDs = AssetDatabase.FindAssets("t:Prefab", folderPaths.ToArray());
        if (prefabGUIDs.Length == 0)
        {
            EditorUtility.DisplayDialog("Remove 'shadow'", "선택된 폴더들에 프리팹이 없습니다.", "OK");
            return;
        }

        int changedCount = 0;
        int removedObjects = 0;

        try
        {
            for (int i = 0; i < prefabGUIDs.Length; i++)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGUIDs[i]);

                if (EditorUtility.DisplayCancelableProgressBar(
                        "Removing 'shadow' children…",
                        $"{i + 1}/{prefabGUIDs.Length}  {prefabPath}",
                        (float)(i + 1) / prefabGUIDs.Length))
                {
                    break;
                }

                // Prefab contents 열기
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) continue;

                // 이름이 "shadow" 인 모든 자식(재귀) 찾기 (대소문자 무시)
                var targets = new List<GameObject>();
                CollectChildrenNamed(root.transform, "shadow", targets);

                if (targets.Count > 0)
                {
                    // 제거
                    foreach (var go in targets)
                    {
                        removedObjects++;
                        Object.DestroyImmediate(go);
                    }

                    // 저장
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    changedCount++;
                }

                // 닫기
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Remove 'shadow'",
            $"처리한 프리팹: {changedCount}개\n삭제한 오브젝트: {removedObjects}개",
            "OK"
        );
    }

    // 재귀적으로 자식 중 이름이 특정 문자열과 일치(대소문자 무시)하는 오브젝트 수집
    private static void CollectChildrenNamed(Transform parent, string nameToMatch, List<GameObject> collector)
    {
        string target = nameToMatch.Trim().ToLowerInvariant();

        void Recurse(Transform t)
        {
            if (t.name.Trim().ToLowerInvariant() == target)
                collector.Add(t.gameObject);

            for (int i = 0; i < t.childCount; i++)
                Recurse(t.GetChild(i));
        }

        Recurse(parent);
    }
}
