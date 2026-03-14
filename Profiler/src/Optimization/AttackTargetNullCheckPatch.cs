// AttackTargetNullCheckPatch.cs
//
// Defensive patch for vanilla NullReferenceException in
// GetAttackTargetHitPosition(). The game calls
// attackTarget.getChestPosition() without checking for null,
// which crashes when the target is killed between the AI decision
// and the attack execution frame (commonly seen with Vultures).

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.GetAttackTargetHitPosition))]
static class AttackTargetNullCheckPatch
{
    public static bool Prefix(EntityAlive __instance, ref Vector3 __result)
    {
        if (__instance.attackTarget == null)
        {
            __result = __instance.getChestPosition();
            return false;
        }
        return true;
    }
}
