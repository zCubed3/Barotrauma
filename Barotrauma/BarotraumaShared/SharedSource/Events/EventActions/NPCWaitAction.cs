using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
	public class NPCWaitAction : EventAction
    {
        [Serialize("", true)]
        public string NPCTag { get; set; }

        [Serialize(true, true)]
        public bool Wait { get; set; }

        private bool isFinished = false;


        public NPCWaitAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private List<Character> affectedNpcs = null;

        private AIObjectiveGoTo gotoObjective;

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }

            affectedNpcs = ParentEvent.GetTargets(NPCTag).Where(c => c is Character).Select(c => c as Character).ToList();

            foreach (var npc in affectedNpcs)
            {
                if (!(npc.AIController is HumanAIController humanAiController)) { continue; }

                if (Wait)
                {
                    gotoObjective = new AIObjectiveGoTo(npc, npc, humanAiController.ObjectiveManager, repeat: true)
                    {
                        OverridePriority = 100.0f
                    };
                    humanAiController.ObjectiveManager.AddObjective(gotoObjective);
                    humanAiController.ObjectiveManager.WaitTimer = 0.0f;
                }
                else
                {
                    if (gotoObjective != null)
                    {
                        gotoObjective.Abandon = true;
                    }
                }
            }
            isFinished = true;
        }

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            if (affectedNpcs != null)
            {
                foreach (var npc in affectedNpcs)
                {
                    if (npc.Removed || !(npc.AIController is HumanAIController humanAiController)) { continue; }
                    if (gotoObjective != null)
                    {
                        gotoObjective.Abandon = true;
                    }                    
                }
                affectedNpcs = null;
            }
            isFinished = false;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(NPCWaitAction)} -> (NPCTag: {NPCTag.ColorizeObject()}, Wait: {Wait.ColorizeObject()})";
        }
    }
}