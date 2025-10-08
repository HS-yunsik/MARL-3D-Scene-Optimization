using UnityEngine;
using UnityEditor;
using System;
using System.IO;

public class CopyScriptsToPrefabs : EditorWindow
{
    private GameObject sourceObject;
    private string prefabFolder = "Assets/Prefabs"; // ✅ 프리팹이 들어있는 폴더 경로

    [MenuItem("Tools/Copy Scripts To Prefabs")]
    static void Init()
    {
        GetWindow<CopyScriptsToPrefabs>("Copy Scripts To Prefabs");
    }

    void OnGUI()
    {
        sourceObject = (GameObject)EditorGUILayout.ObjectField("Source Object", sourceObject, typeof(GameObject), true);
        prefabFolder = EditorGUILayout.TextField("Prefab Folder", prefabFolder);

        if (GUILayout.Button("Copy Script Components To All Prefabs"))
        {
            if (sourceObject == null)
            {
                Debug.LogError("⚠ Source Object를 지정하세요.");
                return;
            }

            CopyScriptsToAllPrefabs();
        }
    }

    void CopyScriptsToAllPrefabs()
    {
        // 소스 오브젝트의 스크립트 타입 가져오기
        var sourceScripts = sourceObject.GetComponents<MonoBehaviour>();
        if (sourceScripts.Length == 0)
        {
            Debug.LogWarning("⚠ Source Object에 스크립트 컴포넌트가 없습니다.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { prefabFolder });

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (prefab == null) continue;

            bool modified = false;
            foreach (var script in sourceScripts)
            {
                if (script == null) continue;
                Type type = script.GetType();

                // 프리팹에 해당 스크립트가 없으면 추가
                if (prefab.GetComponent(type) == null)
                {
                    GameObject prefabInstance = PrefabUtility.LoadPrefabContents(path);
                    prefabInstance.AddComponent(type);
                    PrefabUtility.SaveAsPrefabAsset(prefabInstance, path);
                    PrefabUtility.UnloadPrefabContents(prefabInstance);

                    modified = true;
                    Debug.Log($"✅ {type.Name} 추가됨 → {path}");
                }
            }

            if (!modified)
            {
                Debug.Log($"ℹ 변경 없음: {path}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
