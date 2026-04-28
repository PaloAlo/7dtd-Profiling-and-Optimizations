// AttackTargetNullCheckPatch.cs
//
// Defensive null check for GetAttackTargetHitPosition — vanilla calls
// attackTarget.getChestPosition() without checking for null, which crashes
// when the target dies between the AI decision and the attack execution frame.

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
