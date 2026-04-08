// VehicleRigidbodySleepPatch.cs
//
// Puts idle vehicle rigidbodies to sleep in PhysicsFixedUpdate.
// When a vehicle has no driver, no movement input, and negligible
// velocity/angular velocity, Unity's physics engine can skip collision
// detection entirely for that rigidbody.
//
// Wakes the rigidbody immediately when any of those conditions change.

using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(EntityVehicle), "PhysicsFixedUpdate")]
public static class VehicleRigidbodySleepPatch
{
    public static bool Prefix(EntityVehicle __instance)
    {
        if (!OptimizationConfig.Current.EnableVehicleRigidbodySleep) return true;

        try
        {
            Rigidbody rb = __instance.vehicleRB;
            if (rb == null) return true;

            Vector3 velocity = rb.velocity;
            Vector3 angularVelocity = rb.angularVelocity;
            bool isMoving = velocity.sqrMagnitude > 0.0001f
                         || angularVelocity.sqrMagnitude > 0.0001f;

            MovementInput input = __instance.movementInput;
            bool hasInput = input != null
                         && (input.moveForward != 0f
                          || input.moveStrafe != 0f
                          || input.jump
                          || input.down);

            bool hasDriver = __instance.AttachedMainEntity != null;

            if (!hasInput && !isMoving && !hasDriver)
            {
                if (!rb.IsSleeping())
                    rb.Sleep();

                ProfilerCounterBridge.Increment("Vehicle.RBSleep");
                return false;
            }

            if (rb.IsSleeping())
                rb.WakeUp();

            return true;
        }
        catch
        {
            return true;
        }
    }
}
