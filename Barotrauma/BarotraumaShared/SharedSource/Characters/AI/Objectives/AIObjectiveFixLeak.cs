﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
	public class AIObjectiveFixLeak : AIObjective
    {
        public override string DebugTag => "fix leak";
        public override bool ForceRun => true;
        public override bool KeepDivingGearOn => true;
        public override bool AllowInAnySub => true;

        public Gap Leak { get; private set; }

        private AIObjectiveGetItem getWeldingTool;
        private AIObjectiveContainItem refuelObjective;
        private AIObjectiveGoTo gotoObjective;
        private AIObjectiveOperateItem operateObjective;

        public readonly bool isPriority;

        public AIObjectiveFixLeak(Gap leak, Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1, bool isPriority = false) : base (character, objectiveManager, priorityModifier)
        {
            Leak = leak;
            this.isPriority = isPriority;
        }

        protected override bool Check() => Leak.Open <= 0 || Leak.Removed;

        public override float GetPriority()
        {
            if (!IsAllowed)
            {
                Priority = 0;
                Abandon = true;
            }
            else if (HumanAIController.IsTrueForAnyCrewMember(other => other != HumanAIController && other.ObjectiveManager.GetActiveObjective<AIObjectiveFixLeak>()?.Leak == Leak))
            {
                Priority = 0;
                Abandon = true;
            }
            else
            {
                float xDist = Math.Abs(character.WorldPosition.X - Leak.WorldPosition.X);
                float yDist = Math.Abs(character.WorldPosition.Y - Leak.WorldPosition.Y);
                // Vertical distance matters more than horizontal (climbing up/down is harder than moving horizontally).
                // If the target is close, ignore the distance factor alltogether so that we keep fixing the leaks that are nearby.
                float distanceFactor = isPriority || xDist < 200 && yDist < 100 ? 1 : MathHelper.Lerp(1, 0.1f, MathUtils.InverseLerp(0, 3000, xDist + yDist * 3.0f));
                float severity = isPriority ? 1 : AIObjectiveFixLeaks.GetLeakSeverity(Leak) / 100;
                float reduction = isPriority ? 1 : 2;
                float max = MathHelper.Min(AIObjectiveManager.OrderPriority - reduction, 90);
                float devotion = CumulatedDevotion / 100;
                Priority = MathHelper.Lerp(0, max, MathHelper.Clamp(devotion + (severity * distanceFactor * PriorityModifier), 0, 1));
            }
            return Priority;
        }

        protected override void Act(float deltaTime)
        {
            var weldingTool = character.Inventory.FindItemByTag("weldingequipment", true);
            if (weldingTool == null)
            {
                TryAddSubObjective(ref getWeldingTool, () => new AIObjectiveGetItem(character, "weldingequipment", objectiveManager, equip: true, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC), 
                    onAbandon: () =>
                    {
                        if (objectiveManager.IsCurrentOrder<AIObjectiveFixLeaks>())
                        {
                            character.Speak(TextManager.Get("dialogcannotfindweldingequipment"), null, 0.0f, "dialogcannotfindweldingequipment", 10.0f);
                        }
                        Abandon = true;
                    },
                    onCompleted: () => RemoveSubObjective(ref getWeldingTool));
                return;
            }
            else
            {
                if (weldingTool.OwnInventory == null)
                {
#if DEBUG
                    DebugConsole.ThrowError($"{character.Name}: AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no proper inventory");
#endif
                    Abandon = true;
                    return;
                }
                // Drop empty tanks
                if (weldingTool.OwnInventory.AllItems.Any(it => it.Condition <= 0.0f))
                {
                    foreach (Item containedItem in weldingTool.OwnInventory.AllItemsMod)
                    {
                        if (containedItem.Condition <= 0.0f)
                        {
                            containedItem.Drop(character);
                        }
                    }
                }

                if (weldingTool.OwnInventory.AllItems.None(i => i.HasTag("weldingfuel") && i.Condition > 0.0f))
                {
                    TryAddSubObjective(ref refuelObjective, () => new AIObjectiveContainItem(character, "weldingfuel", weldingTool.GetComponent<ItemContainer>(), objectiveManager, spawnItemIfNotFound: character.TeamID == CharacterTeamType.FriendlyNPC), 
                        onAbandon: () => Abandon = true,
                        onCompleted: () => RemoveSubObjective(ref refuelObjective));
                    return;
                }
            }
            if (subObjectives.Any()) { return; }
            var repairTool = weldingTool.GetComponent<RepairTool>();
            if (repairTool == null)
            {
#if DEBUG
                DebugConsole.ThrowError($"{character.Name}: AIObjectiveFixLeak failed - the item \"" + weldingTool + "\" has no RepairTool component but is tagged as a welding tool");
#endif
                Abandon = true;
                return;
            }
            Vector2 toLeak = Leak.WorldPosition - character.WorldPosition;
            // TODO: use the collider size/reach?
            if (!character.AnimController.InWater && Math.Abs(toLeak.X) < 100 && toLeak.Y < 0.0f && toLeak.Y > -150)
            {
                HumanAIController.AnimController.Crouching = true;
            }
            float reach = CalculateReach(repairTool, character);
            bool canOperate = toLeak.LengthSquared() < reach * reach;
            if (canOperate)
            {
                TryAddSubObjective(ref operateObjective, () => new AIObjectiveOperateItem(repairTool, character, objectiveManager, option: "", requireEquip: true, operateTarget: Leak), 
                    onAbandon: () => Abandon = true,
                    onCompleted: () =>
                    {
                        if (Check()) { IsCompleted = true; }
                        else
                        {
                            // Failed to operate. Probably too far.
                            Abandon = true;
                        }
                    });
            }
            else
            {
                TryAddSubObjective(ref gotoObjective, () => new AIObjectiveGoTo(Leak, character, objectiveManager)
                {
                    CloseEnough = reach,
                    DialogueIdentifier = Leak.FlowTargetHull != null ? "dialogcannotreachleak" : null,
                    TargetName = Leak.FlowTargetHull?.DisplayName,
                    CheckVisibility = false
                },
                onAbandon: () =>
                {
                    if (Check()) { IsCompleted = true; }
                    else if ((Leak.WorldPosition - character.WorldPosition).LengthSquared() > MathUtils.Pow(reach * 2, 2))
                    {
                        // Too far
                        Abandon = true;
                    }
                    else
                    {
                        // We are close, try again.
                        RemoveSubObjective(ref gotoObjective);
                    }
                },
                onCompleted: () => RemoveSubObjective(ref gotoObjective));
            }
        }

        public override void Reset()
        {
            base.Reset();
            getWeldingTool = null;
            refuelObjective = null;
            gotoObjective = null;
            operateObjective = null;
        }

        public static float CalculateReach(RepairTool repairTool, Character character)
        {
            float armLength = ConvertUnits.ToDisplayUnits(((HumanoidAnimController)character.AnimController).ArmLength);
            // This is an approximation, because we don't know the exact reach until the pose is taken.
            // And even then the actual range depends on the direction we are aiming to.
            // Found out that without any multiplier the value (209) is often too short.
            return repairTool.Range + armLength * 1.2f;
        }
    }
}
