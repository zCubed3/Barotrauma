﻿// #define DISABLE_MISSIONS

using System;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{
    public partial class TraitorManager
    {
        public static readonly Random Random = new Random((int)DateTime.UtcNow.Ticks);

        // All traitor related functionality should use the following interface for generating random values
        public static int RandomInt(int n) => Random.Next(n);

        // All traitor related functionality should use the following interface for generating random values
        public static double RandomDouble() => Random.NextDouble();

        public readonly Dictionary<CharacterTeamType, Traitor.TraitorMission> Missions = new Dictionary<CharacterTeamType, Traitor.TraitorMission>();

        public string GetCodeWords(CharacterTeamType team) => Missions.TryGetValue(team, out var mission) ? mission.CodeWords : "";
        public string GetCodeResponse(CharacterTeamType team) => Missions.TryGetValue(team, out var mission) ? mission.CodeResponse : "";

        public IEnumerable<Traitor> Traitors => Missions.Values.SelectMany(mission => mission.Traitors.Values);

        private float startCountdown = 0.0f;
        private GameServer server;

        public bool ShouldEndRound
        {
            get;
            set;
        }

        public bool IsTraitor(Character character)
        {
            if (Traitors == null)
            {
                return false;
            }
            return Traitors.Any(traitor => traitor.Character == character);
        }

        public string GetTraitorRole(Character character)
        {
            var traitor = Traitors.FirstOrDefault(candidate => candidate.Character == character);
            if (traitor == null)
            {
                return "";
            }
            return traitor.Role;
        }

        public TraitorManager()
        {
        }

        public void Start(GameServer server)
        {
#if DISABLE_MISSIONS
            return;
#endif
            if (server == null) { return; }

            ShouldEndRound = false;

            this.server = server;
            startCountdown = MathHelper.Lerp(server.ServerSettings.TraitorsMinStartDelay, server.ServerSettings.TraitorsMaxStartDelay, (float)RandomDouble());
        }

        public void SkipStartDelay()
        {
            startCountdown = 0.01f;
        }

        public void Update(float deltaTime)
        {
            if (ShouldEndRound) { return; }

#if DISABLE_MISSIONS
            return;
#endif
            if (Missions.Any())
            {
                bool missionCompleted = false;
                bool gameShouldEnd = false;
                CharacterTeamType winningTeam = CharacterTeamType.None;
                foreach (var mission in Missions)
                {
                    mission.Value.Update(deltaTime, () =>
                    {
                        switch (mission.Key)
                        {
                            case CharacterTeamType.Team1:
                                winningTeam = (winningTeam == CharacterTeamType.None) ? CharacterTeamType.Team2 : CharacterTeamType.None;
                                break;
                            case CharacterTeamType.Team2:
                                winningTeam = (winningTeam == CharacterTeamType.None) ? CharacterTeamType.Team1 : CharacterTeamType.None;
                                break;
                            default:
                                break;
                        }
                        gameShouldEnd = true;
                    });
                    if (!gameShouldEnd && mission.Value.IsCompleted)
                    {
                        missionCompleted = true;
                        foreach (var traitor in mission.Value.Traitors.Values)
                        {
                            traitor.UpdateCurrentObjective("", mission.Value.Identifier);
                        }
                    }
                }
                if (gameShouldEnd)
                {
                    GameMain.GameSession.WinningTeam = winningTeam;
                    ShouldEndRound = true;
                    return;
                }
                if (missionCompleted)
                {
                    Missions.Clear();
                    startCountdown = MathHelper.Lerp(server.ServerSettings.TraitorsMinRestartDelay, server.ServerSettings.TraitorsMaxRestartDelay, (float)RandomDouble());
                }
            }
            else if (startCountdown > 0.0f && server.GameStarted)
            {
                startCountdown -= deltaTime;
                if (startCountdown <= 0.0f)
                {
                    int playerCharactersCount = server.ConnectedClients.Sum(client => client.Character != null && !client.Character.IsDead ? 1 : 0);
                    if (playerCharactersCount < server.ServerSettings.TraitorsMinPlayerCount)
                    {
                        startCountdown = MathHelper.Lerp(server.ServerSettings.TraitorsMinRestartDelay, server.ServerSettings.TraitorsMaxRestartDelay, (float)RandomDouble());
                        return;
                    }
                    if (Character.CharacterList.Count(c => !c.IsDead && c.TeamID == CharacterTeamType.Team1 || c.TeamID == CharacterTeamType.Team2) <= 1)
                    {
                        return;
                    }
                    if (GameMain.GameSession.Mission is CombatMission)
                    {
                        var teamIds = new[] { CharacterTeamType.Team1, CharacterTeamType.Team2 };
                        foreach (var teamId in teamIds)
                        {
                            if (server.ConnectedClients.Count(c => c.Character != null && !c.Character.IsDead && c.TeamID == teamId) < 2)
                            {
                                continue;
                            }
                            var mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                            if (mission != null)
                            {
                                Missions.Add(teamId, mission);
                            }
                        }
                        var canBeStartedCount = Missions.Sum(mission => mission.Value.CanBeStarted(server, this, mission.Key) ? 1 : 0);
                        if (canBeStartedCount >= Missions.Count)
                        {
                            var startSuccessCount = Missions.Sum(mission => mission.Value.Start(server, this, mission.Key) ? 1 : 0);
                            if (startSuccessCount >= Missions.Count)
                            {
                                return;
                            }
                        }
                    }
                    else
                    {
                        var mission = TraitorMissionPrefab.RandomPrefab()?.Instantiate();
                        if (mission != null) {
                            if (mission.CanBeStarted(server, this, CharacterTeamType.None))
                            {
                                if (mission.Start(server, this, CharacterTeamType.None))
                                {
                                    Missions.Add(CharacterTeamType.None, mission);
                                    return;
                                }
                            }
                        }
                    }
                    Missions.Clear();
                    startCountdown = MathHelper.Lerp(server.ServerSettings.TraitorsMinRestartDelay, server.ServerSettings.TraitorsMaxRestartDelay, (float)RandomDouble());
                }
            }
        }

        public List<TraitorMissionResult> GetEndResults()
        {
            List<TraitorMissionResult> results = new List<TraitorMissionResult>();

#if DISABLE_MISSIONS
            return results;
#endif
            if (GameMain.Server == null || !Missions.Any()) { return results; }

            foreach (var mission in Missions)
            {
                results.Add(new TraitorMissionResult(mission.Value));
            }

            return results;
        }
    }
}
