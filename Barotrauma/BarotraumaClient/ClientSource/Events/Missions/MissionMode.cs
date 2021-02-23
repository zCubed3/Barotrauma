﻿using Microsoft.Xna.Framework;
using System;

namespace Barotrauma
{
    public abstract partial class MissionMode : GameMode
    {
        public override void ShowStartMessage()
        {
            if (mission == null) return;

            new GUIMessageBox(mission.Name, mission.Description, new string[0], type: GUIMessageBox.Type.InGame, icon: mission.Prefab.Icon)
            {
                IconColor = mission.Prefab.IconColor,
                UserData = "missionstartmessage"
            };
        }
    }
}
