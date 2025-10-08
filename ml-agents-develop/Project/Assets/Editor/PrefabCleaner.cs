using UnityEngine;
using UnityEditor;

public class PrefabCleaner
{
    // 메뉴 경로 정의: Assets/Tools/Clean Up Prefabs 라는 메뉴를 생성
    [MenuItem("Assets/Tools/Clean Up Prefabs (Keep BoxCollider)")]
    private static void CleanUpPrefabs()
    {
        // 프로젝트 창에서 선택된 모든 게임오브젝트를 가져옴
        GameObject[] selectedObjects = Selection.GetFiltered<GameObject>(SelectionMode.Assets);

        if (selectedObjects.Length == 0)
        {
            Debug.LogWarning("정리할 프리팹을 프로젝트 창에서 먼저 선택해주세요.");
            return;
        }

        int cleanedCount = 0;
        foreach (var prefab in selectedObjects)
        {
            string path = AssetDatabase.GetAssetPath(prefab);

            // 프리팹을 씬에 임시로 생성하여 수정 준비
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

            // 게임오브젝트의 모든 컴포넌트를 가져옴
            Component[] components = instance.GetComponents<Component>();

            // 배열의 뒤에서부터 순회 (컴포넌트 삭제 시 배열 인덱스 문제를 피하기 위함)
            for (int i = components.Length - 1; i >= 0; i--)
            {
                Component component = components[i];

                // 필수 컴포넌트나 유지해야 할 컴포넌트인지 확인
                // Transform, MeshFilter, MeshRenderer, BoxCollider는 삭제하지 않음
                if (component is Transform || component is MeshFilter || component is MeshRenderer || component is BoxCollider)
                {
                    continue; // 건너뛰기
                }

                // 그 외의 모든 컴포넌트는 즉시 삭제
                Object.DestroyImmediate(component, true);
            }

            // 변경사항을 원본 프리팹에 저장(덮어쓰기)
            PrefabUtility.SaveAsPrefabAsset(instance, path);

            // 임시 인스턴스 파괴
            Object.DestroyImmediate(instance);

            cleanedCount++;
        }

        Debug.Log($"총 {cleanedCount}개의 프리팹을 정리했습니다. (BoxCollider, Mesh 컴포넌트 제외)");
    }
}
