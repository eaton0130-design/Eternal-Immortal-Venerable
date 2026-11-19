using System;
using System.Collections.Generic;
using System.Text.Json;

namespace AccessibleGridGame
{
    // 劇情章節的內容項目：可能是純文字句子、附配音的句子，或傳送指令
    public class StoryEntry
    {
        public string? Text { get; set; }
        public string? Voice { get; set; }
        public TeleportRequest? Teleport { get; set; }
        public bool IsTeleport => Teleport != null;
        public bool HasVoice => !string.IsNullOrWhiteSpace(Voice);
    }

    // 來自 story.json 的傳送指令：{ "teleport": { "map": "神秘空間", "x": 2, "y": 2, "announcement": "..." } }
    public class TeleportRequest
    {
        public string Map { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public string Announcement { get; set; } = "";
    }

    // 地圖集合的容器的（對應 map.json 的 { "maps": [...] } 結構）
    public class MapCollection
    {
        public List<GameMap> Maps { get; set; } = new List<GameMap>();
    }

    // 傳送引擎：負責把 story.json 的傳送物件翻成 MapView 看得懂的目標
    // 以后只要在某章節最後加一筆 { "teleport": { "map": "...", "x": 0, "y": 0 } }，不用改任何 C# 程式碼
    public static class TeleportEngine
    {
        // JSON 使用 camelCase（maps / name / map / x...），C# 属性使用 PascalCase。
        // System.Text.Json 預設大小寫敏感，若不開這顆會全部對應不到而導致「傳送失敗」。
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // 從混合陣列（字串 + 物件）中解析出劇情步驟
        public static List<StoryEntry> ParseEntries(JsonElement arrayElement)
        {
            var entries = new List<StoryEntry>();

            foreach (var item in arrayElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    entries.Add(new StoryEntry { Text = item.GetString() ?? "" });
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    if (item.TryGetProperty("teleport", out var teleportElement))
                    {
                        var teleport = teleportElement.Deserialize<TeleportRequest>(JsonOptions);
                        if (teleport != null)
                        {
                            entries.Add(new StoryEntry { Teleport = teleport });
                        }
                    }
                    else if (item.TryGetProperty("text", out var textElement)
                             && textElement.ValueKind == JsonValueKind.String)
                    {
                        // 附配音的句子：{ "text": "...", "voice": "sounds/voice/01_1.wav" }
                        string? voice = null;
                        if (item.TryGetProperty("voice", out var voiceElement)
                            && voiceElement.ValueKind == JsonValueKind.String)
                        {
                            voice = voiceElement.GetString();
                        }

                        entries.Add(new StoryEntry
                        {
                            Text = textElement.GetString() ?? "",
                            Voice = voice
                        });
                    }
                }
            }

            return entries;
        }

        // 讀取 map.json，找出指定名稱的地圖
        public static GameMap? FindMap(string mapName)
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map.json");
            if (!File.Exists(jsonPath))
            {
                jsonPath = "map.json";
            }

            if (!File.Exists(jsonPath))
            {
                return null;
            }

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var collection = JsonSerializer.Deserialize<MapCollection>(jsonContent, JsonOptions);
                if (collection == null) return null;

                return collection.Maps.Find(m =>
                    string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        // 檢查傳送座標是否落在地圖範圍內且不是牆
        public static bool IsValidDestination(GameMap map, int x, int y)
        {
            if (x < 0 || x >= map.Width || y < 0 || y >= map.Height) return false;
            return !map.Walls.Exists(w => w.X == x && w.Y == y);
        }
    }
}
