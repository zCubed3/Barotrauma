using System;

namespace Barotrauma
{
    public enum CauseOfDeathType
    {
        Unknown, Pressure, Suffocation, Drowning, Affliction, Disconnected
    }

	public class CauseOfDeath
    {
        public readonly CauseOfDeathType Type;
        public readonly AfflictionPrefab Affliction;
        public readonly Character Killer;
        public readonly Entity DamageSource;

        public CauseOfDeath(CauseOfDeathType type, AfflictionPrefab affliction, Character killer, Entity damageSource)
        {
            if (type == CauseOfDeathType.Affliction && affliction == null)
            {
                string errorMsg = "Invalid cause of death (the type of the cause of death was Affliction, but affliction was not specified).\n" + Environment.StackTrace.CleanupStackTrace();
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("InvalidCauseOfDeath", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                type = CauseOfDeathType.Unknown;
            }

            Type = type;
            Affliction = affliction;
            Killer = killer;
            DamageSource = damageSource;
        }
    }
}
