﻿using Microsoft.Xna.Framework;

namespace Barotrauma
{
	public class Skill
    {
        private float level;

        public string Identifier { get; }
        
        public float Level
        {
            get { return level; }
            set { level = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        private Sprite icon;
        public Sprite Icon
        {
            get
            {
                if (icon == null)
                {
                    icon = GetIcon();
                }
                return icon;
            }
        }

        internal SkillPrefab Prefab { get; private set; }

        public Skill(SkillPrefab prefab)
        {
            this.Prefab = prefab;
            Identifier = prefab.Identifier;
            level = Rand.Range(prefab.LevelRange.X, prefab.LevelRange.Y, Rand.RandSync.Server);
            icon = GetIcon();
        }

        public Skill(string identifier, float level)
        {
            Identifier = identifier;
            this.level = level;
            icon = GetIcon();
        }

        private Sprite GetIcon()
        {
            string jobId = null;
            switch (Identifier.ToLowerInvariant())
            {
                case "electrical":
                    jobId = "engineer";
                    break;
                case "helm":
                    jobId = "captain";
                    break;
                case "mechanical":
                    jobId = "mechanic";
                    break;
                case "medical":
                    jobId = "medicaldoctor";
                    break;
                case "weapons":
                    jobId = "securityofficer";
                    break;
            }
            return jobId != null && JobPrefab.Prefabs.ContainsKey(jobId) ? JobPrefab.Prefabs[jobId].IconSmall : null;
        }
    }
}
