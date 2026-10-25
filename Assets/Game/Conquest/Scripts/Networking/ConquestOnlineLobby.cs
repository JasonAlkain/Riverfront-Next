using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Game.Conquest.Networking
{
    /// <summary>An online match as the match list shows it.</summary>
    public struct OnlineMatchInfo
    {
        public string name;
        public string map;
        public int players;
        public int maxPlayers;
        public string joinCode;
    }

    /// <summary>
    /// Lists online matches through Unity Lobby. An online host publishes a public lobby entry that
    /// carries its Relay join code, map and player count; Browse queries those entries and joins a
    /// match with its code, the same as typing it. Only matches on this build's version are listed.
    /// Players never join the Lobby itself: it is only the listing.
    /// </summary>
    public static class ConquestOnlineLobby
    {
        private const string KeyCode = "code";
        private const string KeyMap = "map";
        private const string KeyPlayers = "players";
        // Indexed so the query can filter on it.
        private const string KeyVersion = "version";

        public static async Task<string> PublishAsync(string name, int maxPlayers, string joinCode, string map, int players)
        {
            await ConquestOnlineServices.EnsureSignedInAsync();
            var options = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>
                {
                    { KeyCode, new DataObject(DataObject.VisibilityOptions.Public, joinCode) },
                    { KeyMap, new DataObject(DataObject.VisibilityOptions.Public, map ?? "") },
                    { KeyPlayers, new DataObject(DataObject.VisibilityOptions.Public, players.ToString()) },
                    { KeyVersion, new DataObject(DataObject.VisibilityOptions.Public, ConquestSessionManager.BuildVersion, DataObject.IndexOptions.S1) },
                },
            };
            string listed = string.IsNullOrWhiteSpace(name) ? "Conquest match" : name.Trim();
            var lobby = await LobbyService.Instance.CreateLobbyAsync(listed, Math.Max(2, maxPlayers), options);
            return lobby.Id;
        }

        public static Task HeartbeatAsync(string lobbyId) => LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);

        public static Task UpdateAsync(string lobbyId, int players, string map)
        {
            return LobbyService.Instance.UpdateLobbyAsync(lobbyId, new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { KeyPlayers, new DataObject(DataObject.VisibilityOptions.Public, players.ToString()) },
                    { KeyMap, new DataObject(DataObject.VisibilityOptions.Public, map ?? "") },
                },
            });
        }

        public static Task RemoveAsync(string lobbyId) => LobbyService.Instance.DeleteLobbyAsync(lobbyId);

        public static async Task<List<OnlineMatchInfo>> QueryAsync(int count)
        {
            await ConquestOnlineServices.EnsureSignedInAsync();
            var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
            {
                Count = count,
                Filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.S1, ConquestSessionManager.BuildVersion, QueryFilter.OpOptions.EQ),
                },
                Order = new List<QueryOrder> { new QueryOrder(false, QueryOrder.FieldOptions.LastUpdated) },
            });
            var list = new List<OnlineMatchInfo>();
            if (response?.Results == null) return list;
            foreach (var lobby in response.Results)
            {
                if (lobby?.Data == null || !lobby.Data.TryGetValue(KeyCode, out var code) || string.IsNullOrEmpty(code.Value)) continue;
                int.TryParse(Value(lobby, KeyPlayers), out int players);
                list.Add(new OnlineMatchInfo
                {
                    name = lobby.Name,
                    map = Value(lobby, KeyMap),
                    players = players,
                    maxPlayers = lobby.MaxPlayers,
                    joinCode = code.Value,
                });
            }
            return list;
        }

        private static string Value(Unity.Services.Lobbies.Models.Lobby lobby, string key) =>
            lobby.Data != null && lobby.Data.TryGetValue(key, out var d) && d != null ? d.Value : "";
    }
}
