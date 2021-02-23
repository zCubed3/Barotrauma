﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
	public partial class AITarget
    {
        public static List<AITarget> List = new List<AITarget>();

        private Entity entity;
        public Entity Entity
        {
            get 
            { 
                if (entity != null && entity.Removed) { return null; }
                return entity;
            }
        }
        
        private float soundRange;
        private float sightRange;

        /// <summary>
        /// How long does it take for the ai target to fade out if not kept alive.
        /// </summary>
        public float FadeOutTime { get; private set; } = 2;

        public bool Static { get; private set; }
        public bool StaticSound { get; private set; }
        public bool StaticSight { get; private set; }
        
        public float SoundRange
        {
            get { return soundRange; }
            set 
            {
                if (float.IsNaN(value))
                {
                    DebugConsole.ThrowError("Attempted to set the SoundRange of an AITarget to NaN.\n" + Environment.StackTrace.CleanupStackTrace());
                    return;
                }
                soundRange = MathHelper.Clamp(value, MinSoundRange, MaxSoundRange); 
            }
        }

        public float SightRange
        {
            get { return sightRange; }
            set
            {
                if (float.IsNaN(value))
                {
                    DebugConsole.ThrowError("Attempted to set the SightRange of an AITarget to NaN.\n" + Environment.StackTrace.CleanupStackTrace());
                    return;
                }
                sightRange = MathHelper.Clamp(value, MinSightRange, MaxSightRange); 
            }
        }

        private float sectorRad = MathHelper.TwoPi;
        public float SectorDegrees
        {
            get { return MathHelper.ToDegrees(sectorRad); }
            set { sectorRad = MathHelper.ToRadians(value); }
        }

        private Vector2 sectorDir;
        public Vector2 SectorDir
        {
            get { return sectorDir; }
            set
            {
                if (!MathUtils.IsValid(value))
                {
                    string errorMsg = "Invalid AITarget sector direction (" + value + ")\n" + Environment.StackTrace.CleanupStackTrace();
                    DebugConsole.ThrowError(errorMsg);
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.SectorDir:" + entity?.ToString(), GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    return;
                }
                sectorDir = value;
            }
        }

        public float SonarDisruption
        {
            get;
            set;
        }

        public string SonarLabel;
        public string SonarIconIdentifier;

        public bool Enabled => SoundRange > 0 || SightRange > 0;

        public float MinSoundRange, MinSightRange;
        public float MaxSoundRange = 100000, MaxSightRange = 100000;

        public TargetType Type { get; private set; }

        public enum TargetType
        {
            Any,
            HumanOnly,
            EnemyOnly
        }

        public Vector2 WorldPosition
        {
            get
            {
                if (entity == null || entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace.CleanupStackTrace());
                    return Vector2.Zero;
                }

                return entity.WorldPosition;
            }
        }

        public Vector2 SimPosition
        {
            get
            {
                if (entity == null || entity.Removed)
                {
#if DEBUG
                    DebugConsole.ThrowError("Attempted to access a removed AITarget\n" + Environment.StackTrace.CleanupStackTrace());
#endif
                    GameAnalyticsManager.AddErrorEventOnce("AITarget.WorldPosition:EntityRemoved",
                        GameAnalyticsSDK.Net.EGAErrorSeverity.Error,
                        "Attempted to access a removed AITarget\n" + Environment.StackTrace.CleanupStackTrace());
                    return Vector2.Zero;
                }

                return entity.SimPosition;
            }
        }

        public void Reset()
        {
            if (Static)
            {
                SightRange = MaxSightRange;
                SoundRange = MaxSoundRange;
            }
            else
            {
                // Non-static ai targets must be kept alive by a custom logic (e.g. item components)
                SightRange = StaticSight ? MaxSightRange : MinSightRange;
                SoundRange = StaticSound ? MaxSoundRange : MinSoundRange;
            }
        }

        public AITarget(Entity e, XElement element) : this(e)
        {
            SightRange = element.GetAttributeFloat("sightrange", 0.0f);
            SoundRange = element.GetAttributeFloat("soundrange", 0.0f);
            MinSightRange = element.GetAttributeFloat("minsightrange", 0f);
            MinSoundRange = element.GetAttributeFloat("minsoundrange", 0f);
            MaxSightRange = element.GetAttributeFloat("maxsightrange", SightRange);
            MaxSoundRange = element.GetAttributeFloat("maxsoundrange", SoundRange);
            FadeOutTime = element.GetAttributeFloat("fadeouttime", FadeOutTime);
            Static = element.GetAttributeBool("static", Static);
            StaticSight = element.GetAttributeBool("staticsight", StaticSight);
            StaticSound = element.GetAttributeBool("staticsound", StaticSound);
            if (Static)
            {
                StaticSound = true;
                StaticSight = true;
            }
            SonarDisruption     = element.GetAttributeFloat("sonardisruption", 0.0f);
            string label        = element.GetAttributeString("sonarlabel", "");
            SonarLabel          = TextManager.Get(label, returnNull: true) ?? label;
            SonarIconIdentifier = element.GetAttributeString("sonaricon", "");
            string typeString   = element.GetAttributeString("type", "Any");
            if (Enum.TryParse(typeString, out TargetType t))
            {
                Type = t;
            }
            Reset();
        }

        public AITarget(Entity e)
        {
            entity = e;
            List.Add(this);
        }

        public void Update(float deltaTime)
        {
            if (Enabled && !Static && FadeOutTime > 0)
            {
                // The aitarget goes silent/invisible if the components don't keep it active
                if (!StaticSight)
                {
                    DecreaseSightRange(deltaTime);
                }
                if (!StaticSound)
                {
                    DecreaseSoundRange(deltaTime);
                }
            }
        }

        public void IncreaseSoundRange(float deltaTime, float speed = 1)
        {
            SoundRange += speed * deltaTime * (MaxSoundRange / FadeOutTime);
        }

        public void IncreaseSightRange(float deltaTime, float speed = 1)
        {
            SightRange += speed * deltaTime * (MaxSightRange / FadeOutTime);
        }

        public void DecreaseSoundRange(float deltaTime, float speed = 1)
        {
            SoundRange -= speed * deltaTime * (MaxSoundRange / FadeOutTime);
        }

        public void DecreaseSightRange(float deltaTime, float speed = 1)
        {
            SightRange -= speed * deltaTime * (MaxSightRange / FadeOutTime);
        }

        public bool IsWithinSector(Vector2 worldPosition)
        {
            if (sectorRad >= MathHelper.TwoPi) return true;

            Vector2 diff = worldPosition - WorldPosition;
            return MathUtils.GetShortestAngle(MathUtils.VectorToAngle(diff), MathUtils.VectorToAngle(sectorDir)) <= sectorRad * 0.5f;
        }

        public void Remove()
        {
            List.Remove(this);
            entity = null;
        }
    }
}
