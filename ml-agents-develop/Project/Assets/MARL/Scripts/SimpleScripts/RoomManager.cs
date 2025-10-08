using UnityEngine;
using Unity.MLAgents;
using System.Collections.Generic;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

public class RoomManager : MonoBehaviour
{
    private SimpleMultiAgentGroup m_AgentGroup;

    [SerializeField]
    List<GameObject> AgentsList;
    [SerializeField]
    int numOfAgents;

    [Header("Agents Parent (모든 FurnitureAgent가 들어있는 부모 오브젝트)")]
    public Transform agentsParent;
    public Transform childagentsParent;

    [Header("SceneCapture Option")]
    public bool enableSceneCapture;
    [SerializeField] SceneCapturer sceneCapturer;

    [Header("Environment Settings")]
    public GameObject ground;
    public GameObject WallContainer;

    [Header("Episode Settings")]
    public int maxEnvironmentSteps = 25000;
    private int stepCounter;
    private int TotalEpisodeCount = 0;

    [Header("Prefab Control")]
    private PrefabReplacer prefabReplacer;
    [SerializeField] bool PrefabChangeSwitch = false;
    [SerializeField] bool LightChangeSwitch = false;

    [SerializeField] int roomnum;

    private Dictionary<string, string> relationMap = new Dictionary<string, string>();

    private void Start()
    {
        sceneCapturer = GetComponentInChildren<SceneCapturer>();
        prefabReplacer = GetComponent<PrefabReplacer>();

        // 필수 오브젝트 확인
        if (agentsParent == null)
        {
            Debug.LogError("[RoomManager] agentsParent가 설정되지 않았습니다. 인스펙터에서 지정하세요.");
            enabled = false;
            return;
        }
        if (ground == null)
        {
            Debug.LogError("[RoomManager] ground 오브젝트가 설정되지 않았습니다.");
            enabled = false;
            return;
        }

        // 그룹 초기화
        if (m_AgentGroup == null)
            m_AgentGroup = new SimpleMultiAgentGroup();

        // Start에서는 직접 InitializeAgents()를 즉시 호출하지 않고, 한 프레임 뒤에 호출
        StartCoroutine(DelayedInitAgents());
    }

    private System.Collections.IEnumerator DelayedInitAgents()
    {
        yield return null; // 한 프레임 대기 (PrefabReplacer, ground 등 모두 세팅 완료 보장)
        InitializeAgents();
    }

    private void InitializeAgents()
    {
        // 이전 Agent 리스트를 확실히 정리
        if (AgentsList != null)
        {
            AgentsList.RemoveAll(a => a == null);
            foreach (var agentObj in AgentsList)
            {
                if (agentObj == null) continue;
                if (agentObj.TryGetComponent<simpleFurnitureAgent>(out var a))
                    m_AgentGroup?.UnregisterAgent(a);
                if (agentObj.TryGetComponent<simplechildAgent>(out var c))
                    m_AgentGroup?.UnregisterAgent(c);
            }
            AgentsList.Clear();
        }
        else
        {
            AgentsList = new List<GameObject>();
        }

        // 새 리스트 채우기
        foreach (Transform child in agentsParent)
            AgentsList.Add(child.gameObject);

        if (childagentsParent != null)
            foreach (Transform child in childagentsParent)
            {
                AgentsList.Add(child.gameObject);
                var c = child.GetComponent<simplechildAgent>();
                if (c != null && c.parentObject != null)
                {
                    string childPrefix = child.name.Split('_')[0];
                    string parentPrefix = c.parentObject.name.Split('_')[0];
                    relationMap[childPrefix] = parentPrefix;
                }
            }

        numOfAgents = AgentsList.Count;

        // ✅ 5. 재등록
        foreach (var item in AgentsList)
        {
            if (item.TryGetComponent<simpleFurnitureAgent>(out var parent))
            {
                parent.roomManager = this;
                m_AgentGroup.RegisterAgent(parent);
            }
            else if (item.TryGetComponent<simplechildAgent>(out var child))
            {
                // child.roomManager = this; (필요 시)
                m_AgentGroup.RegisterAgent(child);
            }
        }

        // ✅ 6. 위치 랜덤 배치
        Bounds groundBounds = ground.GetComponent<Collider>().bounds;
        foreach (var item in AgentsList)
        {
            Vector3 pos = GetNonOverlappingRandomPosition(item, ground.GetComponent<Collider>(), AgentsList);
            item.transform.position = pos;
            int yRotation = 90 * Random.Range(0, 4);
            item.transform.rotation = Quaternion.Euler(0, yRotation, 0);
        }

        Debug.Log($"[RoomManager] InitializeAgents 완료 → 현재 에이전트 수: {AgentsList.Count}");

    }
    // 🔹 새 자식 생성될 때 PrefabReplacer가 호출
    public void ConnectNewChild(simplechildAgent newChild)
    {
        string childPrefix = newChild.name.Split('_')[0];
        if (!relationMap.TryGetValue(childPrefix, out string parentPrefix)) return;

        foreach (Transform p in agentsParent)
        {
            if (p.name.StartsWith(parentPrefix))
            {
                newChild.parentObject = p.gameObject;
                newChild.parentPrefix = parentPrefix;
                break;
            }
        }
    }
    private void FixedUpdate()
    {
        // ✅ 아직 초기화되지 않았거나, 비어 있으면 아무것도 하지 않음
        if (m_AgentGroup == null || AgentsList == null || AgentsList.Count == 0)
            return;

        stepCounter++;

        // ✅ MissingReference 방어
        AgentsList.RemoveAll(a => a == null);

        UpdateNumOfAgents();

        if (numOfAgents == 0)
        {
            m_AgentGroup.GroupEpisodeInterrupted();
            m_AgentGroup.AddGroupReward(100.0f / maxEnvironmentSteps);
            Debug.Log("모든 에이전트 목표 달성 → 그룹 에피소드 종료");

            if (sceneCapturer != null && enableSceneCapture)
            {
                string fileName = $"Scene_Episode_{roomnum}_{TotalEpisodeCount}";
                sceneCapturer.CaptureScene(fileName);
            }

            TotalEpisodeCount++;
            m_AgentGroup.AddGroupReward(+1.0f);
            EndEpisode();
            return;
        }

        if (stepCounter >= maxEnvironmentSteps)
        {
            Debug.Log("시간 초과 → 그룹 에피소드 종료");
            m_AgentGroup.AddGroupReward(-0.5f);
            EndEpisode();
        }
    }

    public void UpdateNumOfAgents()
    {
        // Freeze되지 않은(즉, isKinematic == false) 에이전트만 카운트
        int activeCount = 0;
        foreach (var go in AgentsList)
        {
            if (go == null) continue;
            if (go.TryGetComponent<simpleFurnitureAgent>(out var agent))
            {
                if (!agent.GetComponent<Rigidbody>().isKinematic)
                    activeCount++;
            }
        }
        numOfAgents = activeCount;
    }
    void EndEpisode()
    {
        // 에이전트들 간 충돌 체크
        int overlapCount = 0;
        for (int i = 0; i < AgentsList.Count; i++)
        {
            var goA = AgentsList[i];
            if (goA == null) continue; // ✅ 파괴된 오브젝트 방어
            var colA = goA.GetComponent<Collider>();
            if (colA == null) continue;

            for (int j = i + 1; j < AgentsList.Count; j++)
            {
                var goB = AgentsList[j];
                if (goB == null) continue; // ✅ 방어
                var colB = goB.GetComponent<Collider>();
                if (colB == null) continue;

                if (colA.bounds.Intersects(colB.bounds))
                    overlapCount++;
            }
        }

        if (overlapCount > 0)
        {
            Debug.LogWarning($"에피소드 종료 시 {overlapCount} 쌍 충돌 발생 → 패널티 적용");
            m_AgentGroup.AddGroupReward(-0.2f * overlapCount); // 페널티는 상황에 따라 조정
        }

        m_AgentGroup.EndGroupEpisode();
        stepCounter = 0;

        if (LightChangeSwitch)
        {
            LightRandomReplace lightRandomizer = GetComponent<LightRandomReplace>();
            if (lightRandomizer != null) lightRandomizer.SpawnRandomLight();
        }

        // ✅ Prefab 교체 후 바로 InitializeAgents()를 호출하지 않는다!
        if (PrefabChangeSwitch && prefabReplacer != null)
        {
            StartCoroutine(ReplaceAndReinitialize());
        }
        else
        {
            // Prefab 교체가 없으면 즉시 초기화
            InitializeAgents();
        }
    }
    private System.Collections.IEnumerator ReplaceAndReinitialize()
    {
        // 프리팹 교체
        prefabReplacer.ReplacePrefabs();

        // ✅ Destroy()가 실제로 완료될 때까지 한 프레임 기다림
        yield return null;
        AgentsList.RemoveAll(a => a == null); // ✅ 리스트에서 Missing 정리
        // ✅ 안전하게 리스트 재구성
        InitializeAgents();
        yield return null; // (안전하게 한 프레임 더 대기 권장)
        // ✅ 모든 childAgent 부모 재연결
        foreach (var go in AgentsList)
        {
            if (go == null) continue;
            if (go.TryGetComponent<simplechildAgent>(out var c))
                c.AutoReconnectParent();
        }

        Debug.Log($"[RoomManager] 프리팹 교체 후 에이전트 재등록 완료 ({AgentsList.Count}개)");
    }

    private Vector3 GetNonOverlappingRandomPosition(GameObject agentPrefab, Collider groundCollider, List<GameObject> existingAgents, int maxAttempts = 50)
    {
        Bounds groundBounds = groundCollider.bounds;
        Collider agentCollider = agentPrefab.GetComponent<Collider>();
        Vector3 size = agentCollider.bounds.size;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            float randomX = Random.Range(groundBounds.min.x, groundBounds.max.x);
            float randomZ = Random.Range(groundBounds.min.z, groundBounds.max.z);
            float yPos = groundBounds.max.y + 0.1f;

            Vector3 testPos = new Vector3(randomX, yPos, randomZ);
            Vector3 halfExtents = size * 0.5f;

            // 충돌체크 (기존 에이전트, 벽 등과)
            Collider[] overlaps = Physics.OverlapBox(testPos, halfExtents, Quaternion.identity);
            if (overlaps.Length == 0)
            {
                return testPos; // 겹치지 않음
            }
        }

        Debug.LogWarning("겹치지 않는 위치를 찾지 못했습니다. 마지막 시도 위치 사용");
        return new Vector3(Random.Range(groundBounds.min.x, groundBounds.max.x),
                           groundBounds.max.y + 0.1f,
                           Random.Range(groundBounds.min.z, groundBounds.max.z));
    }
    private int completedChildren = 0;

    public void ReportChildCompletion(simplechildAgent agent)
    {
        completedChildren++;
        Debug.Log($"[RoomManager] 자식 완료 보고 받음: {agent.name} ({completedChildren}/{1})");

        if (completedChildren ==1)
        {
            Debug.Log("🎉 모든 자식 완료 → 에피소드 종료!");
            EndEpisode(); // 또는 m_AgentGroup.EndGroupEpisode();
        }
    }
}
