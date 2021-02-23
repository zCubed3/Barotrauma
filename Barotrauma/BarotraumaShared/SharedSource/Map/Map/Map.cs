﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma
{
	public partial class Map
    {
        public bool AllowDebugTeleport;

        private readonly MapGenerationParams generationParams;

        private Location furthestDiscoveredLocation;

        private int Width => generationParams.Width;
        private int Height => generationParams.Height;

        public Action<Location, LocationConnection> OnLocationSelected;
        /// <summary>
        /// From -> To
        /// </summary>
        public Action<Location, Location> OnLocationChanged;
        public Action<LocationConnection, Mission> OnMissionSelected;

        public Location EndLocation { get; private set; }

        public Location StartLocation { get; private set; }

        public Location CurrentLocation { get; private set; }

        public int CurrentLocationIndex
        {
            get { return Locations.IndexOf(CurrentLocation); }
        }

        public Location SelectedLocation { get; private set; }

        public int SelectedLocationIndex
        {
            get { return Locations.IndexOf(SelectedLocation); }
        }

        public int SelectedMissionIndex
        {
            get { return SelectedConnection == null ? -1 : CurrentLocation.SelectedMissionIndex; }
        }

        public LocationConnection SelectedConnection { get; private set; }

        public string Seed { get; private set; }

        public List<Location> Locations { get; private set; }

        public List<LocationConnection> Connections { get; private set; }

        public Map()
        {
            generationParams = MapGenerationParams.Instance;
            Locations = new List<Location>();
            Connections = new List<LocationConnection>();
        }

        /// <summary>
        /// Load a previously saved campaign map from XML
        /// </summary>
        private Map(CampaignMode campaign, XElement element) : this()
        {
            Seed = element.GetAttributeString("seed", "a");
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));
            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "location":
                        int i = subElement.GetAttributeInt("i", 0);
                        while (Locations.Count <= i)
                        {
                            Locations.Add(null);
                        }
                        Locations[i] = new Location(subElement);
                        break;
                }
            }
            System.Diagnostics.Debug.Assert(!Locations.Contains(null));
            for (int i = 0; i < Locations.Count; i++)
            {
                Locations[i].Reputation ??= new Reputation(campaign.CampaignMetadata, $"location.{i}", -100, 100, Rand.Range(-10, 10, Rand.RandSync.Server));
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "connection":
                        Point locationIndices = subElement.GetAttributePoint("locations", new Point(0, 1));
                        if (locationIndices.X == locationIndices.Y) { continue; }
                        var connection = new LocationConnection(Locations[locationIndices.X], Locations[locationIndices.Y])
                        {
                            Passed = subElement.GetAttributeBool("passed", false),
                            Difficulty = subElement.GetAttributeFloat("difficulty", 0.0f)
                        };
                        Locations[locationIndices.X].Connections.Add(connection);
                        Locations[locationIndices.Y].Connections.Add(connection);
                        connection.LevelData = new LevelData(subElement.Element("Level"));
                        string biomeId = subElement.GetAttributeString("biome", "");
                        connection.Biome = 
                            LevelGenerationParams.GetBiomes().FirstOrDefault(b => b.Identifier == biomeId) ??
                            LevelGenerationParams.GetBiomes().FirstOrDefault(b => b.OldIdentifier == biomeId) ??
                            LevelGenerationParams.GetBiomes().First();
                        Connections.Add(connection);
                        break;
                }
            }

            int startLocationindex = element.GetAttributeInt("startlocation", -1);
            if (startLocationindex > 0 && startLocationindex < Locations.Count)
            {
                StartLocation = Locations[startLocationindex];
            }
            else
            {
                DebugConsole.AddWarning($"Error while loading the map. Start location index out of bounds (index: {startLocationindex}, location count: {Locations.Count}).");
                foreach (Location location in Locations)
                {
                    if (!location.Type.HasOutpost) { continue; }
                    if (StartLocation == null || location.MapPosition.X < StartLocation.MapPosition.X)
                    {
                        StartLocation = location;
                    }
                }
            }
            int endLocationindex = element.GetAttributeInt("endlocation", -1);
            if (endLocationindex > 0 && endLocationindex < Locations.Count)
            {
                EndLocation = Locations[endLocationindex];
            }
            else
            {
                DebugConsole.AddWarning($"Error while loading the map. End location index out of bounds (index: {endLocationindex}, location count: {Locations.Count}).");
                foreach (Location location in Locations)
                {
                    if (EndLocation == null || location.MapPosition.X > EndLocation.MapPosition.X)
                    {
                        EndLocation = location;
                    }
                }
            }

            InitProjectSpecific();
        }

        /// <summary>
        /// Generate a new campaign map from the seed
        /// </summary>
        public Map(CampaignMode campaign, string seed) : this()
        {
            Seed = seed;
            Rand.SetSyncedSeed(ToolBox.StringToInt(Seed));

            Generate();

            if (Locations.Count == 0)
            {
                throw new Exception($"Generating a campaign map failed (no locations created). Width: {Width}, height: {Height}");
            }

            for (int i = 0; i < Locations.Count; i++)
            {
                Locations[i].Reputation ??= new Reputation(campaign.CampaignMetadata, $"location.{i}", -100, 100, Rand.Range(-10, 10, Rand.RandSync.Server));
            }

            foreach (Location location in Locations)
            {
                if (!location.Type.Identifier.Equals("city", StringComparison.OrdinalIgnoreCase) &&
                    !location.Type.Identifier.Equals("outpost", StringComparison.OrdinalIgnoreCase)) 
                { 
                    continue; 
                }
                if (CurrentLocation == null || location.MapPosition.X < CurrentLocation.MapPosition.X)
                {
                    CurrentLocation = StartLocation = furthestDiscoveredLocation = location;
                }
            }
            System.Diagnostics.Debug.Assert(StartLocation != null, "Start location not assigned after level generation.");

            CurrentLocation.Discovered = true;
            CurrentLocation.CreateStore();

            InitProjectSpecific();
        }

        partial void InitProjectSpecific();

        #region Generation

        private void Generate()
        {
            Connections.Clear();
            Locations.Clear();

            List<Vector2> voronoiSites = new List<Vector2>();
            for (float x = 10.0f; x < Width - 10.0f; x += generationParams.VoronoiSiteInterval.X)
            {
                for (float y = 10.0f; y < Height - 10.0f; y += generationParams.VoronoiSiteInterval.Y)
                {
                    voronoiSites.Add(new Vector2(
                        x + generationParams.VoronoiSiteVariance.X * Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server),
                        y + generationParams.VoronoiSiteVariance.Y * Rand.Range(-0.5f, 0.5f, Rand.RandSync.Server)));
                }
            }

            Voronoi voronoi = new Voronoi(0.5f);
            List<GraphEdge> edges = voronoi.MakeVoronoiGraph(voronoiSites, Width, Height);
            float zoneWidth = Width / generationParams.DifficultyZones;

            Vector2 margin = new Vector2(
               Math.Min(10, Width * 0.1f),
               Math.Min(10, Height * 0.2f));

            float startX = margin.X, endX = Width - margin.X;
            float startY = margin.Y, endY = Height - margin.Y;

            if (!edges.Any())
            {
                throw new Exception($"Generating a campaign map failed (no edges in the voronoi graph). Width: {Width}, height: {Height}, margin: {margin}");
            }

            voronoiSites.Clear();
            foreach (GraphEdge edge in edges)
            {
                if (edge.Point1 == edge.Point2) { continue; }

                if (edge.Point1.X < margin.X || edge.Point1.X > Width - margin.X || edge.Point1.Y < startY || edge.Point1.Y > endY) 
                {
                    continue;
                }
                if (edge.Point2.X < margin.X || edge.Point2.X > Width - margin.X || edge.Point2.Y < startY || edge.Point2.Y > endY)
                {
                    continue;
                }

                Location[] newLocations = new Location[2];
                newLocations[0] = Locations.Find(l => l.MapPosition == edge.Point1 || l.MapPosition == edge.Point2);
                newLocations[1] = Locations.Find(l => l != newLocations[0] && (l.MapPosition == edge.Point1 || l.MapPosition == edge.Point2));

                for (int i = 0; i < 2; i++)
                {
                    if (newLocations[i] != null) { continue; }

                    Vector2[] points = new Vector2[] { edge.Point1, edge.Point2 };

                    int positionIndex = Rand.Int(1, Rand.RandSync.Server);

                    Vector2 position = points[positionIndex];
                    if (newLocations[1 - i] != null && newLocations[1 - i].MapPosition == position) position = points[1 - positionIndex];
                    int zone = MathHelper.Clamp((int)Math.Floor(position.X / zoneWidth) + 1, 1, generationParams.DifficultyZones);
                    newLocations[i] = Location.CreateRandom(position, zone, Rand.GetRNG(Rand.RandSync.Server), requireOutpost: false, Locations);
                    Locations.Add(newLocations[i]);
                }

                var newConnection = new LocationConnection(newLocations[0], newLocations[1]);
                Connections.Add(newConnection);
            }

            //remove connections that are too short
            float minConnectionDistanceSqr = generationParams.MinConnectionDistance * generationParams.MinConnectionDistance;
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                LocationConnection connection = Connections[i];

                if (Vector2.DistanceSquared(connection.Locations[0].MapPosition, connection.Locations[1].MapPosition) > minConnectionDistanceSqr)
                {
                    continue;
                }
                
                //locations.Remove(connection.Locations[0]);
                Connections.Remove(connection);

                foreach (LocationConnection connection2 in Connections)
                {
                    if (connection2.Locations[0] == connection.Locations[0]) { connection2.Locations[0] = connection.Locations[1]; }
                    if (connection2.Locations[1] == connection.Locations[0]) { connection2.Locations[1] = connection.Locations[1]; }
                }
            }
            
            HashSet<Location> connectedLocations = new HashSet<Location>();
            foreach (LocationConnection connection in Connections)
            {
                connection.Locations[0].Connections.Add(connection);
                connection.Locations[1].Connections.Add(connection);

                connectedLocations.Add(connection.Locations[0]);
                connectedLocations.Add(connection.Locations[1]);
            }

            //remove orphans
            Locations.RemoveAll(c => !connectedLocations.Contains(c));

            //remove locations that are too close to each other
            float minLocationDistanceSqr = generationParams.MinLocationDistance * generationParams.MinLocationDistance;
            for (int i = Locations.Count - 1; i >= 0; i--)
            {
                for (int j = Locations.Count - 1; j > i; j--)
                {
                    float dist = Vector2.DistanceSquared(Locations[i].MapPosition, Locations[j].MapPosition);
                    if (dist > minLocationDistanceSqr)
                    {
                        continue;
                    }
                    //move connections from Locations[j] to Locations[i]
                    foreach (LocationConnection connection in Locations[j].Connections)
                    {
                        if (connection.Locations[0] == Locations[j])
                        {
                            connection.Locations[0] = Locations[i];
                        }
                        else
                        {
                            connection.Locations[1] = Locations[i];
                        }

                        if (connection.Locations[0] != connection.Locations[1])
                        {
                            Locations[i].Connections.Add(connection);
                        }
                        else
                        {
                            Connections.Remove(connection);
                        }
                    }
                    Locations[i].Connections.RemoveAll(c => c.OtherLocation(Locations[i]) == Locations[j]);
                    Locations.RemoveAt(j);
                }
            }

            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                i = Math.Min(i, Connections.Count - 1);
                LocationConnection connection = Connections[i];
                for (int n = Math.Min(i - 1, Connections.Count - 1); n >= 0; n--)
                {
                    if (connection.Locations.Contains(Connections[n].Locations[0])
                        && connection.Locations.Contains(Connections[n].Locations[1]))
                    {
                        Connections.RemoveAt(n);
                    }
                }
            }

            foreach (Location location in Locations)
            {
                for (int i = location.Connections.Count - 1; i >= 0; i--)
                {
                    if (!Connections.Contains(location.Connections[i]))
                    {
                        location.Connections.RemoveAt(i);
                    }
                }
            }

            foreach (LocationConnection connection in Connections)
            {
                connection.Difficulty = MathHelper.Clamp((connection.CenterPos.X / Width * 100) + Rand.Range(-10.0f, 0.0f, Rand.RandSync.Server), 1.2f, 100.0f);
            }

            AssignBiomes();
            CreateEndLocation();

            foreach (Location location in Locations)
            {
                location.LevelData = new LevelData(location);
            }
            foreach (LocationConnection connection in Connections) 
            { 
                connection.LevelData = new LevelData(connection);
            }
        }

        partial void GenerateLocationConnectionVisuals();

        public Biome GetBiome(Vector2 mapPos)
        {
            return GetBiome(mapPos.X);
        }

        public Biome GetBiome(float xPos)
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            int zoneIndex = (int)Math.Floor(xPos / zoneWidth) + 1;
            if (zoneIndex < 1)
            {
                return LevelGenerationParams.GetBiomes().First();

            }
            else if (zoneIndex >= generationParams.DifficultyZones)
            {
                return LevelGenerationParams.GetBiomes().Last();
            }
            return LevelGenerationParams.GetBiomes().FirstOrDefault(b => b.AllowedZones.Contains(zoneIndex));
        }

        private void AssignBiomes()
        {
            var biomes = LevelGenerationParams.GetBiomes();
            float zoneWidth = Width / generationParams.DifficultyZones;

            List<Biome> allowedBiomes = new List<Biome>(10);
            for (int i = 0; i < generationParams.DifficultyZones; i++)
            {
                allowedBiomes.Clear();
                allowedBiomes.AddRange(biomes.Where(b => b.AllowedZones.Contains(generationParams.DifficultyZones - i)));
                float zoneX = Width - zoneWidth * i;

                foreach (Location location in Locations)
                {
                    if (location.MapPosition.X < zoneX)
                    {
                        location.Biome = allowedBiomes[Rand.Range(0, allowedBiomes.Count, Rand.RandSync.Server)];
                    }
                }
            }
            foreach (LocationConnection connection in Connections)
            {
                if (connection.Biome != null) { continue; }
                connection.Biome = connection.Locations[0].Biome;
            }

            System.Diagnostics.Debug.Assert(Locations.All(l => l.Biome != null));
            System.Diagnostics.Debug.Assert(Connections.All(c => c.Biome != null));
        }

        private void CreateEndLocation()
        {
            float zoneWidth = Width / generationParams.DifficultyZones;
            Vector2 endPos = new Vector2(Width - zoneWidth / 2, Height / 2);
            float closestDist = float.MaxValue;
            EndLocation = Locations.First();
            foreach (Location location in Locations)
            {
                float dist = Vector2.DistanceSquared(endPos, location.MapPosition);
                if (location.Biome.IsEndBiome && dist < closestDist)
                {
                    EndLocation = location;
                    closestDist = dist;
                }
            }

            Location previousToEndLocation = null;
            foreach (Location location in Locations)
            {
                if (!location.Biome.IsEndBiome && (previousToEndLocation == null || location.MapPosition.X > previousToEndLocation.MapPosition.X))
                {
                    previousToEndLocation = location;
                }
            }

            if (EndLocation == null || previousToEndLocation == null) { return; }

            //remove all locations from the end biome except the end location
            for (int i = Locations.Count - 1; i >= 0; i--)
            {
                if (Locations[i].Biome.IsEndBiome && Locations[i] != EndLocation)
                {
                    for (int j = Locations[i].Connections.Count - 1; j >= 0; j--)
                    {
                        if (j >= Locations[i].Connections.Count) { continue; }
                        var connection = Locations[i].Connections[j];
                        var otherLocation = connection.OtherLocation(Locations[i]);
                        Locations[i].Connections.RemoveAt(j);
                        otherLocation?.Connections.Remove(connection);
                        Connections.Remove(connection);
                    }
                    Locations.RemoveAt(i);
                }
            }

            //removed all connections from the second-to-last location, need to reconnect it
            if (!previousToEndLocation.Connections.Any())
            {
                Location connectTo = Locations.First();
                foreach (Location location in Locations)
                {
                    if (!location.Biome.IsEndBiome && location != previousToEndLocation && location.MapPosition.X > connectTo.MapPosition.X)
                    {
                        connectTo = location;
                    }
                }
                var newConnection = new LocationConnection(previousToEndLocation, connectTo)
                {
                    Biome = EndLocation.Biome,
                    Difficulty = 100.0f
                };
                Connections.Add(newConnection);
                previousToEndLocation.Connections.Add(newConnection);
                connectTo.Connections.Add(newConnection);
            }

            var endConnection = new LocationConnection(previousToEndLocation, EndLocation)
            {
                Biome = EndLocation.Biome,
                Difficulty = 100.0f
            };
            Connections.Add(endConnection);
            previousToEndLocation.Connections.Add(endConnection);
            EndLocation.Connections.Add(endConnection);
        }

        private void ExpandBiomes(List<LocationConnection> seeds)
        {
            List<LocationConnection> nextSeeds = new List<LocationConnection>(); 
            foreach (LocationConnection connection in seeds)
            {
                foreach (Location location in connection.Locations)
                {
                    foreach (LocationConnection otherConnection in location.Connections)
                    {
                        if (otherConnection == connection) continue;                        
                        if (otherConnection.Biome != null) continue; //already assigned

                        otherConnection.Biome = connection.Biome;
                        nextSeeds.Add(otherConnection);                        
                    }
                }
            }

            if (nextSeeds.Count > 0)
            {
                ExpandBiomes(nextSeeds);
            }
        }


        #endregion Generation
        
        public void MoveToNextLocation()
        {
            if (SelectedConnection == null)
            {
                DebugConsole.ThrowError("Could not move to the next location (no connection selected).\n"+Environment.StackTrace.CleanupStackTrace());
                return;
            }
            if (SelectedLocation == null)
            {
                DebugConsole.ThrowError("Could not move to the next location (no location selected).\n" + Environment.StackTrace.CleanupStackTrace());
                return;
            }

            Location prevLocation = CurrentLocation;
            SelectedConnection.Passed = true;

            CurrentLocation = SelectedLocation;
            CurrentLocation.Discovered = true;
            SelectedLocation = null;

            CurrentLocation.CreateStore();
            OnLocationChanged?.Invoke(prevLocation, CurrentLocation);

            if (GameMain.GameSession?.GameMode is CampaignMode campaign && campaign.CampaignMetadata is { } metadata)
            {
                metadata.SetValue("campaign.location.id", CurrentLocationIndex);
                metadata.SetValue("campaign.location.name", CurrentLocation.Name);
            }
        }

        public void SetLocation(int index)
        {
            if (index == -1)
            {
                CurrentLocation = null;
                return;
            }

            if (index < 0 || index >= Locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            Location prevLocation = CurrentLocation;
            CurrentLocation = Locations[index];
            CurrentLocation.Discovered = true;

            if (prevLocation != CurrentLocation)
            {
                var connection = CurrentLocation.Connections.Find(c => c.Locations.Contains(prevLocation));
                if (connection != null)
                {
                    connection.Passed = true;
                }
            }

            CurrentLocation.CreateStore();
            OnLocationChanged?.Invoke(prevLocation, CurrentLocation);
        }

        public void SelectLocation(int index)
        {
            if (index == -1)
            {
                SelectedLocation = null;
                SelectedConnection = null;

                OnLocationSelected?.Invoke(null, null);
                return;
            }

            if (index < 0 || index >= Locations.Count)
            {
                DebugConsole.ThrowError("Location index out of bounds");
                return;
            }

            SelectedLocation = Locations[index];
            SelectedConnection = 
                Connections.Find(c => c.Locations.Contains(GameMain.GameSession?.Campaign?.CurrentDisplayLocation) && c.Locations.Contains(SelectedLocation)) ??
                Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
        }

        public void SelectLocation(Location location)
        {
            if (!Locations.Contains(location))
            {
                string errorMsg = "Failed to select a location. " + (location?.Name ?? "null") + " not found in the map.";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Map.SelectLocation:LocationNotFound", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }

            SelectedLocation = location;
            SelectedConnection = Connections.Find(c => c.Locations.Contains(CurrentLocation) && c.Locations.Contains(SelectedLocation));
            OnLocationSelected?.Invoke(SelectedLocation, SelectedConnection);
        }

        public void SelectMission(int missionIndex)
        {
            if (SelectedConnection == null) { return; }
            if (CurrentLocation == null)
            {
                string errorMsg = "Failed to select a mission (current location not set).";
                DebugConsole.ThrowError(errorMsg);
                GameAnalyticsManager.AddErrorEventOnce("Map.SelectMission:CurrentLocationNotSet", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                return;
            }
            CurrentLocation.SelectedMissionIndex = missionIndex;

            //the destination must be the same as the destination of the mission
            if (CurrentLocation.SelectedMission != null && 
                CurrentLocation.SelectedMission.Locations[1] != SelectedLocation)
            {
                CurrentLocation.SelectedMissionIndex = -1;
            }

            OnMissionSelected?.Invoke(SelectedConnection, CurrentLocation.SelectedMission);
        }

        public void SelectRandomLocation(bool preferUndiscovered)
        {
            List<Location> nextLocations = CurrentLocation.Connections.Select(c => c.OtherLocation(CurrentLocation)).ToList();            
            List<Location> undiscoveredLocations = nextLocations.FindAll(l => !l.Discovered);
            
            if (undiscoveredLocations.Count > 0 && preferUndiscovered)
            {
                SelectLocation(undiscoveredLocations[Rand.Int(undiscoveredLocations.Count, Rand.RandSync.Unsynced)]);
            }
            else
            {
                SelectLocation(nextLocations[Rand.Int(nextLocations.Count, Rand.RandSync.Unsynced)]);
            }
        }

        public void ProgressWorld(CampaignMode.TransitionType transitionType, float roundDuration)
        {
            //one step per 10 minutes of play time
            int steps = (int)Math.Floor(roundDuration / (60.0f * 10.0f));
            if (transitionType == CampaignMode.TransitionType.ProgressToNextLocation || 
                transitionType == CampaignMode.TransitionType.ProgressToNextEmptyLocation)
            {
                //at least one step when progressing to the next location, regardless of how long the round took
                steps = Math.Max(1, steps);
            }
            steps = Math.Min(steps, 5);
            for (int i = 0; i < steps; i++)
            {
                ProgressWorld();
            }
        }

        private void ProgressWorld()
        {
            foreach (Location location in Locations)
            {
                if (location.Discovered)
                {
                    if (furthestDiscoveredLocation == null || 
                        location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                    {
                        furthestDiscoveredLocation = location;
                    }
                }
            }

            foreach (Location location in Locations)
            {
                if (location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                {
                    continue;
                }

                if (location == CurrentLocation || location == SelectedLocation) { continue; }

                ProgressLocationTypeChanges(location);

                if (location.Discovered)
                {
                    location.UpdateStore();
                }
            }
        }

        private void ProgressLocationTypeChanges(Location location)
        {
            location.TimeSinceLastTypeChange++;

            if (location.PendingLocationTypeChange != null)
            {
                if (location.PendingLocationTypeChange.First.DetermineProbability(location) <= 0.0f)
                {
                    //remove pending type change if it's no longer allowed
                    location.PendingLocationTypeChange = null;
                }
                else
                {
                    location.PendingLocationTypeChange.Second--;
                    if (location.PendingLocationTypeChange.Second <= 0)
                    {
                        ChangeLocationType(location, location.PendingLocationTypeChange.First);
                    }
                    return;
                }
            }

            //find which types of locations this one can change to
            Dictionary<LocationTypeChange, float> allowedTypeChanges = new Dictionary<LocationTypeChange, float>();
            foreach (LocationTypeChange typeChange in location.Type.CanChangeTo)
            {
                float probability = typeChange.DetermineProbability(location);
                if (probability <= 0.0f) { continue; }
                allowedTypeChanges.Add(typeChange, probability);
            }

            //select a random type change
            if (Rand.Range(0.0f, 1.0f) < allowedTypeChanges.Sum(change => change.Value))
            {
                var selectedTypeChange =
                    ToolBox.SelectWeightedRandom(
                        allowedTypeChanges.Keys.ToList(),
                        allowedTypeChanges.Values.ToList(),
                        Rand.RandSync.Unsynced);
                if (selectedTypeChange != null)
                {
                    if (selectedTypeChange.RequiredDurationRange.X > 0)
                    {
                        location.PendingLocationTypeChange = new Pair<LocationTypeChange, int>(
                            selectedTypeChange,
                            Rand.Range(selectedTypeChange.RequiredDurationRange.X, selectedTypeChange.RequiredDurationRange.Y));
                    }
                    else
                    {
                        ChangeLocationType(location, selectedTypeChange);
                    }
                    return;
                }
            }

            foreach (LocationTypeChange typeChange in location.Type.CanChangeTo)
            {
                if (typeChange.AnyWithinDistance(
                    location,
                    typeChange.RequiredProximityForProbabilityIncrease,
                    (otherLocation) => { return typeChange.RequiredLocations.Contains(otherLocation.Type.Identifier); }))
                {
                    if (!location.ProximityTimer.ContainsKey(typeChange)) { location.ProximityTimer[typeChange] = 0; }
                    location.ProximityTimer[typeChange] += 1;
                }
                else
                {
                    location.ProximityTimer.Remove(typeChange);
                }
            }

        }

        public int DistanceToClosestLocationWithOutpost(Location startingLocation, out Location endingLocation)
        {
            if (startingLocation.Type.HasOutpost)
            {
                endingLocation = startingLocation;
                return 0;
            }

            int iterations = 0;
            int distance = 0;
            endingLocation = null;

            List<Location> testedLocations = new List<Location>();
            List<Location> locationsToTest = new List<Location> { startingLocation };

            while (endingLocation == null && iterations < 100)
            {
                List<Location> nextTestingBatch = new List<Location>();
                for (int i = 0; i < locationsToTest.Count; i++)
                {
                    Location testLocation = locationsToTest[i];
                    for (int j = 0; j < testLocation.Connections.Count; j++)
                    {
                        Location potentialOutpost = testLocation.Connections[j].OtherLocation(testLocation);
                        if (potentialOutpost.Type.HasOutpost)
                        {
                            distance = iterations + 1;
                            endingLocation = potentialOutpost;
                        }
                        else if (!testedLocations.Contains(potentialOutpost))
                        {
                            nextTestingBatch.Add(potentialOutpost);
                        }
                    }

                    testedLocations.Add(testLocation);
                }

                locationsToTest = nextTestingBatch;
                iterations++;
            }

            return distance;
        }

        private void ChangeLocationType(Location location, LocationTypeChange change)
        {
            string prevName = location.Name;
            location.ChangeType(LocationType.List.Find(lt => lt.Identifier.Equals(change.ChangeToType, StringComparison.OrdinalIgnoreCase)));
            ChangeLocationTypeProjSpecific(location, prevName, change);
            location.ProximityTimer.Remove(change);
            location.TimeSinceLastTypeChange = 0;
            location.PendingLocationTypeChange = null;
        }

        partial void ChangeLocationTypeProjSpecific(Location location, string prevName, LocationTypeChange change);

        partial void ClearAnimQueue();

        /// <summary>
        /// Load a previously saved map from an xml element
        /// </summary>
        public static Map Load(CampaignMode campaign, XElement element)
        {
            Map map = new Map(campaign, element);
            map.LoadState(element, false);
#if CLIENT
            map.DrawOffset = -map.CurrentLocation.MapPosition;
#endif
            return map;
        }

        /// <summary>
        /// Load the state of an existing map from xml (current state of locations, where the crew is now, etc).
        /// </summary>
        public void LoadState(XElement element, bool showNotifications)
        {
            ClearAnimQueue();
            SetLocation(element.GetAttributeInt("currentlocation", 0));

            if (!Version.TryParse(element.GetAttributeString("version", ""), out _))
            {
                DebugConsole.ThrowError("Incompatible map save file, loading the game failed.");
                return;
            }

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "location":
                        Location location = Locations[subElement.GetAttributeInt("i", 0)];
                        location.ProximityTimer.Clear();
                        for (int i = 0; i < location.Type.CanChangeTo.Count; i++)
                        {
                            location.ProximityTimer.Add(location.Type.CanChangeTo[i], subElement.GetAttributeInt("changetimer" + i, 0));
                        }
                        int locationTypeChangeIndex = subElement.GetAttributeInt("pendinglocationtypechange", -1);
                        if (locationTypeChangeIndex > 0 && locationTypeChangeIndex < location.Type.CanChangeTo.Count - 1)
                        {
                            location.PendingLocationTypeChange = new Pair<LocationTypeChange, int>(
                                location.Type.CanChangeTo[locationTypeChangeIndex],
                                subElement.GetAttributeInt("pendinglocationtypechangetimer", 0));
                        }
                        location.TimeSinceLastTypeChange = subElement.GetAttributeInt("timesincelasttypechange", 0);
                        location.Discovered = subElement.GetAttributeBool("discovered", false);
                        if (location.Discovered)
                        {
#if CLIENT
                            RemoveFogOfWar(location);
#endif
                            if (furthestDiscoveredLocation == null || location.MapPosition.X > furthestDiscoveredLocation.MapPosition.X)
                            {
                                furthestDiscoveredLocation = location;
                            }
                        }

                        string locationType = subElement.GetAttributeString("type", "");
                        string prevLocationName = location.Name;
                        LocationType prevLocationType = location.Type;
                        LocationType newLocationType = LocationType.List.Find(lt => lt.Identifier.Equals(locationType, StringComparison.OrdinalIgnoreCase)) ?? LocationType.List.First();
                        location.ChangeType(newLocationType);
                        if (showNotifications && prevLocationType != location.Type)
                        {
                            var change = prevLocationType.CanChangeTo.Find(c => c.ChangeToType.Equals(location.Type.Identifier, StringComparison.OrdinalIgnoreCase));
                            if (change != null)
                            {
                                ChangeLocationTypeProjSpecific(location, prevLocationName, change);
                                location.TimeSinceLastTypeChange = 0;
                            }
                        }

                        location.LoadStore(subElement);
                        location.LoadMissions(subElement);

                        break;
                    case "connection":
                        int connectionIndex = subElement.GetAttributeInt("i", 0);
                        Connections[connectionIndex].Passed = subElement.GetAttributeBool("passed", false);
                        break;
                }
            }

            foreach (Location location in Locations)
            {
                location?.InstantiateLoadedMissions(this);
            }

            int currentLocationConnection = element.GetAttributeInt("currentlocationconnection", -1);
            if (currentLocationConnection >= 0)
            {
                SelectLocation(Connections[currentLocationConnection].OtherLocation(CurrentLocation));
            }
            else
            {
                //this should not be possible, you can't enter non-outpost locations (= natural formations)
                if (CurrentLocation != null && !CurrentLocation.Type.HasOutpost && SelectedConnection == null)
                {
                    DebugConsole.AddWarning($"Error while loading campaign map state. Submarine in a location with no outpost ({CurrentLocation.Name}). Loading the first adjacent connection...");
                    SelectLocation(CurrentLocation.Connections[0].OtherLocation(CurrentLocation));
                }
            }
        }

        public void Save(XElement element)
        {
            XElement mapElement = new XElement("map");

            mapElement.Add(new XAttribute("version", GameMain.Version.ToString()));
            mapElement.Add(new XAttribute("currentlocation", CurrentLocationIndex));
            if (GameMain.GameSession.GameMode is CampaignMode campaign)
            {
                if (campaign.NextLevel != null && campaign.NextLevel.Type == LevelData.LevelType.LocationConnection)
                {
                    mapElement.Add(new XAttribute("currentlocationconnection", Connections.IndexOf(CurrentLocation.Connections.Find(c => c.LevelData == campaign.NextLevel))));
                }
                else if (Level.Loaded != null && Level.Loaded.Type == LevelData.LevelType.LocationConnection && !CurrentLocation.Type.HasOutpost)
                {
                    mapElement.Add(new XAttribute("currentlocationconnection", Connections.IndexOf(Connections.Find(c => c.LevelData == Level.Loaded.LevelData))));
                }
            }
            mapElement.Add(new XAttribute("selectedlocation", SelectedLocationIndex));
            mapElement.Add(new XAttribute("startlocation", Locations.IndexOf(StartLocation)));
            mapElement.Add(new XAttribute("endlocation", Locations.IndexOf(EndLocation)));
            mapElement.Add(new XAttribute("seed", Seed));

            for (int i = 0; i < Locations.Count; i++)
            {
                var location = Locations[i];
                var locationElement = location.Save(this, mapElement);
                locationElement.Add(new XAttribute("i", i));
            }

            for (int i = 0; i < Connections.Count; i++)
            {
                var connection = Connections[i];

                var connectionElement = new XElement("connection",
                    new XAttribute("passed", connection.Passed),
                    new XAttribute("difficulty", connection.Difficulty),
                    new XAttribute("biome", connection.Biome.Identifier),
                    new XAttribute("locations", Locations.IndexOf(connection.Locations[0]) + "," + Locations.IndexOf(connection.Locations[1])));
                connection.LevelData.Save(connectionElement);
                mapElement.Add(connectionElement);
            }

            element.Add(mapElement);
        }

        public void Remove()
        {
            foreach (Location location in Locations)
            {
                location.Remove();
            }
            RemoveProjSpecific();
        }

        partial void RemoveProjSpecific();
    }
}
