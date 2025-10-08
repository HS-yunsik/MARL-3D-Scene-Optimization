using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;
public class FurnitureEnvController : MonoBehaviour
{
    [SerializeField]
    List<GameObject> AgentsList;
    [SerializeField]
    int numOfAgents;

    [HideInInspector]
    public GameObject ground;
    public GameObject area;

    
    private int m_ResetTimer;

    /// <summary>
    /// Max Academy steps before this platform resets
    /// </summary>
    [Header("Max Environment Steps")] public int MaxEnvironmentSteps = 25000;

    private SimpleMultiAgentGroup m_AgentGroup;

    public Transform agentParent;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        AgentsList = new List<GameObject>();
        foreach (Transform child in agentParent) // 부모를 Inspector에서 지정해도 OK
        {
            AgentsList.Add(child.gameObject);
        }

        numOfAgents = AgentsList.Count;

        m_AgentGroup = new SimpleMultiAgentGroup();
        foreach (var item in AgentsList)
        {
            if (item.GetComponent<ParentAgent>() != null)
                m_AgentGroup.RegisterAgent(item.GetComponent<ParentAgent>());
            else if(item.GetComponent<ChildAgent>() != null)
                m_AgentGroup.RegisterAgent(item.GetComponent<ChildAgent>());
        }
    }
   
    // Update is called once per frame
    void Update()
    {
        m_ResetTimer += 1;
        if (m_ResetTimer >= MaxEnvironmentSteps && MaxEnvironmentSteps > 0)
        {
            m_AgentGroup.GroupEpisodeInterrupted();
            ResetScene();
        }
        if (numOfAgents == 0)
        {
            m_AgentGroup.GroupEpisodeInterrupted();
            m_AgentGroup.AddGroupReward(100.0f / MaxEnvironmentSteps);
            ResetScene();
        }

        //Hurry Up Penalty
        m_AgentGroup.AddGroupReward(-0.5f / MaxEnvironmentSteps);
    }

    public void setNumOfAgents()
    {
        numOfAgents--;
    }

    public void ResetScene()
    {
        m_ResetTimer = 0;

        //Reset Agents
        foreach (var item in AgentsList)
        {
            var pos = GetRandomSpawnPos(); // 월드 좌표 계산

            // 월드 좌표(pos)를 agentParent의 로컬 좌표로 변환합니다.
            var localPos = pos;
            item.transform.localPosition = localPos;
        }

        //Reset counter
        numOfAgents = AgentsList.Count;
    }

    public Vector3 GetRandomSpawnPos()
    {
        
        var randomPosX = Random.Range(-ground.GetComponent<Collider>().bounds.extents.x, ground.GetComponent<Collider>().bounds.extents.x);
        var randomPosZ = Random.Range(-ground.GetComponent<Collider>().bounds.extents.z, ground.GetComponent<Collider>().bounds.extents.z);
        // 장애물 체크 없이 그냥 랜덤 위치 생성

        Debug.Log(ground.GetComponent<Collider>().bounds);
        var randomSpawnPos = ground.transform.position + new Vector3(randomPosX, 0f, randomPosZ);

        return randomSpawnPos;
    }


}
