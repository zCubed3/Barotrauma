using HarmonyLib;

namespace Barotrauma.InternalPatches
{
    [HarmonyPatch(typeof(Harmony), MethodType.Constructor, typeof(string))]
    public class HarmonyCtorPatch
    {
        public static void Postfix(Harmony __instance, string id) 
        {
            ModManager.harmonyInstances.Add(__instance);

            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage($"Harmony patch created with id {id}",
                    Microsoft.Xna.Framework.Color.Orange);
            }
        }
    }
}