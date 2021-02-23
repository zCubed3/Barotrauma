﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public partial class MineralMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            byte caveCount = msg.ReadByte();
            for (int i = 0; i < caveCount; i++)
            {
                byte selectedCave = msg.ReadByte();
                if (selectedCave < 255 && Level.Loaded != null)
                {
                    if (selectedCave < Level.Loaded.Caves.Count)
                    {
                        Level.Loaded.Caves[selectedCave].DisplayOnSonar = true;
                    }
                    else
                    {
                        DebugConsole.ThrowError($"Cave index out of bounds when reading nest mission data. Index: {selectedCave}, number of caves: {Level.Loaded.Caves.Count}");
                    }
                }
            }

            for (int i = 0; i < ResourceClusters.Count; i++)
            {
                var amount = msg.ReadByte();
                var rotation = msg.ReadSingle();
                for (int j = 0; j < amount; j++)
                {
                    var item = Item.ReadSpawnData(msg);
                    if (item.GetComponent<Holdable>() is Holdable h)
                    {
                        h.AttachToWall();
                        item.Rotation = rotation;
                    }
                    if (SpawnedResources.TryGetValue(item.Prefab.Identifier, out var resources))
                    {
                        resources.Add(item);
                    }
                    else
                    {
                        SpawnedResources.Add(item.Prefab.Identifier, new List<Item>() { item });
                    }
                }
            }

            CalculateMissionClusterPositions();

            for(int i = 0; i < ResourceClusters.Count; i++)
            {
                var identifier = msg.ReadString();
                var count = msg.ReadByte();
                var resources = new Item[count];
                for (int j = 0; j < count; j++)
                {
                    var id = msg.ReadUInt16();
                    var entity = Entity.FindEntityByID(id);
                    if (!(entity is Item item)) { continue; }
                    resources[j] = item;
                }
                RelevantLevelResources.Add(identifier, resources);
            }
        }
    }
}
