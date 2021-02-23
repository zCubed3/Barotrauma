﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Collision;
using FarseerPhysics.Dynamics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public partial class RangedWeapon : ItemComponent
    {
        private float reload, reloadTimer;

        private Vector2 barrelPos;

        [Serialize("0.0,0.0", false, description: "The position of the barrel as an offset from the item's center (in pixels). Determines where the projectiles spawn.")]
        public string BarrelPos
        {
            get { return XMLExtensions.Vector2ToString(ConvertUnits.ToDisplayUnits(barrelPos)); }
            set { barrelPos = ConvertUnits.ToSimUnits(XMLExtensions.ParseVector2(value)); }
        }

        [Serialize(1.0f, false, description: "How long the user has to wait before they can fire the weapon again (in seconds).")]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(value, 0.0f); }
        }

        [Serialize(1, false, description: "How projectiles the weapon launches when fired once.")]
        public int ProjectileCount
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles when used by a character with sufficient skills to use the weapon (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the firing angle of the projectiles when used by a character with insufficient skills to use the weapon (in degrees).")]
        public float UnskilledSpread
        {
            get;
            set;
        }

        public Vector2 TransformedBarrelPos
        {
            get
            {
                Matrix bodyTransform = Matrix.CreateRotationZ(item.body.Rotation);
                Vector2 flippedPos = barrelPos;
                if (item.body.Dir < 0.0f) flippedPos.X = -flippedPos.X;
                return Vector2.Transform(flippedPos, bodyTransform);
            }
        }
                
        public RangedWeapon(Item item, XElement element)
            : base(item, element)
        {
            item.IsShootable = true;
            // TODO: should define this in xml if we have ranged weapons that don't require aim to use
            item.RequireAimToUse = true;
            InitProjSpecific(element);
        }

        partial void InitProjSpecific(XElement element);

        public override void Equip(Character character)
        {
            reloadTimer = Math.Min(reload, 1.0f);
            IsActive = true;
        }

        public override void Update(float deltaTime, Camera cam)
        {
            reloadTimer -= deltaTime;

            if (reloadTimer < 0.0f)
            {
                reloadTimer = 0.0f;
                IsActive = false;
            }
        }

        private float GetSpread(Character user)
        {
            float degreeOfFailure = 1.0f - DegreeOfSuccess(user);
            degreeOfFailure *= degreeOfFailure;
            return MathHelper.ToRadians(MathHelper.Lerp(Spread, UnskilledSpread, degreeOfFailure));
        }

        private readonly List<Body> limbBodies = new List<Body>();
        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || character.Removed) { return false; }
            if ((item.RequireAimToUse && !character.IsKeyDown(InputType.Aim)) || reloadTimer > 0.0f) { return false; }

            IsActive = true;
            reloadTimer = reload;

            if (item.AiTarget != null)
            {
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
            }

            limbBodies.Clear();
            foreach (Limb l in character.AnimController.Limbs)
            {
                if (l.IsSevered) { continue; }
                limbBodies.Add(l.body.FarseerBody);
            }

            float degreeOfFailure = 1.0f - DegreeOfSuccess(character);
            degreeOfFailure *= degreeOfFailure;
            if (degreeOfFailure > Rand.Range(0.0f, 1.0f))
            {
                ApplyStatusEffects(ActionType.OnFailure, 1.0f, character);
            }

            for (int i = 0; i < ProjectileCount; i++)
            {
                Projectile projectile = FindProjectile(triggerOnUseOnContainers: true);
                if (projectile != null)
                {
                    Vector2 barrelPos = TransformedBarrelPos + item.body.SimPosition;
                    float rotation = (Item.body.Dir == 1.0f) ? Item.body.Rotation : Item.body.Rotation - MathHelper.Pi;
                    float spread = GetSpread(character) * Rand.Range(-0.5f, 0.5f);
                    projectile.Shoot(character, character.AnimController.AimSourceSimPos, barrelPos, rotation + spread, ignoredBodies: limbBodies.ToList(), createNetworkEvent: false);
                    projectile.Item.GetComponent<Rope>()?.Attach(Item, projectile.Item);
                    if (i == 0)
                    {
                        Item.body.ApplyLinearImpulse(new Vector2((float)Math.Cos(projectile.Item.body.Rotation), (float)Math.Sin(projectile.Item.body.Rotation)) * Item.body.Mass * -50.0f, maxVelocity: NetConfig.MaxPhysicsBodyVelocity);
                    }
                    projectile.Item.body.ApplyTorque(projectile.Item.body.Mass * degreeOfFailure * Rand.Range(-10.0f, 10.0f));
                    Item.RemoveContained(projectile.Item);
                }
            }

            LaunchProjSpecific();

            return true;
        }

        public Projectile FindProjectile(bool triggerOnUseOnContainers = false)
        {
            var containedItems = item.OwnInventory?.AllItemsMod;
            if (containedItems == null) { return null; }

            foreach (Item item in containedItems)
            {
                if (item == null) { continue; }
                Projectile projectile = item.GetComponent<Projectile>();
                if (projectile != null) { return projectile; }
            }

            //projectile not found, see if one of the contained items contains projectiles
            foreach (Item it in containedItems)
            {
                if (it == null) { continue; }
                var containedSubItems = it.OwnInventory?.AllItemsMod;
                if (containedSubItems == null) { continue; }
                foreach (Item subItem in containedSubItems)
                {
                    if (subItem == null) { continue; }
                    Projectile projectile = subItem.GetComponent<Projectile>();
                    //apply OnUse statuseffects to the container in case it has to react to it somehow
                    //(play a sound, spawn more projectiles, reduce condition...)
                    if (triggerOnUseOnContainers && subItem.Condition > 0.0f)
                    {
                        subItem.GetComponent<ItemContainer>()?.Item.ApplyStatusEffects(ActionType.OnUse, 1.0f);
                    }
                    if (projectile != null) { return projectile; }
                }
            }
            
            return null;
        }

        partial void LaunchProjSpecific();
    }
}
