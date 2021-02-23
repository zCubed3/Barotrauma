using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
	public class AfflictionAction : EventAction
    {
        [Serialize("", true)]
        public string Affliction { get; set; }

        [Serialize(0.0f, true)]
        public float Strength { get; set; }

        [Serialize(LimbType.None, true)]
        public LimbType LimbType { get; set; }

        [Serialize("", true)]
        public string TargetTag { get; set; }

        public AfflictionAction(ScriptedEvent parentEvent, XElement element) : base(parentEvent, element) { }

        private bool isFinished = false;

        public override bool IsFinished(ref string goTo)
        {
            return isFinished;
        }

        public override void Reset()
        {
            isFinished = false;
        }

        public override void Update(float deltaTime)
        {
            if (isFinished) { return; }
            var afflictionPrefab = AfflictionPrefab.List.FirstOrDefault(p => p.Identifier.Equals(Affliction, StringComparison.InvariantCultureIgnoreCase));
            if (afflictionPrefab != null)
            {
                var targets = ParentEvent.GetTargets(TargetTag);
                foreach (var target in targets)
                {
                    if (target != null && target is Character character)
                    {
                        var limb = LimbType != LimbType.None ? character.AnimController.GetLimb(LimbType) : null;
                        if (Strength > 0.0f)
                        {
                            character.CharacterHealth.ApplyAffliction(limb, afflictionPrefab.Instantiate(Strength));
                        }
                        else if (Strength < 0.0f)
                        {
                            character.CharacterHealth.ReduceAffliction(limb, Affliction, -Strength);
                        }
                    }
                }
            }
            isFinished = true;
        }

        public override string ToDebugString()
        {
            return $"{ToolBox.GetDebugSymbol(isFinished)} {nameof(AfflictionAction)} -> (TargetTag: {TargetTag.ColorizeObject()}, " +
                   $"Affliction: {Affliction.ColorizeObject()}, Strength: {Strength.ColorizeObject()}, " +
                   $"LimbType: {LimbType.ColorizeObject()})";
        }
    }
}