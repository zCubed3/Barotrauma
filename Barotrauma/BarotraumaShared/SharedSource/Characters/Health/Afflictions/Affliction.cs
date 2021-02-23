﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
	public class Affliction : ISerializableEntity
    {
        public readonly AfflictionPrefab Prefab;

        public string Name => ToString();

        public Dictionary<string, SerializableProperty> SerializableProperties { get; set; }

        protected float _strength;

        [Serialize(0f, true), Editable]
        public virtual float Strength
        {
            get { return _strength; }
            set
            {
                if (_nonClampedStrength < 0 && value > 0)
                {
                    _nonClampedStrength = value;
                }
                _strength = MathHelper.Clamp(value, 0.0f, Prefab.MaxStrength);
            }
        }

        private float _nonClampedStrength = -1;
        public float NonClampedStrength => _nonClampedStrength > 0 ? _nonClampedStrength : _strength;

        [Serialize("", true), Editable]
        public string Identifier { get; private set; }

        [Serialize(1.0f, true, description: "The probability for the affliction to be applied."), Editable(minValue: 0f, maxValue: 1f)]
        public float Probability { get; set; } = 1.0f;

        public float DamagePerSecond;
        public float DamagePerSecondTimer;
        public float PreviousVitalityDecrease;

        public float StrengthDiminishMultiplier = 1.0f;
        public Affliction MultiplierSource;

        public readonly Dictionary<AfflictionPrefab.PeriodicEffect, float> PeriodicEffectTimers = new Dictionary<AfflictionPrefab.PeriodicEffect, float>();

        /// <summary>
        /// Which character gave this affliction
        /// </summary>
        public Character Source;

        public Affliction(AfflictionPrefab prefab, float strength)
        {
            Prefab = prefab;
            _strength = strength;
            Identifier = prefab?.Identifier;

            foreach (var periodicEffect in prefab.PeriodicEffects)
            {
                PeriodicEffectTimers[periodicEffect] = Rand.Range(periodicEffect.MinInterval, periodicEffect.MaxInterval);
            }
        }

        public void Serialize(XElement element)
        {
            SerializableProperty.SerializeProperties(this, element);
        }

        public void Deserialize(XElement element)
        {
            SerializableProperties = SerializableProperty.DeserializeProperties(this, element);
        }

        public Affliction CreateMultiplied(float multiplier)
        {
            return Prefab.Instantiate(NonClampedStrength * multiplier, Source);
        }

        public override string ToString() => Prefab == null ? "Affliction (Invalid)" : $"Affliction ({Prefab.Name})";

        public float GetVitalityDecrease(CharacterHealth characterHealth)
        {
            if (Strength < Prefab.ActivationThreshold) { return 0.0f; }
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) { return 0.0f; }
            if (currentEffect.MaxStrength - currentEffect.MinStrength <= 0.0f) { return 0.0f; }

            float currVitalityDecrease = MathHelper.Lerp(
                currentEffect.MinVitalityDecrease, 
                currentEffect.MaxVitalityDecrease, 
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));

            if (currentEffect.MultiplyByMaxVitality)
            {
                currVitalityDecrease *= characterHealth == null ? 100.0f : characterHealth.MaxVitality;
            }

            return currVitalityDecrease;
        }

        public float GetScreenDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxScreenDistortStrength - currentEffect.MinScreenDistortStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinScreenDistortStrength,
                currentEffect.MaxScreenDistortStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetRadialDistortStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxRadialDistortStrength - currentEffect.MinRadialDistortStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinRadialDistortStrength,
                currentEffect.MaxRadialDistortStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetChromaticAberrationStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxChromaticAberrationStrength - currentEffect.MinChromaticAberrationStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinChromaticAberrationStrength,
                currentEffect.MaxChromaticAberrationStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public float GetScreenBlurStrength()
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxScreenBlurStrength - currentEffect.MinScreenBlurStrength <= 0.0f) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinScreenBlurStrength,
                currentEffect.MaxScreenBlurStrength,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public void CalculateDamagePerSecond(float currentVitalityDecrease)
        {
            DamagePerSecond = Math.Max(DamagePerSecond, currentVitalityDecrease - PreviousVitalityDecrease);
            if (DamagePerSecondTimer >= 1.0f)
            {
                DamagePerSecond = currentVitalityDecrease - PreviousVitalityDecrease;
                PreviousVitalityDecrease = currentVitalityDecrease;
                DamagePerSecondTimer = 0.0f;
            }
        }

        public float GetResistance(string afflictionId)
        {
            if (Strength < Prefab.ActivationThreshold) return 0.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 0.0f;
            if (currentEffect.MaxResistance - currentEffect.MinResistance <= 0.0f) return 0.0f;
            if (afflictionId != null && afflictionId != currentEffect.ResistanceFor) return 0.0f;

            return MathHelper.Lerp(
                currentEffect.MinResistance,
                currentEffect.MaxResistance,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }    

        public float GetSpeedMultiplier()
        {
            if (Strength < Prefab.ActivationThreshold) return 1.0f;
            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) return 1.0f;
            if (currentEffect.MaxSpeedMultiplier - currentEffect.MinSpeedMultiplier <= 0.0f) return 1.0f;

            return MathHelper.Lerp(
                currentEffect.MinSpeedMultiplier,
                currentEffect.MaxSpeedMultiplier,
                (Strength - currentEffect.MinStrength) / (currentEffect.MaxStrength - currentEffect.MinStrength));
        }

        public virtual void Update(CharacterHealth characterHealth, Limb targetLimb, float deltaTime)
        {
            foreach (AfflictionPrefab.PeriodicEffect periodicEffect in Prefab.PeriodicEffects)
            {
                PeriodicEffectTimers[periodicEffect] -= deltaTime;
                if (PeriodicEffectTimers[periodicEffect] <= 0.0f)
                {
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                    {
                        PeriodicEffectTimers[periodicEffect] = 0.0f;
                    }
                    else
                    {
                        foreach (StatusEffect statusEffect in periodicEffect.StatusEffects)
                        {
                            ApplyStatusEffect(statusEffect, 1.0f, characterHealth, targetLimb);
                            PeriodicEffectTimers[periodicEffect] = Rand.Range(periodicEffect.MinInterval, periodicEffect.MaxInterval);
                        }
                    }
                }
            }            

            AfflictionPrefab.Effect currentEffect = Prefab.GetActiveEffect(Strength);
            if (currentEffect == null) { return; }

            if (currentEffect.StrengthChange < 0) // Reduce diminishing of buffs if boosted
            {
                _strength += currentEffect.StrengthChange * deltaTime * StrengthDiminishMultiplier;
            }
            else // Reduce strengthening of afflictions if resistant
            {
                _strength += currentEffect.StrengthChange * deltaTime * (1f - characterHealth.GetResistance(Prefab.Identifier));
            }
            // Don't use the property, because it's virtual and some afflictions like husk overload it for external use.
            _strength = MathHelper.Clamp(_strength, 0.0f, Prefab.MaxStrength);

            foreach (StatusEffect statusEffect in currentEffect.StatusEffects)
            {
                ApplyStatusEffect(statusEffect, deltaTime, characterHealth, targetLimb);
            }
        }

        public void ApplyStatusEffect(StatusEffect statusEffect, float deltaTime, CharacterHealth characterHealth, Limb targetLimb)
        {
            statusEffect.SetUser(Source);
            if (statusEffect.HasTargetType(StatusEffect.TargetType.Character))
            {
                statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, characterHealth.Character);
            }
            if (targetLimb != null && statusEffect.HasTargetType(StatusEffect.TargetType.Limb))
            {
                statusEffect.Apply(ActionType.OnActive, deltaTime, characterHealth.Character, targetLimb);
            }
            if (targetLimb != null && statusEffect.HasTargetType(StatusEffect.TargetType.AllLimbs))
            {
                statusEffect.Apply(ActionType.OnActive, deltaTime, targetLimb.character, targetLimb.character.AnimController.Limbs.Cast<ISerializableEntity>().ToList());
            }
            if (statusEffect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                statusEffect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
            {
                var targets = new List<ISerializableEntity>();
                statusEffect.GetNearbyTargets(characterHealth.Character.WorldPosition, targets);
                statusEffect.Apply(ActionType.OnActive, deltaTime, targetLimb.character, targets);
            }
        }

        /// <summary>
        /// Use this method to skip clamping and additional logic of the setters.
        /// Intended only to be used when the value is already clamped! (networking code)
        /// Ideally we would keep this private, but doing so would require too much refactoring.
        /// </summary>
        public void SetStrength(float strength) => _strength = strength;

        public bool ShouldShowIcon(Character afflictedCharacter)
        {
            return Strength >= (afflictedCharacter == Character.Controlled ? Prefab.ShowIconThreshold : Prefab.ShowIconToOthersThreshold);
        }
    }
}
