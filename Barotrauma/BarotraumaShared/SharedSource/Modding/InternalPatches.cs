using HarmonyLib;

namespace Barotrauma.InternalPatches
{
    [HarmonyPatch(typeof(Harmony), MethodType.Constructor, typeof(string))]
    public class HarmonyCtorPatch
    {
        public static void Postfix(Harmony __instance, string id) 
        {
#if DEBUG
            ModManager.harmonyInstances.Add(__instance);
            DebugConsole.NewMessage($"Harmony patch created with id {id}");
#endif
        }
    }
}