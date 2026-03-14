// BlockTriggerRefreshFinalizerPatch.cs

using System;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(BlockTrigger), "Refresh")]
public static class BlockTriggerRefreshFinalizerPatch
{
    // Harmony finalizer — it receives any exception thrown by the original.
    // Returning null suppresses the exception (prevents crash); returning the exception rethrows it.
    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null)
            return null;

        try
        {
            Debug.LogWarning($"Suppressed exception in BlockTrigger.Refresh: {__exception.GetType().Name}: {__exception.Message}");
            Debug.LogException(__exception);
        }
        catch
        {
            // Ensure this finalizer never throws.
        }

        // Returning null tells Harmony the exception has been handled -> don't rethrow.
        return null;
    }
}