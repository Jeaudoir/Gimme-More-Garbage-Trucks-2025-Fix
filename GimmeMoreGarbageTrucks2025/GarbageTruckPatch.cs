using HarmonyLib;
using ColossalFramework;

namespace GimmeMoreGarbageTrucks2025
{
    // Harmony patch that intercepts vanilla GarbageTruckAI.SetTarget() calls
    // and redirects them to our custom logic in CustomGarbageTruckAI.
    // This allows us to override truck assignment decisions with smarter dispatch logic.
    [HarmonyPatch(typeof(GarbageTruckAI), "SetTarget")]
    public class GarbageTruckPatch
    {
        public static bool Prefix(ushort vehicleID, ref Vehicle data, ushort targetBuilding, GarbageTruckAI __instance)
        {
            // Only intercept if the dispatcher is initialized and enabled
            if (!Dispatcher.IsInitialized)
                return true; // Let original method run
            
            // Use our custom logic instead of the original
            Dispatcher.Instance.CustomGarbageTruckAI.SetTarget(vehicleID, ref data, targetBuilding);
            
            // Skip the original method
            return false;
        }
    }
}