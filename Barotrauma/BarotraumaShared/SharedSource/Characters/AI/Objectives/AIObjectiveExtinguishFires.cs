﻿using System;
using System.Linq;
using System.Collections.Generic;
using Barotrauma.Extensions;
using Microsoft.Xna.Framework;

namespace Barotrauma
{
	public class AIObjectiveExtinguishFires : AIObjectiveLoop<Hull>
    {
        public override string DebugTag => "extinguish fires";
        public override bool ForceRun => true;
        public override bool AllowInAnySub => true;

        public AIObjectiveExtinguishFires(Character character, AIObjectiveManager objectiveManager, float priorityModifier = 1) : base(character, objectiveManager, priorityModifier) { }

        protected override bool Filter(Hull hull) => IsValidTarget(hull, character);

        protected override float TargetEvaluation() => 
            // If any target is visible -> 100 priority
            Targets.Any(t => t == character.CurrentHull || HumanAIController.VisibleHulls.Contains(t)) ? 100 : 
            // Else based on the fire severity
            Targets.Sum(t =>  GetFireSeverity(t) * 100);

        /// <summary>
        /// 0-1 based on the horizontal size of all of the fires in the hull.
        /// </summary>
        public static float GetFireSeverity(Hull hull) => MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, Math.Min(hull.Rect.Width, 1000), hull.FireSources.Sum(fs => fs.Size.X)));

        protected override IEnumerable<Hull> GetList() => Hull.hullList;

        protected override AIObjective ObjectiveConstructor(Hull target) 
            => new AIObjectiveExtinguishFire(character, target, objectiveManager, PriorityModifier);

        protected override void OnObjectiveCompleted(AIObjective objective, Hull target) 
            => HumanAIController.RemoveTargets<AIObjectiveExtinguishFires, Hull>(character, target);

        public static bool IsValidTarget(Hull hull, Character character)
        {
            if (hull == null) { return false; }
            if (hull.FireSources.None()) { return false; }
            if (hull.Submarine == null) { return false; }
            if (character.Submarine == null) { return false; }
            if (!character.Submarine.IsEntityFoundOnThisSub(hull, includingConnectedSubs: true)) { return false; }
            return true;
        }
    }
}
