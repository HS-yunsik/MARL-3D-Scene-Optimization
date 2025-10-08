using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using static UnityEditor.PlayerSettings;

public class simplechildAgent : Agent
{
    [Header("Parent Object Reference")]
    public GameObject parentObject; // 초기 인스펙터에서 직접 지정
    public string parentPrefix;    // 이름 앞부분 (예: "Table" 등)

    [Header("Movement Settings")]
    public float moveSpeed = 2.0f;

    [Header("Relation Settings")]
    public float idealDistanceFromParent = 1.0f; // 이상적 거리
    public float distanceTolerance = 0.2f;       // 허용 오차
    public float rewardWeight = 0.5f;            // 거리 보상 가중치

    [Header("Environment Reference")]
    public RoomManager roomManager;

    private Rigidbody rBody;
    private Collider myCollider;

    // --- 내부 상태 변수 ---
    public float currentDistance;

    public override void Initialize()
    {
        rBody = GetComponent<Rigidbody>();
        if (rBody == null)
            rBody = gameObject.AddComponent<Rigidbody>();

        rBody.useGravity = false;
        rBody.isKinematic = false;
        rBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY;

        myCollider = GetComponent<Collider>();

        // 🔹 부모 이름 접두(prefix) 저장
        if (parentObject != null)
        {
            string fullName = parentObject.name;
            parentPrefix = fullName.Split('_')[0];
        }
        // 🔹 roomManager 자동 탐색
        if (roomManager == null)
            roomManager = GetComponentInParent<RoomManager>();
    }

    public override void OnEpisodeBegin()
    {
        // Rigidbody 상태 초기화
        rBody.linearVelocity = Vector3.zero;
        rBody.angularVelocity = Vector3.zero;
        rBody.isKinematic = false;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 이동 관련 상태 (자기 자신의 속성)
        sensor.AddObservation(transform.localPosition);       // 자기 위치
        sensor.AddObservation(transform.forward);             // 자기 방향 벡터

        Vector3 size = myCollider.bounds.size;
        sensor.AddObservation(size.x);  // 폭
        sensor.AddObservation(size.z);  // 깊이

        Vector3 Parentsize = parentObject.GetComponent<Collider>().bounds.size;
        sensor.AddObservation(Parentsize.x);  // 폭
        sensor.AddObservation(Parentsize.z);  // 깊이

        // 2. RewardSpec 관련 파라미터 관측
        sensor.AddObservation(idealDistanceFromParent);



        Vector3 toParent = (parentObject.transform.position - transform.position).normalized;
        toParent.y= 0;
        float dist = Vector3.Distance(transform.position, parentObject.transform.position);

        sensor.AddObservation(dist);
        sensor.AddObservation(Vector3.Dot(transform.forward.normalized, toParent));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rBody.isKinematic) return;
        if (parentObject == null) AutoReconnectParent();
        int moveAction = actions.DiscreteActions[0];

        Vector3 moveDir = Vector3.zero;
        if (moveAction == 1) moveDir += transform.forward;
        if (moveAction == 2) moveDir -= transform.forward;
        if (moveAction == 3) moveDir -= transform.right;
        if (moveAction == 4) moveDir += transform.right;

        rBody.linearVelocity = moveDir.normalized * moveSpeed;

        // --- 거리 + 방향 기반 보상 ---
        if (parentObject != null)
        {
            Vector3 toParent = (parentObject.transform.position - transform.position).normalized;
            float dist = Vector3.Distance(transform.position, parentObject.transform.position);
            float distError = Mathf.Abs(dist - idealDistanceFromParent);

            // 1️⃣ 거리 보상
            if (distError < distanceTolerance)
                AddReward(+rewardWeight);
            else
                AddReward(-distError * rewardWeight);

            // 2️⃣ 방향 보상 (부모를 바라볼수록 dot → 1)
            float facingDot = Vector3.Dot(transform.forward.normalized, toParent);
            // dot은 [-1,1] 범위 → 0 이상일 때만 긍정 보상
            if (facingDot > 0)
                AddReward(facingDot * 0.3f); // ← 가중치(0.3f)는 조정 가능
            else
                AddReward(facingDot * 0.1f); // 뒤돌면 약한 페널티

            // ✅ 완료 조건: 거리 + 방향 모두 만족
            if (distError < distanceTolerance && facingDot > 0.9f)
            {
                AddReward(+1.0f);
                FreezeAgent(); // 🔹 완료 보고 포함
                return;
            }
        }

        // 3️⃣ 스텝당 소모 보상
        AddReward(-1f / MaxStep);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;
        da[0] = 0;
        if (Input.GetKey(KeyCode.W)) da[0] = 1;
        else if (Input.GetKey(KeyCode.S)) da[0] = 2;
        else if (Input.GetKey(KeyCode.A)) da[0] = 3;
        else if (Input.GetKey(KeyCode.D)) da[0] = 4;
    }

    // ✅ 부모 프리팹 교체 시 자동 재매칭
    public void AutoReconnectParent()
    {
        if (roomManager == null || string.IsNullOrEmpty(parentPrefix))
        {
            Debug.LogWarning($"[{name}] AutoReconnectParent 실패: roomManager 또는 prefix 없음");
            return;
        }

        Transform found = null;
        foreach (Transform t in roomManager.agentsParent)
        {
            if (t.name.StartsWith(parentPrefix))
            {
                found = t;
                break;
            }
        }

        if (found != null)
        {
            parentObject = found.gameObject;           
        }
        else
        {
            Debug.LogWarning($"[{name}] '{parentPrefix}' 로 시작하는 새 부모를 찾지 못함");
        }
    }

    public void FreezeAgent()
    {
        // 1️⃣ Rigidbody 멈춤
        if (rBody != null)
            rBody.isKinematic = true;

        // 2️⃣ RoomManager에 완료 보고
        if (roomManager != null)
        {
            roomManager.ReportChildCompletion(this);
            Debug.Log($"✅ {name} 완료 → RoomManager에 보고함");
        }
    }
}
