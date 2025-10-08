using UnityEngine;
using System.Collections.Generic;

public static class FurnitureReward
{
    public static float WallDistanceReward(GameObject nearestWall, float nearestWallDis, float targetDist, Vector3 agentForward)
    {
        float distanceReward = -Mathf.Abs(nearestWallDis - targetDist);

        // 각도 보상
        float angleReward = 0f;
        if (nearestWall != null)
        {
            Vector3 wallForward = nearestWall.transform.forward;
            float alignment = Vector3.Dot(agentForward.normalized, wallForward.normalized);
            angleReward = alignment; // (1에 가까울수록 보상)
        }

        return distanceReward + angleReward; // 각도 보상 가중치
    }

}
