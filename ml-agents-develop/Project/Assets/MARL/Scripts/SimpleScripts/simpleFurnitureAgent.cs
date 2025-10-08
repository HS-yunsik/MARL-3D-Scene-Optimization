using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public struct WallInfo
{
    public float distance;
    public Vector3 normal;
    public Collider collider; // 어떤 벽인지 Collider 자체를 저장
}

public enum AgentPhase { Positioning, Aligning }

public class simpleFurnitureAgent : Agent
{
    private Vector3 lastWallNormal = Vector3.zero;
    private float lastCalculatedDotForward = 0f;
    private AgentPhase currentPhase;
    public Collider targetWallCollider; // 2단계에서 사용할 고정된 타겟 벽
    private Rigidbody rBody;
    private Collider ownCollider;

    [Header("Movement Settings")]
    public float moveSpeed = 2.0f;
    public float turnSpeed = 200f;

    [Header("Environment")]
    public GameObject wallContainer;
    private Collider[] wallColliders;

    [Header("Reward Settings")]
    public float idealDistanceFromWall = 1.0f;
    [Tooltip("1단계 목표 지점의 고정 허용 오차")]
    public float distanceThreshold = 0.2f;
    [Tooltip("2단계 이탈 감지의 크기 비례 허용 오차")]
    public float distanceThresholdRatio = 0.3f;
    public float minDistanceFromWall = 0.5f;

    [Header("Success Conditions")]
    public float alignmentThreshold = 0.1f;
    public int successStepCount = 50;

    [Header("Spawn Settings")]
    public LayerMask furnitureLayerMask;
    public float agentSizeRadius = 0.5f;

    public RoomManager roomManager;


    public WallInfo currentWallInfo;
    [Header("Debug")]
    public Collider currenttargetwall;

    // --- 추가: 답답함 상태를 위한 변수 ---
    private Vector3 lastPosition;
    private int frustrationCounter;
    [Tooltip("답답함을 느끼기 시작하는 스텝 수")]
    public int frustrationThreshold = 50;

    [Header("Social Distancing")]
    [Tooltip("서로 유지해야 할 최소한의 거리")]
    public float socialDistance = 2.0f;
    [Tooltip("가까워졌을 때 받을 벌점의 최대 강도")]
    public float repulsionPenalty = -0.5f;

    // ✅ Initialize() 교체
    public override void Initialize()
    {
        ownCollider = GetComponent<Collider>();
        rBody = GetComponent<Rigidbody>();
        if (rBody == null) rBody = gameObject.AddComponent<Rigidbody>();
        rBody.useGravity = false;
        rBody.isKinematic = false;

        // 주입 실패 대비: 상위에서 RoomManager 탐색
        if (roomManager == null)
            roomManager = GetComponentInParent<RoomManager>();

        if (roomManager == null)
        {
            Debug.LogError($"[{name}] RoomManager 참조가 없습니다. PrefabReplacer에서 주입하거나, 계층 구조상 부모에 RoomManager가 오도록 하세요.");
            enabled = false; // 더 이상 진행하지 않음
            return;
        }

        if (wallContainer == null)
            wallContainer = roomManager.WallContainer;

        if (wallContainer != null)
            wallColliders = wallContainer.GetComponentsInChildren<Collider>();
        else
            Debug.LogError($"[{name}] wallContainer가 비어 있습니다. RoomManager.WallContainer를 확인하세요.");
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 자기 자신의 콜라이더 사이즈(로컬 기준)를 관측 정보에 추가합니다.
        // 이를 통해 에이전트는 자신의 '모양'을 학습하게 됩니다.
        if (ownCollider is BoxCollider boxCollider)
        {
            // BoxCollider의 로컬 사이즈를 관측에 추가 (회전에 영향받지 않음)
            sensor.AddObservation(boxCollider.size.x);
            sensor.AddObservation(boxCollider.size.z);
        }
        else
        {
            // BoxCollider가 아닐 경우, 월드 기준의 바운딩 박스 크기를 사용
            // (회전 시 크기가 변동될 수 있어 BoxCollider보다 덜 안정적임)
            sensor.AddObservation(ownCollider.bounds.size.x);
            sensor.AddObservation(ownCollider.bounds.size.z);
        }
        sensor.AddObservation(transform.forward);
        sensor.AddObservation(this.lastCalculatedDotForward);
        sensor.AddObservation(this.lastWallNormal);
        sensor.AddObservation(this.idealDistanceFromWall);
    }

    public override void OnEpisodeBegin()
    {
        targetWallCollider = null; // 타겟 벽 초기화
        rBody.isKinematic = false;
        rBody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ | RigidbodyConstraints.FreezePositionY; // X,Z 회전 잠금
        rBody.linearVelocity = Vector3.zero;
        rBody.angularVelocity = Vector3.zero;

        currentPhase = AgentPhase.Positioning;
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (rBody.isKinematic) return;

        // --- 사회적 거리두기 (다른 에이전트 밀어내기) ---
        ownCollider.enabled = false;
        // 내 주변(socialDistance 반경)에 있는 다른 에이전트들을 모두 찾음
        Collider[] nearbyAgents = Physics.OverlapSphere(transform.position, socialDistance, furnitureLayerMask);
        ownCollider.enabled = true;

        // 주변에 다른 에이전트가 있다면
        if (nearbyAgents.Length > 0)
        {
            // 가장 가까운 에이전트와의 거리를 계산
            float minDistanceToAgent = float.MaxValue;
            foreach (var agentCollider in nearbyAgents)
            {
                float dist = Vector3.Distance(transform.position, agentCollider.transform.position);
                if (dist < minDistanceToAgent)
                {
                    minDistanceToAgent = dist;
                }
            }

            // 거리가 가까울수록 벌점이 강해지도록 설계 (0~1 사이 값으로 정규화)
            float penaltyRatio = 1.0f - (minDistanceToAgent / socialDistance);
            float penalty = penaltyRatio * repulsionPenalty;
            AddReward(penalty);
        }

        MoveAgent(actions.DiscreteActions);
        // --- 수정: 단계별로 다른 정보 수집 및 로직 수행 ---
        switch (currentPhase)
        {
            case AgentPhase.Positioning:
                targetWallCollider = GetNearestWallInfo().collider;
                HandlePositioningPhase();
                break;
            case AgentPhase.Aligning:
                // Aligning 단계에서는 고정된 타겟 벽의 법선 벡터가 현재 목표임
                if (targetWallCollider != null)
                {
                    this.lastWallNormal = targetWallCollider.transform.forward;
                }
                HandleAligningPhase();
                break;
        }
    }

    // --- 수정: idealZoneStayCount 대신 즉시 전환 및 타겟 벽 고정 ---
    private void HandlePositioningPhase()
    {
        // 1. 타겟 벽이 없다면, '최초의' 타겟을 설정한다.
        if (targetWallCollider == null) return;

        // 2. 일단 타겟이 정해지면, 그 타겟하고만 상호작용한다.
        Vector3 closestPointOnWall = targetWallCollider.ClosestPoint(transform.position);
        Vector3 closestPointOnAgent = ownCollider.ClosestPoint(closestPointOnWall);
        float distance = Vector3.Distance(closestPointOnWall, closestPointOnAgent);
        currentWallInfo.distance = distance;


        if (Vector3.Distance(transform.position, lastPosition) < 0.01f)
        {
            frustrationCounter++;
        }
        else
        {
            // 움직였다면 카운터 초기화
            frustrationCounter = 0;
        }
        // 마지막 위치 업데이트
        lastPosition = transform.position;

        // 만약 답답함이 한계에 도달했다면
        if (frustrationCounter > frustrationThreshold)
        {
            AddReward(-0.5f);
            targetWallCollider = null; // 타겟을 비워서 다음 스텝에 새로운 벽을 찾게 함
            transform.Rotate(0, 180, 0); // 새로운 방향 탐색
            frustrationCounter = 0;
            return;
        }

        // ★ 4. 성공 조건 (2단계 전환) - 타겟 벽은 이미 정해져 있으므로 바꿀 필요 없음
        bool isInIdealZone = Mathf.Abs(distance - idealDistanceFromWall) < distanceThreshold;
        if (isInIdealZone)
        {
            currentPhase = AgentPhase.Aligning;
            AddReward(0.5f);
            return;
        }

        // 1단계에서는 거리 보상만 계산
        if (distance < minDistanceFromWall)
        {
            AddReward(-0.05f);
        }
        else
        {
            AddReward(-1f / MaxStep);
        }
    }

    // --- 수정: 고정된 'targetWallCollider'만 사용하도록 변경 ---
    private void HandleAligningPhase()
    {
        if (targetWallCollider == null)
        {
            EndEpisode(); // 타겟 벽이 없으면 에피소드 실패
            return;
        }

        // 고정된 타겟 벽을 기준으로 거리와 법선 벡터를 계산
        Vector3 closestPointOnWall = targetWallCollider.ClosestPoint(transform.position);
        Vector3 closestPointOnAgent = ownCollider.ClosestPoint(closestPointOnWall);
        float distance = Vector3.Distance(closestPointOnWall, closestPointOnAgent);
        Vector3 wallNormal = targetWallCollider.transform.forward;

        //if (Mathf.Abs(distance - idealDistanceFromWall) > distanceThreshold)
        //{
        //    AddReward(-0.2f); // 단계가 강등된 것에 대한 벌점
        //    currentPhase = AgentPhase.Positioning; // 1단계로 상태 변경
        //    return; // 이번 스텝의 나머지 계산은 생략
        //}

        // --- 수정: Y축 오차를 제거하고 정렬 상태 계산 ---
        // 1. 계산에 사용할 두 방향 벡터를 가져옵니다.
        Vector3 agentForward = transform.forward;
        Vector3 wallForward = wallNormal;

        // 2. 각 벡터의 Y값을 0으로 만들어 수평으로 만듭니다.
        agentForward.y = 0;
        wallForward.y = 0;

        // 3. 정규화(Normalize)하여 다시 단위 벡터로 만듭니다. (중요!)
        agentForward.Normalize();
        wallForward.Normalize();


        // 4. Y축이 제거된 두 벡터로 내적을 계산합니다.
        float dotForward = Vector3.Dot(agentForward, wallForward);
        this.lastCalculatedDotForward = dotForward;
        // 완벽한 정렬에 대한 '잭팟' 보상
        if (dotForward > 0.99f)
        {
            AddReward(0.5f);
        }

        // 에이전트가 가만히 있는 것을 방지하고, 적극적으로 정답을 찾도록 유도
        AddReward(-1f / MaxStep);
        float alignmentReward = dotForward * 0.1f;
        AddReward(alignmentReward);

        // 정렬에 성공했는지 확인
        if (dotForward > (1.0f - alignmentThreshold))
        {
            FreezeAgent();
        }
    }

    private void MoveAgent(ActionSegment<int> discreteActions)
    {
        if (currentPhase == AgentPhase.Positioning)
        {
            // 1단계에서는 기존과 동일하게 자유로운 이동/회전
            int moveAction = discreteActions[0];
            Vector3 moveDirection = Vector3.zero;
            if (moveAction == 1) moveDirection += transform.forward;
            if (moveAction == 2) moveDirection -= transform.forward;
            if (moveAction == 3) moveDirection -= transform.right;
            if (moveAction == 4) moveDirection += transform.right;
            rBody.linearVelocity = moveDirection.normalized * moveSpeed;
            rBody.angularVelocity = Vector3.zero;
        }
        else // currentPhase == AgentPhase.Aligning
        {
            // 2단계: 이동은 멈추고, '선택된 각도'로 즉시 회전(Snap)
            rBody.linearVelocity = Vector3.zero;
            rBody.angularVelocity = Vector3.zero;

            int rotationAction = discreteActions[1];

            // 타겟 벽이 있어야만 회전 가능
            if (targetWallCollider != null)
            {
                // 벽이 바라보는 방향(방 안쪽)을 기준으로 목표 각도 계산
                Vector3 wallForward = targetWallCollider.transform.forward;
                Quaternion targetRotation = Quaternion.identity;

                switch (rotationAction)
                {
                    case 0: // 0도 (가구의 등을 벽에 붙임)
                        targetRotation = Quaternion.LookRotation(wallForward);
                        break;
                    case 1: // 90도
                        targetRotation = Quaternion.LookRotation(Quaternion.Euler(0, 90, 0) * wallForward);
                        break;
                    case 2: // 180도 (가구를 벽과 마주보게 함)
                        targetRotation = Quaternion.LookRotation(-wallForward);
                        break;
                    case 3: // 270도 (-90도)
                        targetRotation = Quaternion.LookRotation(Quaternion.Euler(0, -90, 0) * wallForward);
                        break;
                }

                // 계산된 목표 각도로 Rigidbody를 즉시 회전시킴
                rBody.MoveRotation(targetRotation);
            }
        }
    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;
        if (da.Length == 0) return; // 연속형일 때/null 방지

        // [0] 이동
        if (Input.GetKey(KeyCode.W)) da[0] = 1;
        else if (Input.GetKey(KeyCode.S)) da[0] = 2;
        else if (Input.GetKey(KeyCode.A)) da[0] = 3;
        else if (Input.GetKey(KeyCode.D)) da[0] = 4;
        else da[0] = 0;

        // [1] 회전
        if (da.Length > 1)
        {
            if (Input.GetKey(KeyCode.Q)) da[1] = 1;
            else if (Input.GetKey(KeyCode.E)) da[1] = 2;
            else da[1] = 0;
        }
    }
    public void FreezeAgent()
    {
        rBody.isKinematic = true;
        if (roomManager != null)
            roomManager.UpdateNumOfAgents();
    }

    private WallInfo GetNearestWallInfo()
    {
        WallInfo nearestWall = new WallInfo();
        nearestWall.distance = float.MaxValue;
        nearestWall.normal = Vector3.zero;
        nearestWall.collider = null;
        if (wallColliders == null || wallColliders.Length == 0) return nearestWall;
        foreach (var wallCollider in wallColliders)
        {
            Vector3 closestPointOnWall = wallCollider.ClosestPoint(transform.position);
            Vector3 closestPointOnAgent = ownCollider.ClosestPoint(closestPointOnWall);
            float distance = Vector3.Distance(closestPointOnWall, closestPointOnAgent);
            if (distance < nearestWall.distance)
            {
                nearestWall.distance = distance;
                nearestWall.normal = wallCollider.transform.forward;
                nearestWall.collider = wallCollider;
            }
        }
        return nearestWall;
    }

    public void OnCollisionEnter(Collision collision)
    {
        // 부딪힌 상대방이 다른 에이전트인지 확인
        if (collision.gameObject.CompareTag("furniture")) // 가구의 태그를 "furniture"로 가정
        {
            simpleFurnitureAgent otherAgent = collision.gameObject.GetComponent<simpleFurnitureAgent>();
            if (otherAgent != null)
            {
                // [규칙 1] 내가 "활동 중"인데, "고정된" 다른 에이전트와 부딪혔을 때 (책임 강화)
                if (!rBody.isKinematic && collision.rigidbody.isKinematic)
                {
                    // "길을 막고 있는 다른 에이전트와 부딪힌 것"에 대해 매우 강한 벌점을 받음
                    AddReward(-0.7f);

                    // 현재 계획이 틀렸음을 인지하고, 즉시 새로운 방향을 탐색하도록 강제 회전
                    transform.Rotate(0, 180, 0);
                }
                // [규칙 2] 내가 "고정" 상태인데, "활동 중인" 다른 에이전트가 와서 부딪혔을 때 (기존의 양보 로직)
                else if (rBody.isKinematic && !collision.rigidbody.isKinematic)
                {
                    // 나의 고정을 풀고, 자리를 양보한 뒤, 다시 1단계부터 시작
                    rBody.isKinematic = false;
                    currentPhase = AgentPhase.Positioning;
                    AddReward(-0.1f);
                }
            }
        }
    }
}
