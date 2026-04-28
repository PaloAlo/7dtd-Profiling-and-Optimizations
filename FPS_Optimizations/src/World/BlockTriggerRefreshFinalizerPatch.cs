// BlockTriggerRefreshFinalizerPatch.cs
//
// Harmony finalizer that suppresses exceptions in BlockTrigger.Refresh
// to prevent the game from crashing on corrupt block trigger data.

using System;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(BlockTrigger), "Refresh")]
public static class BlockTriggerRefreshFinalizerPatch
{
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null)
            return null;

        try
        {
            Debug.LogWarning($"Suppressed exception in BlockTrigger.Refresh: {__exception.GetType().Name}: {__exception.Message}");
            Debug.LogException(__exception);
        }
        catch { }

        return null;
    }
}
