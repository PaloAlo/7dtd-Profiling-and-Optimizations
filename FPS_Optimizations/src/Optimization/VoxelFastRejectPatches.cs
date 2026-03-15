// VoxelFastRejectPatches.cs
//
// Fast voxel-based rejection for path line-of-sight and approach position
// calculations.  Falls back to vanilla physics when voxel says clear.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(GamePath.ASPPathFinder), "IsLineClear")]
static class ASP_IsLineClear_VoxelReject
{
    static bool Prefix(GamePath.ASPPathFinder __instance,
        ref bool __result,
        Vector3 pos1, Vector3 pos2, bool isTall)
    {
        try
        {
            if (Mathf.Abs(pos1.y - pos2.y) > 0.5f)
            {
                __result = false;
                return false;
            }

            if (__instance.entity?.world == null) return true;
            var world = __instance.entity.world;

            const int collisionMask = 1082195968;

            var low1 = pos1; low1.y += 0.5f;
            var low2 = pos2; low2.y += 0.5f;
            var dir = low2 - low1;
            float dist = dir.magnitude;
            if (dist <= 0f) return true;

            var ray = new Ray(low1, dir / dist);
            if (Voxel.GetNextBlockHit(world, ray, dist, collisionMask, false))
            {
                __result = false;
                return false;
            }

            if (isTall)
            {
                low1.y += 1f;
                low2.y += 1f;
                dir = low2 - low1;
                ray = new Ray(low1, dir / dist);
                if (Voxel.GetNextBlockHit(world, ray, dist, collisionMask, false))
                {
                    __result = false;
                    return false;
                }
            }
        }
        catch { }

        return true;
    }
}

[HarmonyPatch(typeof(EAIApproachAndAttackTarget), "GetMoveToLocation")]
static class EAI_GetMoveToLocation_VoxelGround
{
    static bool Prefix(EAIApproachAndAttackTarget __instance,
        ref Vector3 __result,
        float maxDist)
    {
        try
        {
            var entityTarget = __instance.entityTarget;
            var theEntity = __instance.theEntity;
            if (entityTarget?.world == null || theEntity == null)
                return true;

            var pos = entityTarget.position;
            pos += __instance.entityTargetVel * 6f;
            if (__instance.isTargetToEat)
                pos = entityTarget.getBellyPosition();

            pos = entityTarget.world.FindSupportingBlockPos(pos);

            if (maxDist <= 0f)
            {
                __result = pos;
                return false;
            }

            var vector = new Vector3(theEntity.position.x, pos.y, theEntity.position.z);
            var vector2 = pos - vector;
            float magnitude = vector2.magnitude;

            if (magnitude >= 3f)
            {
                __result = pos;
                return false;
            }

            if (magnitude <= maxDist)
            {
                float yDiff = pos.y - theEntity.position.y;
                __result = (yDiff < -3f || yDiff > 1.5f) ? pos : vector;
                return false;
            }

            vector2 *= maxDist / magnitude;
            var vector3 = pos - vector2;
            vector3.y += 0.51f;
            var pos2 = World.worldToBlockPos(vector3);
            var block = entityTarget.world.GetBlock(pos2);
            var block2 = block.Block;

            if (block2.PathType <= 0)
            {
                var belowPos = new Vector3i(pos2.x, pos2.y - 1, pos2.z);
                var belowBlock = entityTarget.world.GetBlock(belowPos);

                if (!belowBlock.isair && !belowBlock.Block.IsMovementBlocked(
                        entityTarget.world, belowPos, belowBlock, BlockFace.None))
                {
                    vector3.y = pos2.y;
                    __result = vector3;
                    return false;
                }

                if (block2.IsElevator(block.rotation))
                {
                    vector3.y = pos.y;
                    __result = vector3;
                    return false;
                }

                return true;
            }

            __result = pos;
            return false;
        }
        catch { }

        return true;
    }
}
