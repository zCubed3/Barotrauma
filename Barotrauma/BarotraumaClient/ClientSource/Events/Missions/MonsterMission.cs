﻿using Barotrauma.Networking;

namespace Barotrauma
{
    public partial class MonsterMission : Mission
    {
        public override void ClientReadInitial(IReadMessage msg)
        {
            byte monsterCount = msg.ReadByte();
            for (int i = 0; i < monsterCount; i++)
            {
                monsters.Add(Character.ReadSpawnData(msg));
            }
            if (monsters.Contains(null))
            {
                throw new System.Exception("Error in MonsterMission.ClientReadInitial: monster list contains null (mission: " + Prefab.Identifier + ")");
            }
            if (monsters.Count != monsterCount)
            {
                throw new System.Exception("Error in MonsterMission.ClientReadInitial: monster count does not match the server count (" + monsterCount + " != " + monsters.Count + "mission: " + Prefab.Identifier + ")");
            }
            InitializeMonsters(monsters);
        }
    }
}
