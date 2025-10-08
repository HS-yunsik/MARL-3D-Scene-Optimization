using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Integrations.Match3;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class ParentAgent : Agent
{
    [Header("Reward Settings")]
    public RewardSpec rewardSpec;

    [Header("Movement Settings")]
    public float moveSpeed = 2f;

    [Header("Environment Settings")]
    public Collider groundCollider;
    public Collider[] WallCollider;

    [Header("Freeze Settings")]
    public bool isFrozen = false;

    public FurnitureEnvController controller;

    private Collider myCol;
    private Bounds areaBounds;
    public float furnitureScore = 10.0f;

    public GameObject NearestWall;
    public float NearestWallDis;

    public bool avoidCollision = true;

    public float currenttargetdis;
    public bool targetDisOK;
    public bool targetRotOK;
    public bool CollisionOK;

    public override void Initialize()
    {
        myCol = GetComponent<Collider>();
        (NearestWall, NearestWallDis) = GetClosestWall(); // 벽 기준
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. 이동 관련 상태 (자기 자신의 속성)
        sensor.AddObservation(transform.localPosition);       // 자기 위치
        sensor.AddObservation(transform.forward);             // 자기 방향 벡터

        Vector3 size = myCol.bounds.size;
        sensor.AddObservation(size.x);  // 폭
        sensor.AddObservation(size.z);  // 깊이

        // 2. RewardSpec 관련 파라미터 관측
        sensor.AddObservation(rewardSpec.targetDistance);
        sensor.AddObservation(rewardSpec.freezeTolerance);
        sensor.AddObservation(rewardSpec.angleThresholdCos);
        sensor.AddObservation(rewardSpec.wDist);
        sensor.AddObservation(rewardSpec.wAngle);

        // 3. 가장 가까운 벽, 벽과의 거리 관측
        sensor.AddObservation(NearestWallDis);
        sensor.AddObservation(Vector3.Dot(transform.forward.normalized, NearestWall.transform.forward.normalized));

        // 4. 충돌 여부 (피처로 넣을 수 있음)
        sensor.AddObservation(avoidCollision ? 1f : 0f);

    }
    public override void OnEpisodeBegin()
    {
        if (groundCollider != null)
            areaBounds = groundCollider.bounds;


        var areaRoot = transform.root; // 혹은 controller.area
        WallCollider = areaRoot.GetComponentsInChildren<Collider>();


        isFrozen = false;
    }


    public void MoveAgent(ActionSegment<int> act)
    {
        var action = act[0];
        Vector3 moveDir = Vector3.zero;

        switch (action)
        {
            case 0:
                moveDir = Vector3.zero; // 정지
                break;
            case 1:
                moveDir = transform.forward; // 앞으로
                break;
            case 2:
                moveDir = -transform.forward; // 뒤로
                break;
            case 3:
                moveDir = -transform.right; // 왼쪽
                break;
            case 4:
                moveDir = transform.right; // 오른쪽
                break;
            case 5:
                transform.Rotate(Vector3.up, 90f); // 왼쪽 90도 회전
                break;
            case 6:
                transform.Rotate(Vector3.up, -90f); // 오른쪽 90도 회전
                break;
        }

        Vector3 newPos = transform.position + moveDir * Time.deltaTime;
        // ground 경계 가져오기
        Bounds gBounds = groundCollider.bounds;
        Bounds myBounds = GetComponent<Collider>().bounds;


        float clampedX = Mathf.Clamp(newPos.x, gBounds.min.x * 0.9f, gBounds.max.x * 0.9f);
        float clampedZ = Mathf.Clamp(newPos.z, gBounds.min.z * 0.9f, gBounds.max.z * 0.9f);

        // Y는 유지
        transform.position = new Vector3(clampedX, transform.position.y, clampedZ);
    }

    public float prevTargetDist = float.MaxValue; // 이전 step에서 부모/벽과의 거리 기록용

    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        if (isFrozen) return;

        MoveAgent(actionBuffers.DiscreteActions);

        float reward = 0f;

        (NearestWall, NearestWallDis) = GetClosestWall(); // 벽 기준

        // ✅ 거리 shaping 보상 (이전보다 가까워졌으면 +, 멀어지면 -)
        if (prevTargetDist < float.MaxValue) // 첫 step은 비교 불가
        {
            float delta = prevTargetDist - NearestWallDis;
            reward += delta * rewardSpec.wDist * 10f;
        }
        prevTargetDist = NearestWallDis; // 다음 step을 위해 업데이트

        float cosVal = 0.0f;
        // ✅ 각도 보상 (가까운 벽과 정렬될수록 보상 ↑) 서로의 forward방향이 일치할수록 보상 높게
        cosVal = Vector3.Dot(transform.forward.normalized, NearestWall.transform.forward.normalized);


        reward += cosVal * rewardSpec.wAngle * 0.5f; // 코사인값 (-1~1) → 정렬되면 +1


        AddReward(reward);

        // Freeze 조건 체크 (목표 위치 + 각도 모두 만족)
        targetDisOK = NearestWallDis < (rewardSpec.targetDistance + rewardSpec.freezeTolerance);
        targetRotOK = cosVal > rewardSpec.angleThresholdCos;
        CollisionOK = avoidCollision;

        if (targetDisOK && targetRotOK && avoidCollision)
        {
            isFrozen = true;
            controller.setNumOfAgents();
            AddReward(rewardSpec.successBonus);
        }
    }

    private (GameObject wall, float distance) GetClosestWall()
    {
        Collider closestWall = null;
        float bestDist = float.MaxValue;

        foreach (var wall in WallCollider)
        {
            // 에이전트 중심과 벽의 ClosestPoint 사이 거리
            Vector3 a = myCol.transform.position;
            Vector3 b = wall.ClosestPoint(myCol.bounds.center);
            a.y = 0; b.y = 0; // ✅ Y 무시
            float dist = Vector3.Distance(a, b);
            if (dist < bestDist)
            {
                bestDist = dist;
                closestWall = wall;
            }
        }

        Vector3 p2 = closestWall.ClosestPoint(myCol.bounds.center);
        Vector3 p1 = myCol.ClosestPoint(p2);

        p1.y = 0; p2.y = 0; // ✅ Y 무시
        float col2coldist = Vector3.Distance(p1, p2);
        return (closestWall.gameObject, col2coldist);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("furniture") && other != myCol && !isFrozen)
        {
            avoidCollision = false;
            AddReward(-2f);
        }
        if (other.CompareTag("wall")) AddReward(-2f);
    }


    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("furniture") && other != myCol && !isFrozen)
        {
            avoidCollision = true;
            AddReward(+0.5f);
        }
        if (other.CompareTag("wall")) AddReward(+0.1f);
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("furniture") && other != myCol && !isFrozen)
        {
            avoidCollision = false;
            AddReward(-5f);
            //Debug.Log($"collision: {gameObject.name}, {other.name}");
        }
        if (other.CompareTag("wall")) AddReward(-5f);
    }


    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        if (Input.GetKey(KeyCode.D)) discreteActionsOut[0] = 3;
        else if (Input.GetKey(KeyCode.W)) discreteActionsOut[0] = 1;
        else if (Input.GetKey(KeyCode.A)) discreteActionsOut[0] = 4;
        else if (Input.GetKey(KeyCode.S)) discreteActionsOut[0] = 2;
        else if (Input.GetKey(KeyCode.Q)) discreteActionsOut[0] = 5;
        else if (Input.GetKey(KeyCode.E)) discreteActionsOut[0] = 6;
    }
}
