using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Integrations.Match3;
using Unity.MLAgents.Sensors;
using UnityEngine;

public enum TargetMode { Wall, Parent }

// 에이전트별 설정(Inspector에서 에이전트마다 다르게 세팅)
[System.Serializable]
public class RewardSpec
{
    public TargetMode targetMode = TargetMode.Wall;
    public Transform parentRef;             // Parent 기준일 때 할당
    public float targetDistance = 0.2f;     // 목표 거리
    public float freezeTolerance = 0.1f;    // 거리 허용오차
    public float angleThresholdCos = 0.9f; // 각도 임계(코사인)
    public float wDist = 1.0f;              // 거리 보상 가중치
    public float wAngle = 5f;             // 각도 보상 가중치
    public float successBonus = 5f;         // 성공 보너스
}

public class FurnitureAgent : Agent
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

        // 2. 타겟 모드 (Wall vs Parent)
        sensor.AddOneHotObservation((int)rewardSpec.targetMode, 2);

        // 3. 타겟 관계 정보
        if (rewardSpec.targetMode == TargetMode.Wall)
        {
            (GameObject targetWall, float dist) = GetClosestWall();
            if (targetWall != null)
            {
                sensor.AddObservation(dist);
                sensor.AddObservation(Vector3.Dot(transform.forward.normalized, targetWall.transform.forward.normalized));
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
            // ✅ Wall 모드에서는 부모 Freeze 여부 = 항상 0
            sensor.AddObservation(0f);
        }
        // TargetMode.Parent
        else
        {
            if (rewardSpec.parentRef != null)
            {
                Vector3 toParent = (rewardSpec.parentRef.position - transform.position).normalized;
                float dist = Vector3.Distance(transform.position, rewardSpec.parentRef.position);

                sensor.AddObservation(dist);
                sensor.AddObservation(Vector3.Dot(transform.forward.normalized, toParent));

                // ✅ 부모 Freeze 여부
                var parentAgent = rewardSpec.parentRef.GetComponent<FurnitureAgent>();
                if (parentAgent != null)
                    sensor.AddObservation(parentAgent.isFrozen ? 1f : 0f);
                else
                    sensor.AddObservation(0f);

            }
            else
            {
                // ✅ 부모가 없더라도 차원 맞추기
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
            }
        }

        // 4. 충돌 여부 (피처로 넣을 수 있음)
        sensor.AddObservation(avoidCollision ? 1f : 0f);

    }
    public override void OnEpisodeBegin()
    {
        if (groundCollider != null)
            areaBounds = groundCollider.bounds;
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

        // 벽객체면 벽, 자식객체면 부모
        GameObject targetObj = null;
        float targetDist = float.MaxValue;

        if (rewardSpec.targetMode == TargetMode.Wall)
        {
            (targetObj, targetDist) = GetClosestWall(); // 벽 기준
        }
        else if (rewardSpec.targetMode == TargetMode.Parent && rewardSpec.parentRef != null)
        {
            targetObj = rewardSpec.parentRef.gameObject;
            Vector3 p1 = myCol.ClosestPoint(rewardSpec.parentRef.position);
            Vector3 p2 = rewardSpec.parentRef.GetComponent<Collider>().ClosestPoint(myCol.bounds.center);
            p1.y = 0; p2.y = 0; // ✅ Y 무시
            targetDist = Vector3.Distance(p1, p2);
        }
        if (targetObj == null) return;

        // ✅ 거리 shaping 보상 (이전보다 가까워졌으면 +, 멀어지면 -)
        if (prevTargetDist < float.MaxValue) // 첫 step은 비교 불가
        {
            float delta = prevTargetDist - targetDist;
            reward += delta * rewardSpec.wDist * 10f;
        }
        prevTargetDist = targetDist; // 다음 step을 위해 업데이트

        float cosVal = 0.0f;
        // ✅ 각도 보상 (가까운 벽과 정렬될수록 보상 ↑)
        if (rewardSpec.targetMode == TargetMode.Wall)
        {
            // 벽객체면 벽과의 각도 계산 --> 서로의 forward방향이 일치할수록 보상 높게
            cosVal = Vector3.Dot(transform.forward.normalized, targetObj.transform.forward.normalized);
        }
        else if (rewardSpec.targetMode == TargetMode.Parent && rewardSpec.parentRef != null)
        {
            // 자식객체면 나의 forward 방향과, 부모객체를 향하는 방향이 일치할수록 보상 높게
            Vector3 dirToParent = targetObj.transform.position - transform.position;
            dirToParent.y = 0; // ✅ Y 무시
            cosVal = Vector3.Dot(transform.forward.normalized, dirToParent.normalized);
        }
        reward += cosVal * rewardSpec.wAngle * 0.5f; // 코사인값 (-1~1) → 정렬되면 +1


        AddReward(reward);

        // Freeze 조건 체크 (목표 위치 + 각도 모두 만족)
        targetDisOK = targetDist < (rewardSpec.targetDistance + rewardSpec.freezeTolerance);
        targetRotOK = cosVal > rewardSpec.angleThresholdCos;
        CollisionOK = avoidCollision;

        if (targetDisOK && targetRotOK && avoidCollision)
        {
            bool canFreeze = true;

            if (rewardSpec.targetMode == TargetMode.Parent && rewardSpec.parentRef != null)
            {
                // 부모에도 FurnitureAgent 스크립트가 붙어있어야 함
                var parentAgent = rewardSpec.parentRef.GetComponent<FurnitureAgent>();
                if (parentAgent != null && !parentAgent.isFrozen)
                {
                    // 부모가 아직 고정되지 않음 → 자식도 고정하면 안 됨
                    canFreeze = false;
                }
            }

            if (canFreeze)
            {
                isFrozen = true;
                controller.setNumOfAgents();
                AddReward(rewardSpec.successBonus);
            }
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
        if(other.CompareTag("wall")) AddReward(-2f);
    }


    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("furniture")  && other != myCol && !isFrozen)
        {
            avoidCollision = true;
            AddReward(+0.5f);
        }
        if (other.CompareTag("wall")) AddReward(+0.1f);
    }

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("furniture")  && other != myCol && !isFrozen)
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
    }
}
