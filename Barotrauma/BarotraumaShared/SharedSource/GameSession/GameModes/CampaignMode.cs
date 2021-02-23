﻿using Barotrauma.Items.Components;
using FarseerPhysics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
	public abstract partial class CampaignMode : GameMode
    {
        const int MaxMoney = int.MaxValue / 2; //about 1 billion
        public const int InitialMoney = 8500;

        //duration of the cinematic + credits at the end of the campaign
        protected const float EndCinematicDuration = 240.0f;
        //duration of the camera transition at the end of a round
        protected const float EndTransitionDuration = 5.0f;
        //there can be no events before this time has passed during the 1st campaign round
        const float FirstRoundEventDelay = 30.0f;

        public enum InteractionType { None, Talk, Map, Crew, Store, Repair, Upgrade, PurchaseSub }

        public readonly CargoManager CargoManager;
        public UpgradeManager UpgradeManager;

        public List<Faction> Factions;

        public CampaignMetadata CampaignMetadata;

        protected XElement petsElement;

        public enum TransitionType
        {
            None,
            //leaving a location level
            LeaveLocation,
            //progressing to next location level
            ProgressToNextLocation,
            //returning to previous location level
            ReturnToPreviousLocation,
            //returning to previous location (one with no level/outpost, the player is taken to the map screen and must choose their next destination)
            ReturnToPreviousEmptyLocation,
            //progressing to an empty location (one with no level/outpost, the player is taken to the map screen and must choose their next destination)
            ProgressToNextEmptyLocation,
            //end of campaign (reached end location)
            End
        }

        public bool IsFirstRound { get; protected set; } = true;

        public bool DisableEvents
        {
            get { return IsFirstRound && Timing.TotalTime < GameMain.GameSession.RoundStartTime + FirstRoundEventDelay; }
        }

        public bool CheatsEnabled;

        public const int HullRepairCost = 500, ItemRepairCost = 500, ShuttleReplaceCost = 1000;

        protected bool wasDocked;

        //key = dialog flag, double = Timing.TotalTime when the line was last said
        private readonly Dictionary<string, double> dialogLastSpoken = new Dictionary<string, double>();

        public bool PurchasedHullRepairs, PurchasedLostShuttles, PurchasedItemRepairs;

        public SubmarineInfo PendingSubmarineSwitch;

        protected Map map;
        public Map Map
        {
            get { return map; }
        }

        public override Mission Mission
        {
            get
            {
                return Map.CurrentLocation?.SelectedMission;
            }
        }

        private int money;
        public int Money
        {
            get { return money; }
            set { money = MathHelper.Clamp(value, 0, MaxMoney); }
        }

        public LevelData NextLevel
        {
            get;
            protected set;
        }

        protected CampaignMode(GameModePreset preset)
            : base(preset)
        {
            Money = InitialMoney;
            CargoManager = new CargoManager(this);
        }

        /// <summary>
        /// The location that's displayed as the "current one" in the map screen. Normally the current outpost or the location at the start of the level,
        /// but when selecting the next destination at the end of the level at an uninhabited location we use the location at the end
        /// </summary>
        public Location CurrentDisplayLocation
        {
            get
            {
                if (Level.Loaded?.EndLocation != null && !Level.Loaded.Generating &&
                    Level.Loaded.Type == LevelData.LevelType.LocationConnection &&
                    GetAvailableTransition(out _, out _) == TransitionType.ProgressToNextEmptyLocation)
                {
                    return Level.Loaded.EndLocation;
                }
                return Level.Loaded?.StartLocation ?? Map.CurrentLocation;
            }
        }

        public List<Submarine> GetSubsToLeaveBehind(Submarine leavingSub)
        {
            //leave subs behind if they're not docked to the leaving sub and not at the same exit
            return Submarine.Loaded.FindAll(s =>
                s != leavingSub &&
                !leavingSub.DockedTo.Contains(s) &&
                s.Info.Type == SubmarineType.Player &&
                (s.AtEndPosition != leavingSub.AtEndPosition || s.AtStartPosition != leavingSub.AtStartPosition));
        }

        public override void Start()
        {
            base.Start();
            dialogLastSpoken.Clear();
            characterOutOfBoundsTimer.Clear();

            if (PurchasedHullRepairs)
            {
                foreach (Structure wall in Structure.WallList)
                {
                    if (wall.Submarine == null || wall.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (wall.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(wall.Submarine))
                    {
                        for (int i = 0; i < wall.SectionCount; i++)
                        {
                            wall.SetDamage(i, 0, createNetworkEvent: false);
                        }
                    }
                }
                PurchasedHullRepairs = false;
            }
            if (PurchasedItemRepairs)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.Submarine == null || item.Submarine.Info.Type != SubmarineType.Player) { continue; }
                    if (item.Submarine == Submarine.MainSub || Submarine.MainSub.DockedTo.Contains(item.Submarine))
                    {
                        if (item.GetComponent<Items.Components.Repairable>() != null)
                        {
                            item.Condition = item.MaxCondition;
                        }
                    }
                }
                PurchasedItemRepairs = false;
            }
            PurchasedLostShuttles = false;
            var connectedSubs = Submarine.MainSub.GetConnectedSubs();
            wasDocked = Level.Loaded.StartOutpost != null && connectedSubs.Contains(Level.Loaded.StartOutpost);
        }

        public void InitCampaignData()
        {
            Factions = new List<Faction>();
            foreach (FactionPrefab factionPrefab in FactionPrefab.Prefabs)
            {
                Factions.Add(new Faction(CampaignMetadata, factionPrefab));
            }
        }

        /// <summary>
        /// Automatically cleared after triggering -> no need to unregister
        /// </summary>
        public event Action BeforeLevelLoading;

        public void LoadNewLevel()
        {
            if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient) 
            {
                return;
            }

            if (CoroutineManager.IsCoroutineRunning("LevelTransition"))
            {
                DebugConsole.ThrowError("Level transition already running.\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            BeforeLevelLoading?.Invoke();
            BeforeLevelLoading = null;

            if (Level.Loaded == null || Submarine.MainSub == null)
            {
                LoadInitialLevel();
                return;
            }

            var availableTransition = GetAvailableTransition(out LevelData nextLevel, out Submarine leavingSub);

            if (availableTransition == TransitionType.None)
            {
                DebugConsole.ThrowError("Failed to load a new campaign level. No available level transitions " +
                    "(current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                    "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                    "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                    "at start: " + (leavingSub?.AtStartPosition.ToString() ?? "null") + ", " +
                    "at end: " + (leavingSub?.AtEndPosition.ToString() ?? "null") + ")\n" +
                    Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (nextLevel == null)
            {
                DebugConsole.ThrowError("Failed to load a new campaign level. No available level transitions " +
                    "(transition type: " + availableTransition + ", " +
                    "current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                    "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                    "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                    "at start: " + (leavingSub?.AtStartPosition.ToString() ?? "null") + ", " +
                    "at end: " + (leavingSub?.AtEndPosition.ToString() ?? "null") + ")\n" +
                    Environment.StackTrace.CleanupStackTrace());
                return;
            }
#if CLIENT
            ShowCampaignUI = ForceMapUI = false;
#endif
            DebugConsole.NewMessage("Transitioning to " + (nextLevel?.Seed ?? "null") +
                " (current location: " + (map.CurrentLocation?.Name ?? "null") + ", " +
                "selected location: " + (map.SelectedLocation?.Name ?? "null") + ", " +
                "leaving sub: " + (leavingSub?.Info?.Name ?? "null") + ", " +
                "at start: " + (leavingSub?.AtStartPosition.ToString() ?? "null") + ", " +
                "at end: " + (leavingSub?.AtEndPosition.ToString() ?? "null") + ", " +
                "transition type: " + availableTransition + ")");

            IsFirstRound = false;
            bool mirror = map.SelectedConnection != null && map.CurrentLocation != map.SelectedConnection.Locations[0];
            CoroutineManager.StartCoroutine(DoLevelTransition(availableTransition, nextLevel, leavingSub, mirror), "LevelTransition");
        }

        /// <summary>
        /// Load the first level and start the round after loading a save file
        /// </summary>
        protected abstract void LoadInitialLevel();

        protected abstract IEnumerable<object> DoLevelTransition(TransitionType transitionType, LevelData newLevel, Submarine leavingSub, bool mirror, List<TraitorMissionResult> traitorResults = null);

        /// <summary>
        /// Which type of transition between levels is currently possible (if any)
        /// </summary>
        public TransitionType GetAvailableTransition(out LevelData nextLevel, out Submarine leavingSub)
        {
            if (Level.Loaded == null || Submarine.MainSub == null)
            {
                nextLevel = null;
                leavingSub = null;
                return TransitionType.None;
            }

            leavingSub = GetLeavingSub();
            if (leavingSub == null)
            {
                nextLevel = null;
                return TransitionType.None;
            }

            //currently travelling from location to another
            if (Level.Loaded.Type == LevelData.LevelType.LocationConnection)
            {
                if (leavingSub.AtEndPosition)
                {
                    if (Map.EndLocation != null && 
                        map.SelectedLocation == Map.EndLocation && 
                        Map.EndLocation.Connections.Any(c => c.LevelData == Level.Loaded.LevelData))
                    {
                        nextLevel = map.StartLocation.LevelData;
                        return TransitionType.End;
                    }
                    if (Level.Loaded.EndLocation != null && Level.Loaded.EndLocation.Type.HasOutpost && Level.Loaded.EndOutpost != null)
                    {
                        nextLevel = Level.Loaded.EndLocation.LevelData;
                        return TransitionType.ProgressToNextLocation;
                    }
                    else if (map.SelectedConnection != null)
                    {
                        nextLevel = Level.Loaded.LevelData != map.SelectedConnection?.LevelData || (map.SelectedConnection.Locations[0] == Level.Loaded.EndLocation == Level.Loaded.Mirrored) ? 
                            map.SelectedConnection.LevelData : null;
                        return TransitionType.ProgressToNextEmptyLocation;
                    }
                    else
                    {
                        nextLevel = null;
                        return TransitionType.ProgressToNextEmptyLocation;
                    }
                }
                else if (leavingSub.AtStartPosition)
                {
                    if (map.CurrentLocation.Type.HasOutpost && Level.Loaded.StartOutpost != null)
                    {
                        nextLevel = map.CurrentLocation.LevelData;
                        return TransitionType.ReturnToPreviousLocation;
                    }
                    else if (map.SelectedLocation != null && map.SelectedLocation != map.CurrentLocation && !map.CurrentLocation.Type.HasOutpost && 
                        (Level.Loaded.LevelData != map.SelectedConnection.LevelData))
                    {
                        nextLevel = map.SelectedConnection.LevelData;
                        return TransitionType.LeaveLocation;
                    }
                    else
                    {
                        nextLevel = map.SelectedConnection?.LevelData;
                        return TransitionType.ReturnToPreviousEmptyLocation;
                    }
                }
                else
                {
                    nextLevel = null;
                    return TransitionType.None;
                }
            }
            else if (Level.Loaded.Type == LevelData.LevelType.Outpost)
            {
                nextLevel = map.SelectedLocation == null ? null : map.SelectedConnection?.LevelData;
                return nextLevel == null ? TransitionType.None : TransitionType.LeaveLocation;
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Which submarine is at a position where it can leave the level and enter another one (if any).
        /// </summary>
        private Submarine GetLeavingSub()
        {
            //in single player, only the sub the controlled character is inside can transition between levels
            //in multiplayer, if there's subs at both ends of the level, only the one with more players inside can transition
            //TODO: ignore players who don't have the permission to trigger a transition between levels?
            var leavingPlayers = Character.CharacterList.Where(c => !c.IsDead && (c == Character.Controlled || c.IsRemotePlayer));

            //allow leaving if inside an outpost, and the submarine is either docked to it or close enough
            Submarine leavingSubAtStart = GetLeavingSubAtStart(leavingPlayers);
            Submarine leavingSubAtEnd = GetLeavingSubAtEnd(leavingPlayers);

            if (Level.IsLoadedOutpost)
            {
                leavingSubAtStart ??= Submarine.MainSub;
                leavingSubAtEnd ??= Submarine.MainSub;            
            }
            int playersInSubAtStart = leavingSubAtStart == null ? 0 :
                leavingPlayers.Count(c => c.Submarine == leavingSubAtStart || leavingSubAtStart.DockedTo.Contains(c.Submarine) || (Level.Loaded.StartOutpost != null && c.Submarine == Level.Loaded.StartOutpost));
            int playersInSubAtEnd = leavingSubAtEnd == null ? 0 :
                leavingPlayers.Count(c => c.Submarine == leavingSubAtEnd || leavingSubAtEnd.DockedTo.Contains(c.Submarine) || (Level.Loaded.EndOutpost != null && c.Submarine == Level.Loaded.EndOutpost));

            if (playersInSubAtStart == 0 && playersInSubAtEnd == 0) { return null; }

            return playersInSubAtStart > playersInSubAtEnd ? leavingSubAtStart : leavingSubAtEnd;

            static Submarine GetLeavingSubAtStart(IEnumerable<Character> leavingPlayers)
            {
                if (Level.Loaded.StartOutpost == null)
                {
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.StartPosition, ignoreOutposts: true);
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
                else
                {
                    //if there's a sub docked to the outpost, we can leave the level
                    if (Level.Loaded.StartOutpost.DockedTo.Any())
                    {
                        var dockedSub = Level.Loaded.StartOutpost.DockedTo.FirstOrDefault();
                        return dockedSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : dockedSub;
                    }

                    //nothing docked, check if there's a sub close enough to the outpost and someone inside the outpost
                    if (Level.Loaded.Type == LevelData.LevelType.LocationConnection && !leavingPlayers.Any(s => s.Submarine == Level.Loaded.StartOutpost)) { return null; }
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.StartOutpost.WorldPosition, ignoreOutposts: true);
                    if (closestSub == null || !closestSub.AtStartPosition) { return null; }
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
            }

            static Submarine GetLeavingSubAtEnd(IEnumerable<Character> leavingPlayers)
            {
                //no "end" in outpost levels
                if (Level.Loaded.Type == LevelData.LevelType.Outpost) { return null; }

                if (Level.Loaded.EndOutpost == null)
                {
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.EndPosition, ignoreOutposts: true);
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
                else
                {
                    //if there's a sub docked to the outpost, we can leave the level
                    if (Level.Loaded.EndOutpost.DockedTo.Any())
                    {
                        var dockedSub = Level.Loaded.EndOutpost.DockedTo.FirstOrDefault();
                        return dockedSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : dockedSub;
                    }

                    //nothing docked, check if there's a sub close enough to the outpost and someone inside the outpost
                    if (Level.Loaded.Type == LevelData.LevelType.LocationConnection && !leavingPlayers.Any(s => s.Submarine == Level.Loaded.EndOutpost)) { return null; }
                    Submarine closestSub = Submarine.FindClosest(Level.Loaded.EndOutpost.WorldPosition, ignoreOutposts: true);
                    if (closestSub == null || !closestSub.AtEndPosition) { return null; }
                    return closestSub.DockedTo.Contains(Submarine.MainSub) ? Submarine.MainSub : closestSub;
                }
            }
        }

        public override void End(CampaignMode.TransitionType transitionType = CampaignMode.TransitionType.None)
        {
            List<Item> takenItems = new List<Item>();
            foreach (Item item in Item.ItemList)
            {
                if (!item.SpawnedInOutpost || item.OriginalModuleIndex < 0) { continue; }
                if ((!(item.GetRootInventoryOwner()?.Submarine?.Info?.IsOutpost ?? false)) || item.Submarine == null || !item.Submarine.Info.IsOutpost)
                {
                    takenItems.Add(item);
                }
            }
            map.CurrentLocation.RegisterTakenItems(takenItems);

            map.CurrentLocation.AddToStock(CargoManager.SoldItems);
            CargoManager.ClearSoldItemsProjSpecific();
            map.CurrentLocation.RemoveFromStock(CargoManager.PurchasedItems);
            if (GameMain.NetworkMember == null)
            {
                CargoManager.ClearItemsInBuyCrate();
                CargoManager.ClearItemsInSellCrate();
            }
            else
            {
                if (GameMain.NetworkMember.IsServer)
                {
                    CargoManager.ClearItemsInBuyCrate();
                }
                else if (GameMain.NetworkMember.IsClient)
                {
                    CargoManager.ClearItemsInSellCrate();
                }
            }

            if (Level.Loaded?.StartOutpost != null)
            {
                List<Character> killedCharacters = new List<Character>();
                foreach (Character c in Level.Loaded.StartOutpost.Info.OutpostNPCs.SelectMany(kpv => kpv.Value))
                {
                    if (!c.IsDead && !c.Removed) { continue; }
                    killedCharacters.Add(c);
                }
                map.CurrentLocation.RegisterKilledCharacters(killedCharacters);
                Level.Loaded.StartOutpost.Info.OutpostNPCs.Clear();
            }

            List<Character> deadCharacters = Character.CharacterList.FindAll(c => c.IsDead);
            foreach (Character c in deadCharacters)
            {
                if (c.IsDead) 
                {
                    CrewManager.RemoveCharacterInfo(c.Info);
                    c.DespawnNow();
                }
            }

            foreach (CharacterInfo ci in CrewManager.CharacterInfos.ToList())
            {
                if (ci.CauseOfDeath != null)
                {
                    CrewManager.RemoveCharacterInfo(ci);
                }
                ci?.ResetCurrentOrder();
            }

            foreach (DockingPort port in DockingPort.List)
            {
                if (port.Door != null &
                    port.Item.Submarine.Info.Type == SubmarineType.Player && 
                    port.DockingTarget?.Item?.Submarine != null && 
                    port.DockingTarget.Item.Submarine.Info.IsOutpost)
                {
                    port.Door.IsOpen = false;
                }
            }
        }


        public void EndCampaign()
        {
            foreach (LocationConnection connection in Map.Connections)
            {
                connection.Difficulty = MathHelper.Lerp(connection.Difficulty, 100.0f, 0.25f);
                connection.LevelData.Difficulty = connection.Difficulty;
            }
            foreach (Location location in Map.Locations)
            {
                location.CreateStore(force: true);
                location.ClearMissions();
            }
            Map.SetLocation(Map.Locations.IndexOf(Map.StartLocation));
            Map.SelectLocation(-1);
            EndCampaignProjSpecific();

            if (CampaignMetadata != null)
            {
                int loops = CampaignMetadata.GetInt("campaign.endings", 0);
                CampaignMetadata.SetValue("campaign.endings",  loops + 1);
            }
        }

        protected virtual void EndCampaignProjSpecific() { }

        public bool TryHireCharacter(Location location, CharacterInfo characterInfo)
        {
            if (Money < characterInfo.Salary) { return false; }

            characterInfo.IsNewHire = true;

            location.RemoveHireableCharacter(characterInfo);
            CrewManager.AddCharacterInfo(characterInfo);
            Money -= characterInfo.Salary;

            return true;
        }

        private void NPCInteract(Character npc, Character interactor)
        {
            if (!npc.AllowCustomInteract) { return; }
            NPCInteractProjSpecific(npc, interactor);
            string coroutineName = "DoCharacterWait." + (npc?.ID ?? Entity.NullEntityID);
            if (!CoroutineManager.IsCoroutineRunning(coroutineName))
            {
                CoroutineManager.StartCoroutine(DoCharacterWait(npc, interactor), coroutineName);
            }
        }

        private IEnumerable<object> DoCharacterWait(Character npc, Character interactor)
        {
            if (npc == null || interactor == null) { yield return CoroutineStatus.Failure; }

            HumanAIController humanAI = npc.AIController as HumanAIController;
            if (humanAI == null) { yield return CoroutineStatus.Failure; }

            OrderInfo? prevSpeakerOrder = null;
            if (humanAI.CurrentOrder != null)
            {
                prevSpeakerOrder = new OrderInfo(humanAI.CurrentOrder, humanAI.CurrentOrderOption);
            }
            var waitOrder = Order.PrefabList.Find(o => o.Identifier.Equals("wait", StringComparison.OrdinalIgnoreCase));
            humanAI.SetOrder(waitOrder, option: string.Empty, orderGiver: null, speak: false);
            humanAI.FaceTarget(interactor);
            
            while (!npc.Removed && !interactor.Removed &&
                Vector2.DistanceSquared(npc.WorldPosition, interactor.WorldPosition) < 300.0f * 300.0f &&
                humanAI.CurrentOrder == waitOrder &&
                humanAI.AllowCampaignInteraction() &&
                !interactor.IsIncapacitated)
            {
                yield return CoroutineStatus.Running;
            }

#if CLIENT
            ShowCampaignUI = false;
#endif

            if (humanAI.CurrentOrder == waitOrder)
            {
                if (prevSpeakerOrder != null)
                {
                    humanAI.SetOrder(prevSpeakerOrder.Value.Order, prevSpeakerOrder.Value.OrderOption, orderGiver: null, speak: false);
                }
                else
                {
                    humanAI.SetOrder(null, string.Empty, orderGiver: null, speak: false);
                }
            }
            yield return CoroutineStatus.Success;
        }

        partial void NPCInteractProjSpecific(Character npc, Character interactor);

        public void AssignNPCMenuInteraction(Character character, InteractionType interactionType)
        {
            character.CampaignInteractionType = interactionType;
            if (interactionType == InteractionType.None) 
            {
                character.SetCustomInteract(null, null);
                return; 
            }
            character.CharacterHealth.UseHealthWindow = false;
            //character.CanInventoryBeAccessed = false;
            character.SetCustomInteract(
                NPCInteract,
#if CLIENT
                hudText: TextManager.GetWithVariable("CampaignInteraction." + interactionType, "[key]", GameMain.Config.KeyBindText(InputType.Use)));
#else
                hudText: TextManager.Get("CampaignInteraction." + interactionType));
#endif
        }

        private readonly Dictionary<Character, float> characterOutOfBoundsTimer = new Dictionary<Character, float>();

        protected void KeepCharactersCloseToOutpost(float deltaTime)
        {
            const float MaxDist = 3000.0f;
            const float MinDist = 2500.0f;

            if (!Level.IsLoadedOutpost) { return; }

            Rectangle worldBorders = Submarine.MainSub.GetDockedBorders();
            worldBorders.Location += Submarine.MainSub.WorldPosition.ToPoint();

            foreach (Character c in Character.CharacterList)
            {
                if ((c != Character.Controlled && !c.IsRemotePlayer) || 
                    c.Removed || c.IsDead || c.IsIncapacitated || c.Submarine != null)
                {
                    if (characterOutOfBoundsTimer.ContainsKey(c)) 
                    {
                        c.OverrideMovement = null;
                        characterOutOfBoundsTimer.Remove(c); 
                    }
                    continue;
                }

                if (c.WorldPosition.Y < worldBorders.Y - worldBorders.Height - MaxDist) 
                { 
                    if (!characterOutOfBoundsTimer.ContainsKey(c)) 
                    { 
                        characterOutOfBoundsTimer.Add(c, 0.0f); 
                    }
                    else
                    {
                        characterOutOfBoundsTimer[c] += deltaTime;
                    }
                }
                else if (c.WorldPosition.Y > worldBorders.Y - worldBorders.Height - MinDist)
                {
                    if (characterOutOfBoundsTimer.ContainsKey(c))
                    {
                        c.OverrideMovement = null; 
                        characterOutOfBoundsTimer.Remove(c); 
                    }
                }
            }

            foreach (KeyValuePair<Character, float> character in characterOutOfBoundsTimer)
            {
                if (character.Value <= 0.0f)
                {
                    if (IsSinglePlayer)
                    {
#if CLIENT
                        GameMain.GameSession.CrewManager.AddSinglePlayerChatMessage(
                            TextManager.Get("RadioAnnouncerName"), 
                            TextManager.Get("TooFarFromOutpostWarning"), 
                            Networking.ChatMessageType.Default, 
                            sender: null);
#endif
                    }
                    else
                    {
#if SERVER
                        foreach (Networking.Client c in GameMain.Server.ConnectedClients)
                        {
                        
                            GameMain.Server.SendDirectChatMessage(Networking.ChatMessage.Create(
                                TextManager.Get("RadioAnnouncerName"), 
                                TextManager.Get("TooFarFromOutpostWarning"),  Networking.ChatMessageType.Default, null), c);
                        }
#endif
                    }
                }
                character.Key.OverrideMovement = Vector2.UnitY * 10.0f;
#if CLIENT
                Character.DisableControls = true;
#endif
                //if the character doesn't get back up in 10 seconds (something blocking the way?), teleport it closer
                if (character.Value > 10.0f)
                {
                    Vector2 teleportPos = character.Key.WorldPosition;
                    teleportPos += Vector2.Normalize(Submarine.MainSub.WorldPosition - character.Key.WorldPosition) * 100.0f;
                    character.Key.AnimController.SetPosition(ConvertUnits.ToSimUnits(teleportPos));
                }
            }
        }

        public void OutpostNPCAttacked(Character npc, Character attacker, AttackResult attackResult)
        {
            if (npc == null || attacker == null || npc.IsDead || npc.IsInstigator) { return; }
            if (npc.TeamID != CharacterTeamType.FriendlyNPC) { return; }
            if (!attacker.IsRemotePlayer && attacker != Character.Controlled) { return; }
            Location location = Map?.CurrentLocation;
            if (location != null)
            {
                location.Reputation.Value -= attackResult.Damage * Reputation.ReputationLossPerNPCDamage;
            }
        }

        public abstract void Save(XElement element);
        
        public void LogState()
        {
            DebugConsole.NewMessage("********* CAMPAIGN STATUS *********", Color.White);
            DebugConsole.NewMessage("   Money: " + Money, Color.White);
            DebugConsole.NewMessage("   Current location: " + map.CurrentLocation.Name, Color.White);

            DebugConsole.NewMessage("   Available destinations: ", Color.White);
            for (int i = 0; i < map.CurrentLocation.Connections.Count; i++)
            {
                Location destination = map.CurrentLocation.Connections[i].OtherLocation(map.CurrentLocation);
                if (destination == map.SelectedLocation)
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name + " [SELECTED]", Color.White);
                }
                else
                {
                    DebugConsole.NewMessage("     " + i + ". " + destination.Name, Color.White);
                }
            }
            
            if (map.CurrentLocation?.SelectedMission != null)
            {
                DebugConsole.NewMessage("   Selected mission: " + map.CurrentLocation.SelectedMission.Name, Color.White);
                DebugConsole.NewMessage("\n" + map.CurrentLocation.SelectedMission.Description, Color.White);
            }
        }

        public override void Remove()
        {
            base.Remove();
            map?.Remove();
            map = null;
        }
    }
}
