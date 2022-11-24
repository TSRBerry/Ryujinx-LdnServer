using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LanPlayServer.Utils
{
    public class Game
    {
        public ulong  ID;
        public string Name;

        public Game(ulong id, string name)
        {
            ID   = id;
            Name = name;
        }
    }
    public class JsonGame
    {
        public string id;
        public string name;
    }

    public static class GameList
    {
        // TODO: populate using some online list or something.
        // We still need to be able to provide game specific info, as we might want to parse AdvertiseData or do special things for different games.
        // (eg. a mode to regulate framerate to keep sync in Mario Kart would be nice)

        private static Dictionary<ulong, Game> _games = new();

        static GameList() {
            try
            {
                JsonGame[] data = JsonConvert.DeserializeObject<JsonGame[]>(File.ReadAllText("Utils/gamelist.json"));

                foreach (JsonGame game in data)
                {
                    try
                    {
                        ulong id = ulong.Parse(game.id.Substring(2), System.Globalization.NumberStyles.HexNumber);

                        _games[id] = new Game(id, game.name);
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            catch (Exception) { }
        }

        public static Game GetGameById(ulong id)
        {
            _games.TryGetValue(id, out Game result);

            return result;
        }
    }
}
