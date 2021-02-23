﻿using Microsoft.Xna.Framework;

namespace Barotrauma
{
	public class HumanWalkParams : HumanGroundedParams
    {
        public static HumanWalkParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanWalkParams>(character, AnimationType.Walk);
        public static HumanWalkParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanWalkParams>(character.SpeciesName, AnimationType.Walk, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanWalkParams>();
    }

	public class HumanRunParams : HumanGroundedParams
    {
        public static HumanRunParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanRunParams>(character, AnimationType.Run);
        public static HumanRunParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanRunParams>(character.SpeciesName, AnimationType.Run, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanRunParams>();
    }

	public class HumanSwimFastParams: HumanSwimParams
    {
        public static HumanSwimFastParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanSwimFastParams>(character, AnimationType.SwimFast);
        public static HumanSwimFastParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanSwimFastParams>(character.SpeciesName, AnimationType.SwimFast, fileName);
        }


        public override void StoreSnapshot() => StoreSnapshot<HumanSwimFastParams>();
    }

	public class HumanSwimSlowParams : HumanSwimParams
    {
        public static HumanSwimSlowParams GetDefaultAnimParams(Character character) => GetDefaultAnimParams<HumanSwimSlowParams>(character, AnimationType.SwimSlow);
        public static HumanSwimSlowParams GetAnimParams(Character character, string fileName = null)
        {
            return GetAnimParams<HumanSwimSlowParams>(character.SpeciesName, AnimationType.SwimSlow, fileName);
        }

        public override void StoreSnapshot() => StoreSnapshot<HumanSwimSlowParams>();
    }

	public abstract class HumanSwimParams : SwimParams, IHumanAnimation
    {
        [Serialize(0.5f, true), Editable(DecimalCount = 2)]
        public float LegMoveAmount { get; set; }

        [Serialize(5.0f, true), Editable]
        public float LegCycleLength { get; set; }

        [Serialize("0.5, 0.1", true), Editable(DecimalCount = 2)]
        public Vector2 HandMoveAmount { get; set; }

        [Serialize(0.5f, true), Editable(MinValueFloat = 0, MaxValueFloat = 10, DecimalCount = 2)]
        public float HandMoveStrength { get; set; }

        [Serialize(5.0f, true), Editable]
        public float HandCycleSpeed { get; set; }

        [Serialize("0.0, 0.0", true), Editable(DecimalCount = 2)]
        public Vector2 HandMoveOffset { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float FootAngle
        {
            get => MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                FootAngleInRadians = MathHelper.ToRadians(value);
            }
        }
        public float FootAngleInRadians { get; private set; }

        [Serialize(25.0f, true, description: "How much torque is used to rotate the feet to the correct orientation."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float FootRotateStrength { get; set; }
    }

	public abstract class HumanGroundedParams : GroundedMovementParams, IHumanAnimation
    {
        [Serialize(0.3f, true, description: "How much force is used to force the character upright."), Editable(MinValueFloat = 0, MaxValueFloat = 1, DecimalCount = 2)]
        public float GetUpForce { get; set; }
        
        // -- TODO: use a separate clip for crawling -> replace these when implemented.

        [Serialize(0.65f, true, description: "Height of the torso when crouching."), Editable(MinValueFloat = 0, MaxValueFloat = 5, DecimalCount = 2)]
        public float CrouchingTorsoPos { get; set; }

        [Serialize(0.65f, true, description: "Height of the head when crouching."), Editable(MinValueFloat = 0, MaxValueFloat = 5, DecimalCount = 2)]
        public float CrouchingHeadPos { get; set; }

        /// <summary>
        /// In degrees
        /// </summary>
        [Serialize(-10f, true, description: "Angle of the torso when crouching."), Editable(MinValueFloat = -360, MaxValueFloat = 360)]
        public float CrouchingTorsoAngle { get; set; }

        /// <summary>
        /// In degrees
        /// </summary>
        [Serialize(-10f, true, description: "Angle of the head when crouching."), Editable(MinValueFloat = -360, MaxValueFloat = 360)]
        public float CrouchingHeadAngle { get; set; }

        // --

        [Serialize(0.25f, true, description: "How much the character's head leans forwards when moving."), Editable(DecimalCount = 2)]
        public float HeadLeanAmount { get; set; }

        [Serialize(0.25f, true, description: "How much the character's torso leans forwards when moving."), Editable(DecimalCount = 2)]
        public float TorsoLeanAmount { get; set; }

        [Serialize(15.0f, true, description: "How much force is used to move the feet to the correct position."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float FootMoveStrength { get; set; }

        /// <summary>
        /// In degrees.
        /// </summary>
        [Serialize(0.0f, true), Editable(-360f, 360f)]
        public float FootAngle
        {
            get => MathHelper.ToDegrees(FootAngleInRadians);
            set
            {
                FootAngleInRadians = MathHelper.ToRadians(value);                
            }
        }
        public float FootAngleInRadians { get; private set; }

        [Serialize(20.0f, true, description: "How much torque is used to rotate the feet to the correct orientation."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float FootRotateStrength { get; set; }

        [Serialize("0.0, 0.0", true, description: "Added to the calculated foot positions, e.g. a value of {-1.0, 0.0f} would make the character \"drag\" their feet one unit behind them."), Editable(DecimalCount = 2)]
        public Vector2 FootMoveOffset { get; set; }

        [Serialize("0.0, 0.0", true, description: "Added to the calculated foot positions, e.g. a value of {-1.0, 0.0f} would make the character \"drag\" their feet one unit behind them."), Editable(DecimalCount = 2)]
        public Vector2 CrouchingFootMoveOffset { get; set; }

        [Serialize(10.0f, true, description: "How much torque is used to bend the characters legs when taking a step."), Editable(MinValueFloat = 0, MaxValueFloat = 100)]
        public float LegBendTorque { get; set; }

        [Serialize("0.4, 0.15", true, description: "How much the hands move along each axis."), Editable(DecimalCount = 2)]
        public Vector2 HandMoveAmount { get; set; }

        [Serialize("-0.15, 0.0", true, description: "Added to the calculated hand positions, e.g. a value of {-1.0, 0.0f} would make the character \"drag\" their hands one unit behind them."), Editable(DecimalCount = 2)]
        public Vector2 HandMoveOffset { get; set; }

        [Serialize(0.7f, true, description: "How much force is used to move the hands."), Editable(MinValueFloat = 0, MaxValueFloat = 2, DecimalCount = 2)]
        public float HandMoveStrength { get; set; }

        [Serialize(-1.0f, true, description: "The position of the hands is clamped below this (relative to the position of the character's torso)."), Editable(DecimalCount = 2)]
        public float HandClampY { get; set; }
    }

    public interface IHumanAnimation
    {
        float FootAngle { get; set; }
        float FootAngleInRadians { get; }
        float FootRotateStrength { get; set; }
    }
}
