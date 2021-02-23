using System;

using Barotrauma;
using Barotrauma.Extensions;

using HarmonyLib;

using System.Linq;

namespace TestMod
{
    public class BarotraumaTestMod
    {
        [ModInitMethod]
        public static void Init() 
        {
            Harmony harmony = new Harmony("Barotrauma.TestMod");
            harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

            DebugConsole.NewMessage("TestMod init");
        }
    }

    [HarmonyPatch(typeof(Barotrauma.Character), MethodType.Constructor, typeof(CharacterPrefab), typeof(string), typeof(Microsoft.Xna.Framework.Vector2), typeof(string), typeof(CharacterInfo), typeof(ushort), typeof(bool), typeof(RagdollParams))]
    public class CharacterPatch
    {
        public static void Postfix(Character __instance)
        {
            __instance.OnDeath += (character, causeOfDeath) =>
            {
                if (character is Character _character)
                {
                    _character.AnimController.Limbs.ForEach((limb) => { _character.TrySeverLimbJoints(limb, 100.0f, 100.0f, true); });
                    GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage($"{_character.Name}", "Kaboom!", Barotrauma.Networking.ChatMessageType.Dead, _character);
                }
            };
        }
    }
}
