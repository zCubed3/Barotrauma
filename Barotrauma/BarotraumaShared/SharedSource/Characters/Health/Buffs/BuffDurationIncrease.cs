﻿using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
	public class BuffDurationIncrease : Affliction
    {
        public BuffDurationIncrease(AfflictionPrefab prefab, float strength) : base(prefab, strength)
        {

        }

        public override void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            base.Update(characterHealth, targetLimb, deltaTime);

            var afflictions = characterHealth.GetAllAfflictions();

            if (Strength <= 0)
            {
                foreach (Affliction affliction in afflictions)
                {
                    if (!affliction.Prefab.IsBuff || affliction == this || affliction.MultiplierSource != this) { continue; }
                    affliction.MultiplierSource = null;
                    affliction.StrengthDiminishMultiplier = 1f;
                }
            }
            else
            {
                foreach (Affliction affliction in afflictions)
                {
                    if (!affliction.Prefab.IsBuff || affliction == this) { continue; }
                    float multiplier = GetDiminishMultiplier();
                    if (affliction.StrengthDiminishMultiplier < multiplier && affliction.MultiplierSource != this) { continue; }

                    affliction.MultiplierSource = this;
                    affliction.StrengthDiminishMultiplier = multiplier;
                }
            }
        }

        private float GetDiminishMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) { return 1.0f; }
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) { return 1.0f; }

            float multiplier = MathHelper.Lerp(
                currentEffect.MinBuffMultiplier,
                currentEffect.MaxBuffMultiplier,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
            return 1.0f / Math.Max(multiplier, 0.001f);
        }
    }
}
