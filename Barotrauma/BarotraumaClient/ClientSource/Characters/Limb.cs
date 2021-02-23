﻿using Barotrauma.Lights;
using Barotrauma.Particles;
using Barotrauma.SpriteDeformations;
using Barotrauma.Extensions;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using Barotrauma.IO;
using System.Linq;
using System.Xml.Linq;
using SpriteParams = Barotrauma.RagdollParams.SpriteParams;

namespace Barotrauma
{
    public partial class LimbJoint
    {
        public void UpdateDeformations(float deltaTime)
        {
            float diff = Math.Abs(UpperLimit - LowerLimit);
            float strength = MathHelper.Lerp(0, 1, MathUtils.InverseLerp(0, MathHelper.Pi, diff));
            float jointAngle = JointAngle * strength;

            JointBendDeformation limbADeformation = LimbA.ActiveDeformations.Find(d => d is JointBendDeformation) as JointBendDeformation;
            JointBendDeformation limbBDeformation = LimbB.ActiveDeformations.Find(d => d is JointBendDeformation) as JointBendDeformation;

            if (limbADeformation != null && limbBDeformation != null)
            {
                UpdateBend(LimbA, limbADeformation, this.LocalAnchorA, -jointAngle);
                UpdateBend(LimbB, limbBDeformation, this.LocalAnchorB, jointAngle);
            }
            
            void UpdateBend(Limb limb, JointBendDeformation deformation, Vector2 localAnchor, float angle)
            {
                deformation.Scale = limb.DeformSprite.Size;

                Vector2 displayAnchor = ConvertUnits.ToDisplayUnits(localAnchor);
                displayAnchor.Y = -displayAnchor.Y;
                Vector2 refPos = displayAnchor + limb.DeformSprite.Origin;

                refPos.X /= limb.DeformSprite.Size.X;
                refPos.Y /= limb.DeformSprite.Size.Y;

                if (Math.Abs(localAnchor.X) > Math.Abs(localAnchor.Y))
                {
                    if (localAnchor.X > 0.0f)
                    {
                        deformation.BendRightRefPos = refPos;
                        deformation.BendRight = angle;
                    }
                    else
                    {
                        deformation.BendLeftRefPos = refPos;
                        deformation.BendLeft = angle;
                    }
                }
                else
                {
                    if (localAnchor.Y > 0.0f)
                    {
                        deformation.BendUpRefPos = refPos;
                        deformation.BendUp = angle;
                    }
                    else
                    {
                        deformation.BendDownRefPos = refPos;
                        deformation.BendDown = angle;
                    }
                }
            }
        }

        public void Draw(SpriteBatch spriteBatch)
        {
            // TODO: move this into the character editor
            //var mouthPos = ragdoll.GetMouthPosition();
            //if (mouthPos != null)
            //{
            //    var pos = ConvertUnits.ToDisplayUnits(mouthPos.Value);
            //    pos.Y = -pos.Y;
            //    ShapeExtensions.DrawPoint(spriteBatch, pos, GUI.Style.Red, size: 5);
            //}
            
            // A debug visualisation on the bezier curve between limbs.
            /*var start = LimbA.WorldPosition;
            var end = LimbB.WorldPosition;
            var jointAPos = ConvertUnits.ToDisplayUnits(LocalAnchorA);
            var control = start + Vector2.Transform(jointAPos, Matrix.CreateRotationZ(LimbA.Rotation));
            start.Y = -start.Y;
            end.Y = -end.Y;
            control.Y = -control.Y;
            GUI.DrawRectangle(spriteBatch, start, Vector2.One * 5, Color.White, true);
            GUI.DrawRectangle(spriteBatch, end, Vector2.One * 5, Color.Black, true);
            GUI.DrawRectangle(spriteBatch, control, Vector2.One * 5, Color.Black, true);
            GUI.DrawLine(spriteBatch, start, end, Color.White);
            GUI.DrawLine(spriteBatch, start, control, Color.Black);
            GUI.DrawLine(spriteBatch, control, end, Color.Black);
            GUI.DrawBezierWithDots(spriteBatch, start, end, control, 1000, GUI.Style.Red);*/
        }
    }

    public partial class Limb
    {
        //minimum duration between hit/attack sounds
        public const float SoundInterval = 0.4f;
        public float LastAttackSoundTime, LastImpactSoundTime;

        private float wetTimer;
        private float dripParticleTimer;
        private float deadTimer;

        /// <summary>
        /// Note that different limbs can share the same deformations.
        /// Use ragdoll.SpriteDeformations for a collection that cannot have duplicates.
        /// </summary>
        private List<SpriteDeformation> Deformations { get; set; } = new List<SpriteDeformation>();
        private List<SpriteDeformation> NonConditionalDeformations { get; set; } = new List<SpriteDeformation>();
        private List<(ConditionalSprite, IEnumerable<SpriteDeformation>)> ConditionalDeformations { get; set; } = new List<(ConditionalSprite, IEnumerable<SpriteDeformation>)>();
        public List<SpriteDeformation> ActiveDeformations { get; set; } = new List<SpriteDeformation>();

        public Sprite Sprite { get; protected set; }

        protected DeformableSprite _deformSprite;

        public DeformableSprite DeformSprite
        {
            get
            {
                var conditionalSprite = ConditionalSprites.FirstOrDefault(c => c.Exclusive && c.IsActive && c.DeformableSprite != null);
                if (conditionalSprite != null)
                {
                    return conditionalSprite.DeformableSprite;
                }
                else
                {
                    return _deformSprite;
                }
            }
        }

        public List<DecorativeSprite> DecorativeSprites { get; private set; } = new List<DecorativeSprite>();

        public Sprite ActiveSprite
        {
            get
            {
                var conditionalSprite = ConditionalSprites.FirstOrDefault(c => c.Exclusive && c.IsActive && c.ActiveSprite != null);
                if (conditionalSprite != null)
                {
                    return conditionalSprite.ActiveSprite;
                }
                else
                {
                    return _deformSprite != null ? _deformSprite.Sprite : Sprite;
                }
            }
        }

        public WearableSprite HuskSprite { get; private set; }
        public WearableSprite HerpesSprite { get; private set; }

        public void LoadHuskSprite() => HuskSprite = GetWearableSprite(WearableType.Husk);
        public void LoadHerpesSprite() => HerpesSprite = GetWearableSprite(WearableType.Herpes);

        public float TextureScale => Params.Ragdoll.TextureScale;

        public Sprite DamagedSprite { get; private set; }

        public bool Hide
        {
            get => Params.Hide;
            set => Params.Hide = value;
        }

        public List<ConditionalSprite> ConditionalSprites { get; private set; } = new List<ConditionalSprite>();
        private Dictionary<DecorativeSprite, SpriteState> spriteAnimState = new Dictionary<DecorativeSprite, SpriteState>();
        private Dictionary<int, List<DecorativeSprite>> DecorativeSpriteGroups = new Dictionary<int, List<DecorativeSprite>>();

        public class SpriteState
        {
            public float RotationState;
            public float OffsetState;
            public Vector2 RandomOffsetMultiplier = new Vector2(Rand.Range(-1.0f, 1.0f), Rand.Range(-1.0f, 1.0f));
            public float RandomRotationFactor = Rand.Range(0.0f, 1.0f);
            public float RandomScaleFactor = Rand.Range(0.0f, 1.0f);
            public bool IsActive = true;
        }

        public Color InitialLightSourceColor
        {
            get;
            private set;
        }
        public float? InitialLightSpriteAlpha
        {
            get;
            private set;
        }

        public LightSource LightSource
        {
            get;
            private set;
        }

        private float damageOverlayStrength;
        public float DamageOverlayStrength
        {
            get { return damageOverlayStrength; }
            set { damageOverlayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        private float burnOverLayStrength;
        public float BurnOverlayStrength
        {
            get { return burnOverLayStrength; }
            set { burnOverLayStrength = MathHelper.Clamp(value, 0.0f, 100.0f); }
        }

        public string HitSoundTag => Params?.Sound?.Tag;

        private List<WearableSprite> wearableTypeHidingSprites = new List<WearableSprite>();
        private List<WearableType> wearableTypesToHide = new List<WearableType>();
        private bool enableHuskSprite;
        public bool EnableHuskSprite
        {
            get
            {
                return enableHuskSprite;
            }
            set
            {
                if (enableHuskSprite == value) { return; }
                enableHuskSprite = value;
                if (enableHuskSprite)
                {
                    if (HuskSprite == null)
                    {
                        LoadHuskSprite();
                    }
                }
                if (HuskSprite != null)
                {
                    if (enableHuskSprite)
                    {
                        List<WearableSprite> otherWearablesWithHusk = new List<WearableSprite>() { HuskSprite };
                        otherWearablesWithHusk.AddRange(OtherWearables);
                        OtherWearables = otherWearablesWithHusk;
                        UpdateWearableTypesToHide();
                    }
                    else
                    {
                        OtherWearables.Remove(HuskSprite);
                        UpdateWearableTypesToHide();
                    }
                }
            }
        }

        partial void InitProjSpecific(XElement element)
        {
            for (int i = 0; i < Params.decorativeSpriteParams.Count; i++)
            {
                var param = Params.decorativeSpriteParams[i];
                var decorativeSprite = new DecorativeSprite(param.Element, file: GetSpritePath(param.Element, param));
                DecorativeSprites.Add(decorativeSprite);
                int groupID = decorativeSprite.RandomGroupID;
                if (!DecorativeSpriteGroups.ContainsKey(groupID))
                {
                    DecorativeSpriteGroups.Add(groupID, new List<DecorativeSprite>());
                }
                DecorativeSpriteGroups[groupID].Add(decorativeSprite);
                spriteAnimState.Add(decorativeSprite, new SpriteState());
            }
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "sprite":
                        Sprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.normalSpriteParams));
                        break;
                    case "damagedsprite":
                        DamagedSprite = new Sprite(subElement, file: GetSpritePath(subElement, Params.damagedSpriteParams));
                        break;
                    case "conditionalsprite":
                        var conditionalSprite = new ConditionalSprite(subElement, GetConditionalTarget(), file: GetSpritePath(subElement, null));
                        ConditionalSprites.Add(conditionalSprite);
                        if (conditionalSprite.DeformableSprite != null)
                        {
                            var conditionalDeformations = CreateDeformations(subElement.GetChildElement("deformablesprite"));
                            Deformations.AddRange(conditionalDeformations);
                            ConditionalDeformations.Add((conditionalSprite, conditionalDeformations));
                        }
                        break;
                    case "deformablesprite":
                        _deformSprite = new DeformableSprite(subElement, filePath: GetSpritePath(subElement, Params.deformSpriteParams));
                        var deformations = CreateDeformations(subElement);
                        Deformations.AddRange(deformations);
                        NonConditionalDeformations.AddRange(deformations);
                        break;
                    case "lightsource":
                        LightSource = new LightSource(subElement, GetConditionalTarget())
                        {
                            ParentBody = body,
                            SpriteScale = Vector2.One * Scale * TextureScale
                        };
                        InitialLightSourceColor = LightSource.Color;
                        InitialLightSpriteAlpha = LightSource.OverrideLightSpriteAlpha;
                        break;
                }

                ISerializableEntity GetConditionalTarget()
                {
                    ISerializableEntity targetEntity;
                    string target = subElement.GetAttributeString("target", null);
                    if (string.Equals(target, "character", StringComparison.OrdinalIgnoreCase))
                    {
                        targetEntity = character;
                    }
                    else
                    {
                        targetEntity = this;
                    }
                    return targetEntity;
                }

                IEnumerable<SpriteDeformation> CreateDeformations(XElement e)
                {
                    List<SpriteDeformation> deformations = new List<SpriteDeformation>();
                    foreach (XElement animationElement in e.GetChildElements("spritedeformation"))
                    {
                        int sync = animationElement.GetAttributeInt("sync", -1);
                        SpriteDeformation deformation = null;
                        if (sync > -1)
                        {
                            // if the element is marked with the sync attribute, use a deformation of the same type with the same sync value, if there is one already.
                            string typeName = animationElement.GetAttributeString("type", "").ToLowerInvariant();
                            deformation = ragdoll.Limbs
                                .Where(l => l != null)
                                .SelectMany(l => l.Deformations)
                                .FirstOrDefault(d => d.TypeName == typeName && d.Sync == sync);
                        }
                        if (deformation == null)
                        {
                            deformation = SpriteDeformation.Load(animationElement, character.SpeciesName);
                            if (deformation != null)
                            {
                                ragdoll.SpriteDeformations.Add(deformation);
                            }
                        }
                        if (deformation != null)
                        {
                            deformations.Add(deformation);
                        }
                    }
                    return deformations;
                }
            }
            LightSource?.CheckConditionals();
        }

        private void RefreshDeformations()
        {
            if (_deformSprite == null) { return; }
            if (ConditionalSprites.None())
            {
                ActiveDeformations = Deformations;
            }
            else
            {
                ActiveDeformations.Clear();
                if (_deformSprite == DeformSprite)
                {
                    ActiveDeformations.AddRange(NonConditionalDeformations);
                }
                foreach (var conditionalDeformation in ConditionalDeformations)
                {
                    if (conditionalDeformation.Item1.IsActive)
                    {
                        ActiveDeformations.AddRange(conditionalDeformation.Item2);
                    }
                }
            }
        }

        public void RecreateSprites()
        {
            if (Sprite != null)
            {
                Sprite.Remove();
                var source = Sprite.SourceElement;
                Sprite = new Sprite(source, file: GetSpritePath(source, Params.normalSpriteParams));
            }
            if (_deformSprite != null)
            {
                _deformSprite.Remove();
                var source = _deformSprite.Sprite.SourceElement;
                _deformSprite = new DeformableSprite(source, filePath: GetSpritePath(source, Params.deformSpriteParams));
            }
            if (DamagedSprite != null)
            {
                DamagedSprite.Remove();
                var source = DamagedSprite.SourceElement;
                DamagedSprite = new Sprite(source, file: GetSpritePath(source, Params.damagedSpriteParams));
            }
            for (int i = 0; i < ConditionalSprites.Count; i++)
            {
                var conditionalSprite = ConditionalSprites[i];
                var source = conditionalSprite.ActiveSprite.SourceElement;
                conditionalSprite.Remove();
                ConditionalSprites[i] = new ConditionalSprite(source, character, file: GetSpritePath(source, null));
            }
            for (int i = 0; i < DecorativeSprites.Count; i++)
            {
                var decorativeSprite = DecorativeSprites[i];
                decorativeSprite.Remove();
                var source = decorativeSprite.Sprite.SourceElement;
                DecorativeSprites[i] = new DecorativeSprite(source, file: GetSpritePath(source, Params.decorativeSpriteParams[i]));
            }
        }

        private void CalculateHeadPosition(Sprite sprite)
        {
            if (type != LimbType.Head) { return; }
            character.Info?.CalculateHeadPosition(sprite);
        }

        private string _texturePath;
        private string GetSpritePath(XElement element, SpriteParams spriteParams)
        {
            if (_texturePath == null)
            {
                if (spriteParams != null)
                {
                    string texturePath = character.Params.VariantFile?.Root?.GetAttributeString("texture", null) ?? spriteParams.GetTexturePath();
                    _texturePath = GetSpritePath(texturePath);
                }
                else
                {
                    string texturePath = element.GetAttributeString("texture", null);
                    texturePath = string.IsNullOrWhiteSpace(texturePath) ? ragdoll.RagdollParams.Texture : texturePath;
                    _texturePath = GetSpritePath(texturePath);
                }
            }
            return _texturePath;
        }

        /// <summary>
        /// Get the full path of a limb sprite, taking into account tags, gender and head id
        /// </summary>
        private string GetSpritePath(string texturePath)
        {
            string spritePath = texturePath;
            string spritePathWithTags = spritePath;
            if (character.Info != null && character.IsHumanoid)
            {
                spritePath = spritePath.Replace("[GENDER]", (character.Info.Gender == Gender.Female) ? "female" : "male");
                spritePath = spritePath.Replace("[RACE]", character.Info.Race.ToString().ToLowerInvariant());
                spritePath = spritePath.Replace("[HEADID]", character.Info.HeadSpriteId.ToString());

                if (character.Info.HeadSprite != null && character.Info.SpriteTags.Any())
                {
                    string tags = "";
                    character.Info.SpriteTags.ForEach(tag => tags += "[" + tag + "]");

                    spritePathWithTags = Path.Combine(
                        Path.GetDirectoryName(spritePath),
                        Path.GetFileNameWithoutExtension(spritePath) + tags + Path.GetExtension(spritePath));
                }
            }
            return File.Exists(spritePathWithTags) ? spritePathWithTags : spritePath;
        }

        partial void LoadParamsProjSpecific()
        {
            bool isFlipped = dir == Direction.Left;
            Sprite?.LoadParams(Params.normalSpriteParams, isFlipped);
            DamagedSprite?.LoadParams(Params.damagedSpriteParams, isFlipped);
            _deformSprite?.Sprite.LoadParams(Params.deformSpriteParams, isFlipped);
            for (int i = 0; i < DecorativeSprites.Count; i++)
            {
                DecorativeSprites[i].Sprite?.LoadParams(Params.decorativeSpriteParams[i], isFlipped);
            }
        }

        partial void AddDamageProjSpecific(bool playSound, AttackResult result)
        {
            float bleedingDamage = 0;
            if (character.CharacterHealth.DoesBleed)
            {
                foreach (var affliction in result.Afflictions)
                {
                    if (affliction is AfflictionBleeding)
                    {
                        bleedingDamage += affliction.GetVitalityDecrease(null);
                    }
                }
            }
            float damage = 0;
            foreach (var affliction in result.Afflictions)
            {
                if (affliction.Prefab.AfflictionType == "damage")
                {
                    damage += affliction.GetVitalityDecrease(null);
                }
            }
            float damageMultiplier = 1;
            float bleedingDamageMultiplier = 1;
            foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
            {
                if (damageModifier.MatchesAfflictionType("damage"))
                {
                    damageMultiplier *= damageModifier.DamageMultiplier;
                }
                else if (damageModifier.MatchesAfflictionType("bleeding"))
                {
                    bleedingDamageMultiplier *= damageModifier.DamageMultiplier;
                }
            }
            if (playSound)
            {
                string damageSoundType = (bleedingDamage > damage) ? "LimbSlash" : "LimbBlunt";
                foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
                {
                    if (!string.IsNullOrWhiteSpace(damageModifier.DamageSound))
                    {
                        damageSoundType = damageModifier.DamageSound;
                        break;
                    }
                }
                SoundPlayer.PlayDamageSound(damageSoundType, Math.Max(damage, bleedingDamage), WorldPosition);
            }

            // spawn damage particles
            float damageParticleAmount = damage < 1 ? 0 : Math.Min(damage / 5, 1.0f) * damageMultiplier;
            if (damageParticleAmount > 0.001f)
            {
                foreach (ParticleEmitter emitter in character.DamageEmitters)
                {
                    if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) { continue; }
                    if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) { continue; }
                    ParticlePrefab overrideParticle = null;
                    foreach (DamageModifier damageModifier in result.AppliedDamageModifiers)
                    {
                        if (damageModifier.DamageMultiplier > 0 && !string.IsNullOrWhiteSpace(damageModifier.DamageParticle))
                        {
                            overrideParticle = GameMain.ParticleManager?.FindPrefab(damageModifier.DamageParticle);
                            break;
                        }
                    }
                    emitter.Emit(1.0f, WorldPosition, character.CurrentHull, amountMultiplier: damageParticleAmount, overrideParticle: overrideParticle);
                }
            }

            if (bleedingDamage > 0)
            {
                float bloodParticleAmount = Math.Min(bleedingDamage / 5, 1.0f) * bleedingDamageMultiplier;
                float bloodParticleSize = MathHelper.Clamp(bleedingDamage / 5, 0.1f, 1.0f);

                foreach (ParticleEmitter emitter in character.BloodEmitters)
                {
                    if (inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Air) { continue; }
                    if (!inWater && emitter.Prefab.ParticlePrefab.DrawTarget == ParticlePrefab.DrawTargetType.Water) { continue; }
                    emitter.Emit(1.0f, WorldPosition, character.CurrentHull, sizeMultiplier: bloodParticleSize, amountMultiplier: bloodParticleAmount);
                }
            }   
        }

        partial void UpdateProjSpecific(float deltaTime)
        {
            if (!body.Enabled) { return; }

            if (!IsDead)
            {
                DamageOverlayStrength -= deltaTime;
                BurnOverlayStrength -= deltaTime;
            }
            else
            {
                var spriteParams = Params.GetSprite();
                if (spriteParams != null && spriteParams.DeadColorTime > 0 && deadTimer < spriteParams.DeadColorTime)
                {
                    deadTimer += deltaTime;
                }
            }

            if (inWater)
            {
                wetTimer = 1.0f;
            }
            else
            {
                wetTimer -= deltaTime * 0.1f;
                if (wetTimer > 0.0f)
                {
                    dripParticleTimer += wetTimer * deltaTime * Mass * (wetTimer > 0.9f ? 50.0f : 5.0f);
                    if (dripParticleTimer > 1.0f)
                    {
                        float dropRadius = body.BodyShape == PhysicsBody.Shape.Rectangle ? Math.Min(body.width, body.height) : body.radius;
                        GameMain.ParticleManager.CreateParticle(
                            "waterdrop", 
                            WorldPosition + Rand.Vector(Rand.Range(0.0f, ConvertUnits.ToDisplayUnits(dropRadius))), 
                            ConvertUnits.ToDisplayUnits(body.LinearVelocity), 
                            0, character.CurrentHull);
                        dripParticleTimer = 0.0f;
                    }
                }
            }

            foreach (var conditionalSprite in ConditionalSprites)
            {
                conditionalSprite.CheckConditionals();
            }

            if (LightSource != null)
            {
                LightSource.ParentSub = body.Submarine;
                LightSource.Rotation = (dir == Direction.Right) ? body.Rotation : body.Rotation - MathHelper.Pi;
                if (LightSource.LightSprite != null)
                {
                    LightSource.LightSprite.Depth = ActiveSprite.Depth;
                }
                if (LightSource.DeformableLightSprite != null)
                {
                    LightSource.DeformableLightSprite.Sprite.Depth = ActiveSprite.Depth;
                }
                LightSource.CheckConditionals();
            }

            UpdateSpriteStates(deltaTime);
            RefreshDeformations();
        }

        public void Draw(SpriteBatch spriteBatch, Camera cam, Color? overrideColor = null)
        {
            float brightness = 1.0f - (burnOverLayStrength / 100.0f) * 0.5f;
            var spriteParams = Params.GetSprite();
            if (spriteParams == null) { return; }

            Color color = new Color(spriteParams.Color.R / 255f * brightness, spriteParams.Color.G / 255f * brightness, spriteParams.Color.B / 255f * brightness, spriteParams.Color.A / 255f);
            if (deadTimer > 0)
            {
                color = Color.Lerp(color, spriteParams.DeadColor, MathUtils.InverseLerp(0, spriteParams.DeadColorTime, deadTimer));
            }

            color = overrideColor ?? color;

            if (isSevered)
            {
                if (severedFadeOutTimer > SeveredFadeOutTime)
                {
                    if (LightSource != null) { LightSource.Enabled = false; }
                    return;
                }
                else if (severedFadeOutTimer > SeveredFadeOutTime - 1.0f)
                {
                    color *= SeveredFadeOutTime - severedFadeOutTimer;
                }
            }
            
            body.Dir = Dir;

            float herpesStrength = character.CharacterHealth.GetAfflictionStrength("spaceherpes");

            bool hideLimb = Hide || 
                OtherWearables.Any(w => w.HideLimb) || 
                wearingItems.Any(w => w != null && w.HideLimb);

            var activeSprite = ActiveSprite;
            if (type == LimbType.Head)
            {
                CalculateHeadPosition(activeSprite);
            }
            
            body.UpdateDrawPosition();

            if (!hideLimb)
            {
                var deformSprite = DeformSprite;
                if (deformSprite != null)
                {
                    if (ActiveDeformations.Any())
                    {
                        var deformation = SpriteDeformation.GetDeformation(ActiveDeformations, deformSprite.Size);
                        deformSprite.Deform(deformation);
                        if (LightSource != null && LightSource.DeformableLightSprite != null)
                        {
                            deformation = SpriteDeformation.GetDeformation(ActiveDeformations, deformSprite.Size, dir == Direction.Left);
                            LightSource.DeformableLightSprite.Deform(deformation);
                        }
                    }
                    else
                    {
                        deformSprite.Reset();
                    }
                    body.Draw(deformSprite, cam, Vector2.One * Scale * TextureScale, color, Params.MirrorHorizontally);
                }
                else
                {
                    body.Draw(spriteBatch, activeSprite, color, null, Scale * TextureScale, Params.MirrorHorizontally, Params.MirrorVertically);
                }
                // Handle non-exlusive, i.e. additional conditional sprites
                foreach (var conditionalSprite in ConditionalSprites)
                {
                    // Exclusive conditional sprites are handled in the Properties
                    if (conditionalSprite.Exclusive) { continue; }
                    if (!conditionalSprite.IsActive) { continue; }
                    if (conditionalSprite.DeformableSprite != null)
                    {
                        var defSprite = conditionalSprite.DeformableSprite;
                        if (ActiveDeformations.Any())
                        {
                            var deformation = SpriteDeformation.GetDeformation(ActiveDeformations, defSprite.Size);
                            defSprite.Deform(deformation);
                        }
                        else
                        {
                            defSprite.Reset();
                        }
                        body.Draw(defSprite, cam, Vector2.One * Scale * TextureScale, color, Params.MirrorHorizontally);
                    }
                    else
                    {
                        body.Draw(spriteBatch, conditionalSprite.Sprite, color, null, Scale * TextureScale, Params.MirrorHorizontally, Params.MirrorVertically);
                    }
                }
            }
            SpriteEffects spriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipHorizontally;
            if (LightSource != null)
            {
                LightSource.LightSpriteEffect = (dir == Direction.Right) ? SpriteEffects.None : SpriteEffects.FlipVertically;
            }
            if (damageOverlayStrength > 0.0f && DamagedSprite != null && !hideLimb)
            {
                DamagedSprite.Draw(spriteBatch,
                    new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                    color * Math.Min(damageOverlayStrength, 1.0f), activeSprite.Origin,
                    -body.DrawRotation,
                    Scale, spriteEffect, activeSprite.Depth - 0.0000015f);
            }
            foreach (var decorativeSprite in DecorativeSprites)
            {
                if (!spriteAnimState[decorativeSprite].IsActive) { continue; }
                Color c = new Color(decorativeSprite.Color.R / 255f * brightness, decorativeSprite.Color.G / 255f * brightness, decorativeSprite.Color.B / 255f * brightness, decorativeSprite.Color.A / 255f);
                if (deadTimer > 0)
                {
                    c = Color.Lerp(c, spriteParams.DeadColor, MathUtils.InverseLerp(0, Params.GetSprite().DeadColorTime, deadTimer));
                }
                c = overrideColor ?? c;
                float rotation = decorativeSprite.GetRotation(ref spriteAnimState[decorativeSprite].RotationState, spriteAnimState[decorativeSprite].RandomRotationFactor);
                Vector2 offset = decorativeSprite.GetOffset(ref spriteAnimState[decorativeSprite].OffsetState, spriteAnimState[decorativeSprite].RandomOffsetMultiplier) * Scale;
                var ca = (float)Math.Cos(-body.Rotation);
                var sa = (float)Math.Sin(-body.Rotation);
                Vector2 transformedOffset = new Vector2(ca * offset.X + sa * offset.Y, -sa * offset.X + ca * offset.Y);
                decorativeSprite.Sprite.Draw(spriteBatch, new Vector2(body.DrawPosition.X + transformedOffset.X, -(body.DrawPosition.Y + transformedOffset.Y)), c,
                    -body.Rotation + rotation, decorativeSprite.GetScale(spriteAnimState[decorativeSprite].RandomScaleFactor) * Scale, spriteEffect,
                    depth: decorativeSprite.Sprite.Depth);
            }
            float depthStep = 0.000001f;
            float step = depthStep;
            WearableSprite onlyDrawable = wearingItems.Find(w => w.HideOtherWearables);
            if (Params.MirrorHorizontally)
            {
                spriteEffect = spriteEffect == SpriteEffects.None ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
            }
            if (Params.MirrorVertically)
            {
                spriteEffect |= SpriteEffects.FlipVertically;
            }
            if (onlyDrawable == null)
            {
                if (HerpesSprite != null && !wearableTypesToHide.Contains(WearableType.Herpes))
                {
                    DrawWearable(HerpesSprite, depthStep, spriteBatch, color * Math.Min(herpesStrength / 10.0f, 1.0f), spriteEffect);
                    depthStep += step;
                }
                foreach (WearableSprite wearable in OtherWearables)
                {
                    if (wearableTypesToHide.Contains(wearable.Type)) { continue; }
                    DrawWearable(wearable, depthStep, spriteBatch, color, spriteEffect);
                    //if there are multiple sprites on this limb, make the successive ones be drawn in front
                    depthStep += step;
                }
            }
            foreach (WearableSprite wearable in WearingItems)
            {
                if (onlyDrawable != null && onlyDrawable != wearable) continue;
                DrawWearable(wearable, depthStep, spriteBatch, color, spriteEffect);
                //if there are multiple sprites on this limb, make the successive ones be drawn in front
                depthStep += step;
            }

            if (GameMain.DebugDraw)
            {
                if (pullJoint != null)
                {
                    Vector2 pos = ConvertUnits.ToDisplayUnits(pullJoint.WorldAnchorB);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)pos.X, (int)-pos.Y, 5, 5), GUI.Style.Red, true);
                }
                var bodyDrawPos = body.DrawPosition;
                bodyDrawPos.Y = -bodyDrawPos.Y;
                if (IsStuck)
                {
                    Vector2 from = ConvertUnits.ToDisplayUnits(attachJoint.WorldAnchorA);
                    from.Y = -from.Y;
                    Vector2 to = ConvertUnits.ToDisplayUnits(attachJoint.WorldAnchorB);
                    to.Y = -to.Y;
                    var localFront = body.GetLocalFront(Params.GetSpriteOrientation());
                    var front = ConvertUnits.ToDisplayUnits(body.FarseerBody.GetWorldPoint(localFront));
                    front.Y = -front.Y;
                    GUI.DrawLine(spriteBatch, bodyDrawPos, front, Color.Yellow, width: 2);
                    GUI.DrawLine(spriteBatch, from, to, GUI.Style.Red, width: 1);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 12, 12), Color.White, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)from.X, (int)from.Y, 10, 10), Color.Blue, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)to.X, (int)to.Y, 10, 10), GUI.Style.Red, true);
                    GUI.DrawRectangle(spriteBatch, new Rectangle((int)front.X, (int)front.Y, 10, 10), Color.Yellow, true);

                    //Vector2 mainLimbFront = ConvertUnits.ToDisplayUnits(ragdoll.MainLimb.body.FarseerBody.GetWorldPoint(ragdoll.MainLimb.body.GetFrontLocal(MathHelper.ToRadians(limbParams.Orientation))));
                    //mainLimbFront.Y = -mainLimbFront.Y;
                    //var mainLimbDrawPos = ragdoll.MainLimb.body.DrawPosition;
                    //mainLimbDrawPos.Y = -mainLimbDrawPos.Y;
                    //GUI.DrawLine(spriteBatch, mainLimbDrawPos, mainLimbFront, Color.White, width: 5);
                    //GUI.DrawRectangle(spriteBatch, new Rectangle((int)mainLimbFront.X, (int)mainLimbFront.Y, 10, 10), Color.Yellow, true);
                }
                //DrawDamageModifiers(spriteBatch, cam, bodyDrawPos, isScreenSpace: false);
            }
        }

        public void UpdateWearableTypesToHide()
        {
            wearableTypeHidingSprites.Clear();
            if (WearingItems != null && WearingItems.Count > 0)
            {
                wearableTypeHidingSprites.AddRange(
                    WearingItems.FindAll(w => w.HideWearablesOfType != null && w.HideWearablesOfType.Count > 0));
            }
            if (OtherWearables != null && OtherWearables.Count > 0)
            {
                wearableTypeHidingSprites.AddRange(
                    OtherWearables.FindAll(w => w.HideWearablesOfType != null && w.HideWearablesOfType.Count > 0));
            }

            wearableTypesToHide.Clear();
            if (wearableTypeHidingSprites.Count > 0)
            {
                foreach (WearableSprite sprite in wearableTypeHidingSprites)
                {
                    foreach (WearableType type in sprite.HideWearablesOfType)
                    {
                        if (!wearableTypesToHide.Contains(type))
                        {
                            wearableTypesToHide.Add(type);
                        }
                    }
                }
            }
        }

        private void UpdateSpriteStates(float deltaTime)
        {
            foreach (int spriteGroup in DecorativeSpriteGroups.Keys)
            {
                for (int i = 0; i < DecorativeSpriteGroups[spriteGroup].Count; i++)
                {
                    var decorativeSprite = DecorativeSpriteGroups[spriteGroup][i];
                    if (decorativeSprite == null) { continue; }
                    if (spriteGroup > 0)
                    {
                        // TODO
                        //int activeSpriteIndex = ID % DecorativeSpriteGroups[spriteGroup].Count;
                        //if (i != activeSpriteIndex)
                        //{
                        //    spriteAnimState[decorativeSprite].IsActive = false;
                        //    continue;
                        //}
                    }

                    //check if the sprite is active (whether it should be drawn or not)
                    var spriteState = spriteAnimState[decorativeSprite];
                    spriteState.IsActive = true;
                    foreach (PropertyConditional conditional in decorativeSprite.IsActiveConditionals)
                    {
                        if (!conditional.Matches(this))
                        {
                            spriteState.IsActive = false;
                            break;
                        }
                    }
                    if (!spriteState.IsActive) { continue; }

                    //check if the sprite should be animated
                    bool animate = true;
                    foreach (PropertyConditional conditional in decorativeSprite.AnimationConditionals)
                    {
                        if (!conditional.Matches(this)) { animate = false; break; }
                    }
                    if (!animate) { continue; }
                    spriteState.OffsetState += deltaTime;
                    spriteState.RotationState += deltaTime;
                }
            }
        }

        public void DrawDamageModifiers(SpriteBatch spriteBatch, Camera cam, Vector2 startPos, bool isScreenSpace)
        {
            foreach (var modifier in DamageModifiers)
            {
                //Vector2 up = VectorExtensions.Backward(-body.TransformedRotation + Params.GetSpriteOrientation() * Dir);
                //int width = 4;
                //if (!isScreenSpace)
                //{
                //    width = (int)Math.Round(width / cam.Zoom);
                //}
                //GUI.DrawLine(spriteBatch, startPos, startPos + Vector2.Normalize(up) * size, GUI.Style.Red, width: width);
                Color color = modifier.DamageMultiplier > 1 ? GUI.Style.Red : GUI.Style.Green;
                float size = ConvertUnits.ToDisplayUnits(body.GetSize().Length() / 2);
                if (isScreenSpace)
                {
                    size *= cam.Zoom;
                }
                int thickness = 2;
                if (!isScreenSpace)
                {
                    thickness = (int)Math.Round(thickness / cam.Zoom);
                }
                float bodyRotation = -body.Rotation;
                float constantOffset = -MathHelper.PiOver2;
                Vector2 armorSector = modifier.ArmorSectorInRadians;
                float armorSectorSize = Math.Abs(armorSector.X - armorSector.Y);
                float radians = armorSectorSize * Dir;
                float armorSectorOffset = armorSector.X * Dir;
                float finalOffset = bodyRotation + constantOffset + armorSectorOffset;
                ShapeExtensions.DrawSector(spriteBatch, startPos, size, radians, 40, color, finalOffset, thickness);
            }
        }

        private void DrawWearable(WearableSprite wearable, float depthStep, SpriteBatch spriteBatch, Color color, SpriteEffects spriteEffect)
        {
            var sprite = ActiveSprite;
            if (wearable.InheritSourceRect)
            {
                if (wearable.SheetIndex.HasValue)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, wearable.SheetIndex.Value), sprite.SourceRect.Size);
                }
                else if (type == LimbType.Head && character.Info != null && character.Info.Head.SheetIndex.HasValue)
                {
                    wearable.Sprite.SourceRect = new Rectangle(CharacterInfo.CalculateOffset(sprite, character.Info.Head.SheetIndex.Value.ToPoint()), sprite.SourceRect.Size);
                }
                else
                {
                    wearable.Sprite.SourceRect = sprite.SourceRect;
                }
            }

            Vector2 origin = wearable.Sprite.Origin;
            if (wearable.InheritOrigin)
            {
                origin = sprite.Origin;
                wearable.Sprite.Origin = origin;
            }
            else
            {
                origin = wearable.Sprite.Origin;
                // If the wearable inherits the origin, flipping is already handled.
                if (body.Dir == -1.0f)
                {
                    origin.X = wearable.Sprite.SourceRect.Width - origin.X;
                }
            }

            float depth = wearable.Sprite.Depth;

            if (wearable.InheritLimbDepth)
            {
                depth = sprite.Depth - depthStep;
                Limb depthLimb = (wearable.DepthLimb == LimbType.None) ? this : character.AnimController.GetLimb(wearable.DepthLimb);
                if (depthLimb != null)
                {
                    depth = depthLimb.ActiveSprite.Depth - depthStep;
                }
            }
            var wearableItemComponent = wearable.WearableComponent;
            Color wearableColor = Color.White;
            if (wearableItemComponent != null)
            {
                // Draw outer cloths on top of inner cloths.
                if (wearableItemComponent.AllowedSlots.Contains(InvSlotType.OuterClothes))
                {
                    depth -= depthStep;
                }
                if (wearableItemComponent.AllowedSlots.Contains(InvSlotType.Bag))
                {
                    depth -= depthStep * 2;
                }
                wearableColor = wearableItemComponent.Item.GetSpriteColor();
            }
            float textureScale = wearable.InheritTextureScale ? TextureScale : 1;

            wearable.Sprite.Draw(spriteBatch,
                new Vector2(body.DrawPosition.X, -body.DrawPosition.Y),
                new Color((color.R * wearableColor.R) / (255.0f * 255.0f), (color.G * wearableColor.G) / (255.0f * 255.0f), (color.B * wearableColor.B) / (255.0f * 255.0f)) * ((color.A * wearableColor.A) / (255.0f * 255.0f)),
                origin, -body.DrawRotation,
                Scale * textureScale, spriteEffect, depth);
        }

        private WearableSprite GetWearableSprite(WearableType type, bool random = false)
        {
            var info = character.Info;
            if (info == null) { return null; }
            XElement element;
            if (random)
            {
                element = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables, info.Gender, info.Race), type, info.Head.HeadSpriteId)?.GetRandom(Rand.RandSync.ClientOnly);
            }
            else
            {
                element = info.FilterByTypeAndHeadID(info.FilterElementsByGenderAndRace(info.Wearables, info.Gender, info.Race), type, info.Head.HeadSpriteId)?.FirstOrDefault();
            }
            if (element != null)
            {
                return new WearableSprite(element.Element("sprite"), type);
            }
            return null;
        }

        partial void RemoveProjSpecific()
        {
            Sprite?.Remove();
            Sprite = null;            

            DamagedSprite?.Remove();
            DamagedSprite = null;            

            _deformSprite?.Sprite?.Remove();
            _deformSprite = null;

            DecorativeSprites.ForEach(s => s.Remove());
            ConditionalSprites.Clear();

            ConditionalSprites.ForEach(s => s.Remove());
            ConditionalSprites.Clear();

            LightSource?.Remove();
            LightSource = null;

            OtherWearables?.ForEach(w => w.Sprite.Remove());
            OtherWearables = null;

            HuskSprite?.Sprite.Remove();
            HuskSprite = null;

            HerpesSprite?.Sprite.Remove();
            HerpesSprite = null;
        }
    }
}
