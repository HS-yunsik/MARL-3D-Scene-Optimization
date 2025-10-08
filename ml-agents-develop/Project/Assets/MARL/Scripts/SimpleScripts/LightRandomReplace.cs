using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LightRandomReplace : MonoBehaviour
{
    [Header("조명 프리팹 폴더 (Resources 기준)")]
    public string lightPrefabPath = "3d-future-prefabs/Lighting";
    [SerializeField]
    private GameObject currentLight; // 이전 라이트 추적

    /// <summary>
    /// 새로운 랜덤 조명을 배치합니다.
    /// </summary>
    public void SpawnRandomLight()
    {
        // 기존 라이트 제거
        if (currentLight != null)
        {
            DestroyImmediate(currentLight);
        }

        // 프리팹 로드
        GameObject[] lightPrefabs = Resources.LoadAll<GameObject>(lightPrefabPath);
        if (lightPrefabs == null || lightPrefabs.Length == 0)
        {
            Debug.LogWarning($"{lightPrefabPath} 경로에서 라이트 프리팹을 찾을 수 없습니다.");
            return;
        }

        // 랜덤 선택
        GameObject lightPrefab = lightPrefabs[Random.Range(0, lightPrefabs.Length)];

        // 위치 랜덤 (-0.3 ~ 0.3, y=1, -0.3 ~ 0.3)
        Vector3 lightPos = new Vector3(
            Random.Range(-0.3f, 0.3f),
            1f,
            Random.Range(-0.3f, 0.3f)
        );

        // 라이트 생성
        currentLight = Instantiate(lightPrefab, lightPos, Quaternion.identity);
        currentLight.name = lightPrefab.name;

        // Debug.Log($"랜덤 라이트 배치 완료 → {currentLight.name}, 위치 {lightPos}");
    }
}
