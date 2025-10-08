using UnityEngine;

public class PrefabReplacer : MonoBehaviour
{
    [Header("전역 Resources 폴더 경로")]
    public string globalResourcePath = "3d-future-prefabs";

    [Header("대상 부모 오브젝트")]
    public Transform agentsParent;
    public Transform childagentsParent; // ✅ 자식용 추가

    [Header("Ground 설정")]
    public GameObject ground;
    [SerializeField] private string groundMaterialPath = "Materials";
    [SerializeField] private bool GroundMaterialChange = true;

    private RoomManager rm;

    private void Awake()
    {
        rm = GetComponent<RoomManager>();
        if (ground == null && rm != null)
            ground = rm.ground;
    }

    public void ReplacePrefabs()
    {
        ReplacePrefabsUnderParent(agentsParent);
        ReplacePrefabsUnderParent(childagentsParent);

        // ✅ 프리팹 교체 후 Ground 머터리얼 랜덤 변경
        RandomizeGroundMaterial();
    }

    private void ReplacePrefabsUnderParent(Transform parent)
    {
        if (parent == null) return;

        var children = new Transform[parent.childCount];
        for (int i = 0; i < parent.childCount; i++)
            children[i] = parent.GetChild(i);

        foreach (var child in children)
        {
            string childName = child.name;
            string prefix = childName.Split('_')[0];
            string folderPath = globalResourcePath + "/" + prefix;

            GameObject[] prefabOptions = Resources.LoadAll<GameObject>(folderPath);
            if (prefabOptions == null || prefabOptions.Length == 0)
            {
                Debug.LogWarning($"{folderPath} 경로에서 프리팹을 찾을 수 없음");
                continue;
            }

            Vector3 pos = child.position;
            Quaternion rot = child.rotation;
            Vector3 scale = child.localScale;

            GameObject newPrefab = prefabOptions[Random.Range(0, prefabOptions.Length)];

            DestroyImmediate(child.gameObject);
            GameObject newObj = Instantiate(newPrefab, pos, rot, parent);
            newObj.transform.localScale = scale;
            newObj.name = newPrefab.name;

            var childAgent = newObj.GetComponent<simplechildAgent>();
            if (childAgent != null) rm.ConnectNewChild(childAgent);
        }
    }

    private void RandomizeGroundMaterial()
    {
        if (ground == null || !GroundMaterialChange || string.IsNullOrEmpty(groundMaterialPath))
            return;

        Material[] mats = Resources.LoadAll<Material>(groundMaterialPath);
        if (mats == null || mats.Length == 0)
        {
            Debug.LogWarning($"{groundMaterialPath} 경로에서 Material을 찾을 수 없음");
            return;
        }

        Renderer rend = ground.GetComponent<Renderer>();
        if (rend != null)
        {
            Material randomMat = mats[Random.Range(0, mats.Length)];
            rend.material = randomMat;
            //Debug.Log($"[PrefabReplacer] Ground 머터리얼 교체 완료 → {randomMat.name}");
        }
    }
}
