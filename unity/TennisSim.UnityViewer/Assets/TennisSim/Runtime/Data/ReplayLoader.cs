using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TennisSim.Viewer
{
    public static class ReplayLoader
    {
        public static ReplayData Load(string path) => Parse(File.ReadAllText(path), Path.GetFileName(path));
        public static ReplayData Parse(string json, string fileName = "memory.json")
        {
            var root = Obj(StrictJson.Parse(json));
            if (Text(root, "schemaVersion") != "1.0") throw new FormatException("Unsupported replay schemaVersion; expected 1.0");
            if (Text(root, "engineVersion") != "tennissim-mvp-1" && Text(root, "engineVersion") != "tennissim-mvp-2" && Text(root, "engineVersion") != "tennissim-mvp-3" && Text(root, "engineVersion") != "tennissim-mvp-4" && Text(root, "engineVersion") != "tennissim-mvp-5" && Text(root, "engineVersion") != "tennissim-mvp-6") throw new FormatException("Unsupported engineVersion / coordinate contract");
            var input = Obj(Get(root, "input"));
            double seed = Number(input, "seed");
            if (seed < 0 || seed > uint.MaxValue || seed != Math.Truncate(seed)) throw new FormatException("Invalid seed");
            var data = new ReplayData {
                FileName = fileName, Seed = (uint)seed, EngineVersion = Text(root, "engineVersion"),
                TickSeconds = Number(Obj(Get(input, "config")), "tickSeconds"),
                PlayerIds = Array(input, "players").Select(p => Text(Obj(p), "id")).ToArray(),
                Status = Text(root, "status"), Diagnostic = Text(root, "diagnostic"),
                FinalScore = Score(Get(root, "finalScore"))
            };
            if (data.PlayerIds.Length != 2 || data.PlayerIds.Any(string.IsNullOrEmpty) || data.PlayerIds[0] == data.PlayerIds[1]) throw new FormatException("Two distinct player IDs required");
            data.Frames = Array(root, "frames").Select(s => State(s, data.PlayerIds)).ToArray();
            data.Events = Array(root, "events").Select(item => {
                var e = Obj(item); var before = Get(e, "before");
                return new ReplayEvent { Sequence = Integer(e, "sequence"), Time = Number(e, "time"), Point = Integer(e, "point"), Kind = Text(e, "kind"), PlayerId = Text(e, "playerId"), Reason = Text(e, "reason"), Before = before == null ? null : State(before, data.PlayerIds), State = State(Get(e, "state"), data.PlayerIds) };
            }).ToArray();
            ReplayValidator.Validate(data); return data;
        }
        static ReplayState State(object value, string[] ids)
        {
            var s = Obj(value); var players = Array(s, "players");
            if (players.Count != 2 || Text(Obj(players[0]), "id") != ids[0] || Text(Obj(players[1]), "id") != ids[1]) throw new FormatException("State player order differs from input");
            return new ReplayState { Time = Number(s, "time"), Point = Integer(s, "point"), Phase = Text(s, "phase"), Score = Score(Get(s, "score")), Ball = Vector(Get(Obj(Get(s, "ball")), "position")), A = Vector(Get(Obj(players[0]), "position")), B = Vector(Get(Obj(players[1]), "position")) };
        }
        static Position Vector(object value) { var v = Obj(value); return new Position(Number(v, "x"), Number(v, "y"), Number(v, "z")); }
        static ReplayScore Score(object value)
        {
            var s = Obj(value); var games = Array(s, "games");
            if (games.Count != 2) throw new FormatException("Score requires two games values");
            return new ReplayScore { Display = Text(s, "display"), PointsPlayed = Integer(s, "pointsPlayed"), Winner = Integer(s, "winner"), Complete = Boolean(s, "complete"), Games = games.Select(x => Int(x, "games")).ToArray() };
        }
        static Dictionary<string, object> Obj(object value) => value as Dictionary<string, object> ?? throw new FormatException("Expected JSON object");
        static object Get(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) ? v : throw new FormatException("Missing required field: " + key);
        static List<object> Array(Dictionary<string, object> o, string key) => Get(o, key) as List<object> ?? throw new FormatException("Expected array: " + key);
        static string Text(Dictionary<string, object> o, string key) => Get(o, key) as string ?? throw new FormatException("Expected string: " + key);
        static bool Boolean(Dictionary<string, object> o, string key) => Get(o, key) is bool b ? b : throw new FormatException("Expected boolean: " + key);
        static double Number(Dictionary<string, object> o, string key) => Get(o, key) is double n ? n : throw new FormatException("Expected number: " + key);
        static int Int(object value, string key) => value is double n && n >= int.MinValue && n <= int.MaxValue && n == Math.Truncate(n) ? (int)n : throw new FormatException("Expected integer: " + key);
        static int Integer(Dictionary<string, object> o, string key) => Int(Get(o, key), key);
    }
}
