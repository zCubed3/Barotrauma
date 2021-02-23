﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma.Items.Components
{
	public class MeleeWeapon : Holdable
    {
        private float hitPos;

        private bool hitting;

        private float range;
        private float reload;

        private float reloadTimer;

        public Attack Attack { get; private set; }

        private readonly HashSet<Entity> hitTargets = new HashSet<Entity>();

        private readonly Queue<Fixture> impactQueue = new Queue<Fixture>();

        public Character User { get; private set; }

        [Serialize(0.0f, false, description: "An estimation of how close the item has to be to the target for it to hit. Used by AI characters to determine when they're close enough to hit a target.")]
        public float Range
        {
            get { return ConvertUnits.ToDisplayUnits(range); }
            set { range = ConvertUnits.ToSimUnits(value); }
        }

        [Serialize(0.5f, false, description: "How long the user has to wait before they can hit with the weapon again (in seconds).")]
        public float Reload
        {
            get { return reload; }
            set { reload = Math.Max(0.0f, value); }
        }

        [Serialize(false, false, description: "Can the weapon hit multiple targets per swing.")]
        public bool AllowHitMultiple
        {
            get;
            set;
        }

        /// <summary>
        /// Defines items that boost the weapon functionality, like battery cell for stun batons.
        /// </summary>
        public readonly string[] PreferredContainedItems;

        public MeleeWeapon(Item item, XElement element)
            : base(item, element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("attack", StringComparison.OrdinalIgnoreCase)) { continue; }
                Attack = new Attack(subElement, item.Name + ", MeleeWeapon");
            }
            item.IsShootable = true;
            // TODO: should define this in xml if we have melee weapons that don't require aim to use
            item.RequireAimToUse = true;
            PreferredContainedItems = element.GetAttributeStringArray("preferredcontaineditems", new string[0], convertToLowerInvariant: true);
        }

        public override void Equip(Character character)
        {
            base.Equip(character);
            reloadTimer = Math.Min(reload, 1.0f);
            IsActive = true;
        }

        public override bool Use(float deltaTime, Character character = null)
        {
            if (character == null || reloadTimer > 0.0f) { return false; }
            if (Item.RequireAimToUse && !character.IsKeyDown(InputType.Aim) || hitting) { return false; }

            //don't allow hitting if the character is already hitting with another weapon
            foreach (Item heldItem in character.HeldItems)
            {
                var otherWeapon = heldItem.GetComponent<MeleeWeapon>();
                if (otherWeapon == null) { continue; }
                if (otherWeapon.hitting) { return false; }
            }

            SetUser(character);

            if (hitPos < MathHelper.PiOver4) { return false; }

            ActivateNearbySleepingCharacters();
            reloadTimer = reload;

            item.body.FarseerBody.CollisionCategories = Physics.CollisionProjectile;
            item.body.FarseerBody.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall;
            item.body.FarseerBody.OnCollision += OnCollision;
            item.body.FarseerBody.IsBullet = true;
            item.body.PhysEnabled = true;

            if (!character.AnimController.InWater)
            {
                foreach (Limb l in character.AnimController.Limbs)
                {
                    if (l.IsSevered) { continue; }
                    if (l.type == LimbType.LeftFoot || l.type == LimbType.LeftThigh || l.type == LimbType.LeftLeg) { continue; }
                    if (l.type == LimbType.Head || l.type == LimbType.Torso)
                    {
                        l.body.ApplyLinearImpulse(new Vector2(character.AnimController.Dir * 7.0f, -4.0f));                   
                    }
                    else
                    {
                        l.body.ApplyLinearImpulse(new Vector2(character.AnimController.Dir * 5.0f, -2.0f));
                    }                
                }
            }
            
            hitting = true;
            hitTargets.Clear();

            IsActive = true;

            if (item.AiTarget != null)
            {
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
            }
            return false;
        }
        
        public override void Drop(Character dropper)
        {
            base.Drop(dropper);
            hitting = false;
            hitPos = 0.0f;
        }

        public override void UpdateBroken(float deltaTime, Camera cam)
        {
            Update(deltaTime, cam);
        }
        
        public override void Update(float deltaTime, Camera cam)
        {
            if (!item.body.Enabled) { impactQueue.Clear(); return; }
            if (picker == null && !picker.HeldItems.Contains(item)) { impactQueue.Clear(); IsActive = false; }

            while (impactQueue.Count > 0)
            {
                var impact = impactQueue.Dequeue();
                HandleImpact(impact.Body);
            }
            //in case handling the impact does something to the picker
            if (picker == null) { return; }

            reloadTimer -= deltaTime;
            if (reloadTimer < 0) { reloadTimer = 0; }

            if (!picker.IsKeyDown(InputType.Aim) && !hitting) { hitPos = 0.0f; }

            ApplyStatusEffects(ActionType.OnActive, deltaTime, picker);

            if (item.body.Dir != picker.AnimController.Dir) { item.FlipX(relativeToSub: false); }

            AnimController ac = picker.AnimController;

            //TODO: refactor the hitting logic (get rid of the magic numbers, make it possible to use different kinds of animations for different items)
            if (!hitting)
            {
                bool aim = picker.AllowInput && picker.IsKeyDown(InputType.Aim) && reloadTimer <= 0 && picker.CanAim;
                if (aim)
                {
                    hitPos = MathUtils.WrapAnglePi(Math.Min(hitPos + deltaTime * 5f, MathHelper.PiOver4));
                    ac.HoldItem(deltaTime, item, handlePos, aimPos, Vector2.Zero, false, hitPos, holdAngle + hitPos);
                }
                else
                {
                    hitPos = 0;
                    ac.HoldItem(deltaTime, item, handlePos, holdPos, Vector2.Zero, false, holdAngle);
                }
            }
            else
            {
                hitPos = MathUtils.WrapAnglePi(hitPos - deltaTime * 15f);
                ac.HoldItem(deltaTime, item, handlePos, new Vector2(2, 0), Vector2.Zero, false, hitPos, holdAngle + hitPos); // aimPos not used -> zero (new Vector2(-0.3f, 0.2f)), holdPos new Vector2(0.6f, -0.1f)
                if (hitPos < -MathHelper.PiOver2)
                {
                    RestoreCollision();
                    hitting = false;
                    hitTargets.Clear();
                    hitPos = 0;
                }
            }
        }

        /// <summary>
        /// Activate sleeping ragdolls that are close enough to hit with the weapon (otherwise the collision will not be registered)
        /// </summary>
        private void ActivateNearbySleepingCharacters()
        {
            foreach (Character c in Character.CharacterList)
            {
                if (!c.Enabled || !c.AnimController.BodyInRest) { continue; }
                //do a broad check first
                if (Math.Abs(c.WorldPosition.X - item.WorldPosition.X) > 1000.0f) { continue; }
                if (Math.Abs(c.WorldPosition.Y - item.WorldPosition.Y) > 1000.0f) { continue; }

                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float hitRange = 2.0f;
                    if (Vector2.DistanceSquared(limb.SimPosition, item.SimPosition) < hitRange * hitRange)
                    {
                        c.AnimController.BodyInRest = false;
                        break;
                    }
                }
            }
        }

        private void SetUser(Character character)
        {
            if (User == character) { return; }
            if (User != null && User.Removed) { User = null; }

            User = character;
        }

        private void RestoreCollision()
        {
            impactQueue.Clear();
            item.body.FarseerBody.OnCollision -= OnCollision;
            item.body.CollisionCategories = Physics.CollisionItem;
            item.body.CollidesWith = Physics.CollisionWall;
            item.body.FarseerBody.IsBullet = false;
            item.body.PhysEnabled = false;
        }


        private bool OnCollision(Fixture f1, Fixture f2, Contact contact)
        {
            if (User == null || User.Removed)
            {
                impactQueue.Enqueue(f2);
                return true;
            }

            contact.GetWorldManifold(out Vector2 normal, out var points);

            //ignore collision if there's a wall between the user and the contact point to prevent hitting through walls
            if (Submarine.PickBody(User.AnimController.AimSourceSimPos,
                points[0],
                collisionCategory: Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking,
                allowInsideFixture: true,
                customPredicate: (Fixture fixture) => { return fixture.CollidesWith.HasFlag(Physics.CollisionItem) && fixture.Body != f2.Body; }) != null)
            {
                return false;
            }

            Character targetCharacter = null;
            Limb targetLimb = null;
            Structure targetStructure = null;
            Item targetItem = null;

            if (f2.Body.UserData is Limb)
            {
                targetLimb = (Limb)f2.Body.UserData;
                if (targetLimb.IsSevered || targetLimb.character == null || targetLimb.character == User) { return false; }
                targetCharacter = targetLimb.character;
                if (targetCharacter == picker) { return false; }
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetCharacter)) { return false; }
                }
                else
                {
                    if (hitTargets.Any(t => t is Character)) { return false; }
                }
                hitTargets.Add(targetCharacter);
            }
            else if (f2.Body.UserData is Character)
            {
                targetCharacter = (Character)f2.Body.UserData;
                if (targetCharacter == picker || targetCharacter == User) { return false; }
                targetLimb = targetCharacter.AnimController.GetLimb(LimbType.Torso); //Otherwise armor can be bypassed in strange ways
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetCharacter)) { return false; }
                }
                else
                {
                    if (hitTargets.Any(t => t is Character)) { return false; }
                }
                hitTargets.Add(targetCharacter);
            }
            else if (f2.Body.UserData is Structure)
            {
                targetStructure = (Structure)f2.Body.UserData;
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetStructure)) { return true; }
                }
                else
                {
                    if (hitTargets.Any(t => t is Structure)) { return true; }
                }
                hitTargets.Add(targetStructure);
            }
            else if (f2.Body.UserData is Item)
            {
                targetItem = (Item)f2.Body.UserData;
                if (AllowHitMultiple)
                {
                    if (hitTargets.Contains(targetItem)) { return true; }
                }
                else
                {
                    if (hitTargets.Any(t => t is Item)) { return true; }
                }
                hitTargets.Add(targetItem);
            }
            else
            {
                return false;
            }

            impactQueue.Enqueue(f2);

            return true;
        }

        private void HandleImpact(Body target)
        {
            if (User == null || User.Removed || target == null)
            {
                RestoreCollision();
                hitting = false;
                User = null;
                return;
            }
            
            Limb targetLimb = target.UserData as Limb;
            Character targetCharacter = targetLimb?.character ?? target.UserData as Character;
            Structure targetStructure = target.UserData as Structure;
            Item targetItem = target.UserData as Item;
            
            if (Attack != null)
            {
                Attack.SetUser(User);

                if (targetLimb != null)
                {
                    if (targetLimb.character.Removed) { return; }
                    targetLimb.character.LastDamageSource = item;
                    Attack.DoDamageToLimb(User, targetLimb, item.WorldPosition, 1.0f);
                }
                else if (targetCharacter != null)
                {
                    if (targetCharacter.Removed) { return; }
                    targetCharacter.LastDamageSource = item;
                    Attack.DoDamage(User, targetCharacter, item.WorldPosition, 1.0f);
                }
                else if (targetStructure != null)
                {
                    if (targetStructure.Removed) { return; }
                    Attack.DoDamage(User, targetStructure, item.WorldPosition, 1.0f);
                }
                else if (targetItem != null && targetItem.Prefab.DamagedByMeleeWeapons && targetItem.Condition > 0)
                {
                    if (targetItem.Removed) { return; }
                    Attack.DoDamage(User, targetItem, item.WorldPosition, 1.0f);
                }
                else
                {
                    return;
                }
            }

            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) { return; }

            bool success = Rand.Range(0.0f, 0.5f) < DegreeOfSuccess(User);

#if SERVER
            if (GameMain.Server != null && targetCharacter != null) //TODO: Log structure hits
            {
                GameMain.Server.CreateEntityEvent(item, new object[] 
                {
                    Networking.NetEntityEvent.Type.ApplyStatusEffect,                    
                    success ? ActionType.OnUse : ActionType.OnFailure,
                    null, //itemcomponent
                    targetCharacter.ID, targetLimb
                });

                string logStr = picker?.LogName + " used " + item.Name;
                if (item.ContainedItems != null && item.ContainedItems.Any())
                {
                    logStr += " (" + string.Join(", ", item.ContainedItems.Select(i => i?.Name)) + ")";
                }
                logStr += " on " + targetCharacter.LogName + ".";
                Networking.GameServer.Log(logStr, Networking.ServerLog.MessageType.Attack);
            }
#endif

            if (targetCharacter != null) //TODO: Allow OnUse to happen on structures too maybe??
            {
                ApplyStatusEffects(success ? ActionType.OnUse : ActionType.OnFailure, 1.0f, targetCharacter, targetLimb, user: User);
            }

            if (DeleteOnUse)
            {
                Entity.Spawner.AddToRemoveQueue(item);
            }
        }
    }
}
