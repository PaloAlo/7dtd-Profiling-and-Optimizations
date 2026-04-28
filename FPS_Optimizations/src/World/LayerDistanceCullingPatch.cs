// LayerDistanceCullingPatch.cs
//
// Sets Camera.main.layerCullDistances for the two most expensive distant-
// render layers in 7DTD:
//   Layer 23 — terrain detail / voxel meshes
//   Layer 28 — vegetation / grass
//
// The cull distance is derived from the player's Y (elevation):
//   ground-level (y=0):   detail=75m,  vegetation=150m
//   high altitude (y=200): detail=225m, vegetation=450m
//
// Uses spherical culling (layerCullSpherical=true) so the fade is
// calculated as sphere distance from the camera rather than depth-
// plane, giving correct horizon pop-in behaviour.
//
// Updated every 120 frames (~2 s at 60 fps) to avoid per-frame
// Camera property writes.  Resets when the patch is disabled.
//
// Adapted from Redbeardt's Afterlife mod (LayerDistanceCulling.cs),
// rewritten to use OptimizationConfig + GameManager.gmUpdate hook
// instead of Afterlife-specific utilities.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(GameManager), nameof(GameManager.gmUpdate))]
public static class LayerDistanceCullingPatch
{
    private static readonly float[] s_distances = new float[32];
    private static bool s_applied;
    private static int s_lastApplyFrame = -1;
    private const int UPDATE_INTERVAL = 120; // frames

    [HarmonyPostfix]
    public static void Postfix()
    {
        if (!OptimizationConfig.Current.EnableLayerDistanceCulling)
        {
            if (s_applied) Reset();
            return;
        }

        int frame = Time.frameCount;
        if (frame - s_lastApplyFrame < UPDATE_INTERVAL) return;
        s_lastApplyFrame = frame;

        Camera cam = Camera.main;
        if (cam == null) return;

        EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer() as EntityPlayerLocal;
        if (player == null) return;

        // Scale cull radius with elevation — higher ground = further horizon.
        float elevationBonus = Mathf.Pow(Mathf.Max(0f, player.GetBlockPosition().y), 1.4f);
        float vegDist    = 150f + elevationBonus;
        float detailDist = vegDist * 0.5f;

        s_distances[23] = detailDist;
        s_distances[28] = vegDist;

        cam.layerCullDistances = s_distances;
        cam.layerCullSpherical = true;
        s_applied = true;
    }

    private static void Reset()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        // Restore default (all zeros = no per-layer override)
        cam.layerCullDistances = new float[32];
        cam.layerCullSpherical = false;
        s_applied = false;
    }

    public static void ClearCaches() => s_lastApplyFrame = -1;
}
