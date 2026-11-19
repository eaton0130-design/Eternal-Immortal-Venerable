using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    // 地圖資料模型（對應 map.json）
    public class GameMap
    {
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public MapPoint Start { get; set; } = new MapPoint();
        public List<MapPoint> Walls { get; set; } = new List<MapPoint>();
        public List<MapLocation> Locations { get; set; } = new List<MapLocation>();
        public List<MapEvent> Events { get; set; } = new List<MapEvent>();
    }

    public class MapPoint
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public class MapLocation
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class MapEvent
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Chapter { get; set; } = "";
        public string Hint { get; set; } = "";
    }

    /// <summary>
    /// 地圖探索畫面（UserControl）。不再另開視窗，由 MainMenuForm 無縫切換進出。
    /// WASD / 方向鍵移動、C 報座標、Enter/G 觸發劇情、Esc 回主選單。
    /// </summary>
    public class MapView : UserControl
    {
        private GameMap? map;
        private int playerX;
        private int playerY;
        private readonly HashSet<string> wallSet = new HashSet<string>();
        private readonly Label lblStatus;
        private readonly Label lblHint;

        // 載入新地圖時，Focus 引发的補報不會重複講一次
        private bool suppressFocusAnnouncement;

        // 傳送進場参数（由劇情引擎傳入）；若為 null 則用 map.json 的預設起點
        private string? pendingMapName;
        private int? pendingX;
        private int? pendingY;
        private string? pendingAnnouncement;

        public event Action? ExitRequested;
        public event Action<string>? StoryRequested;
        public event Action? MenuRequested;

        public MapView()
        {
            this.BackColor = SystemColors.Control;

            // 純 Label + Prism 語音，不依賴任何 UIA 即時通知
            lblStatus = new Label();
            lblStatus.Location = new Point(30, 30);
            lblStatus.Size = new Size(500, 130);
            lblStatus.Font = new Font("微軟正黑體", 13, FontStyle.Bold);
            lblStatus.TabIndex = 0;
            lblStatus.TabStop = true;
            lblStatus.AccessibleName = "地圖狀態播報";
            lblStatus.AccessibleDescription = "顯示目前位置、移動結果與事件提示";
            lblStatus.GotFocus += LblStatus_GotFocus;

            lblHint = new Label();
            lblHint.Text = "操作提示：W/A/S/D 或方向鍵移動，C 鍵查詢座標，Enter/G 鍵觸發劇情，F1 鍵開啟選單，Esc 鍵返回";
            lblHint.Location = new Point(30, 180);
            lblHint.AutoSize = true;
            lblHint.MaximumSize = new Size(500, 0);
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;
            lblHint.AccessibleName = "地圖操作提示";
            lblHint.AccessibleDescription = "WASD 或方向鍵移動，C 查詢座標，Enter 或 G 觸發劇情，F1 開啟選單，Esc 返回";

            this.Controls.Add(lblStatus);
            this.Controls.Add(lblHint);
        }

        /// <summary>由主 Form 在切換到本畫面後呼叫。</summary>
        public void Activate()
        {
            // 第一次進入、或有新的傳送指令時才載入地圖；單純回來時保留玩家位置。
            if (map == null || !string.IsNullOrEmpty(pendingMapName))
            {
                suppressFocusAnnouncement = true;
                LoadMapAndStart();
                suppressFocusAnnouncement = false;
            }

            lblStatus.Focus();
        }

        // 供外部（劇情播完時）呼叫：指定要進入的地圖、座標與語音提示
        public void EnterMap(string mapName, int x, int y, string announcement)
        {
            pendingMapName = mapName;
            pendingX = x;
            pendingY = y;
            pendingAnnouncement = announcement;
        }

        /// <summary>更新狀態 Label，並透過單一語音通道播報。</summary>
        private void Say(string text, bool interrupt = true)
        {
            lblStatus.Text = text;
            lblStatus.AccessibleName = text;
            ScreenReader.Speak(text, interrupt);
        }

        private void LblStatus_GotFocus(object? sender, EventArgs e)
        {
            if (suppressFocusAnnouncement)
            {
                return;
            }

            // 回到地圖時補報一次位置，但不插隊打斷剛剛的劇情訊息。
            AnnounceLocation(false, "", false);
        }

        private void LoadMapAndStart()
        {
            // 若有傳送指令，優先載入指定地圖
            if (!string.IsNullOrEmpty(pendingMapName))
            {
                var target = TeleportEngine.FindMap(pendingMapName);
                if (target == null)
                {
                    Say($"找不到名為「{pendingMapName}」的地圖！");
                    return;
                }

                if (!TeleportEngine.IsValidDestination(target, pendingX ?? target.Start.X, pendingY ?? target.Start.Y))
                {
                    Say($"傳送座標無效：{pendingMapName} ({pendingX},{pendingY})。");
                    return;
                }

                map = target;
                wallSet.Clear();
                foreach (var wall in map.Walls)
                {
                    wallSet.Add($"{wall.X},{wall.Y}");
                }
                playerX = pendingX ?? target.Start.X;
                playerY = pendingY ?? target.Start.Y;

                string announce = string.IsNullOrEmpty(pendingAnnouncement)
                    ? $"你被傳送到了{map.Name}。"
                    : pendingAnnouncement;
                string warning = ValidateMapObjects(target);
                Say($"{announce}目前位置：X {playerX}，Y {playerY}。{warning}");

                // 傳送已消費掉，之後從劇情回來才不會又被丟回起點
                pendingMapName = null;
                pendingX = null;
                pendingY = null;
                pendingAnnouncement = null;
                return;
            }

            // 沒有傳送指令：載入 map.json 第一張地圖（常恆劍宗）
            LoadDefaultMap();
        }

        private void LoadDefaultMap()
        {
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "map.json");
            if (!File.Exists(jsonPath))
            {
                jsonPath = "map.json";
            }

            if (!File.Exists(jsonPath))
            {
                Say("找不到 map.json 地圖檔案！");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var collection = JsonSerializer.Deserialize<MapCollection>(jsonContent, TeleportEngine.JsonOptions);

                if (collection == null || collection.Maps.Count == 0)
                {
                    Say("map.json 地圖資料格式不正確！");
                    return;
                }

                map = collection.Maps[0];
                wallSet.Clear();
                foreach (var wall in map.Walls)
                {
                    wallSet.Add($"{wall.X},{wall.Y}");
                }

                playerX = map.Start.X;
                playerY = map.Start.Y;
                AnnounceLocation(true, ValidateMapObjects(map));
            }
            catch (Exception ex)
            {
                Say($"讀取地圖 JSON 失敗：{ex.Message}");
            }
        }

        /// <summary>由唯一的 Form 統一轉送鍵盤事件。</summary>
        public void HandleKey(KeyEventArgs e)
        {
            // F1 開啟遊戲選單；放在地圖檢查之前，連地圖載入失敗都能查看角色資訊
            if (e.KeyCode == Keys.F1)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                MenuRequested?.Invoke();
                return;
            }

            if (map == null)
            {
                return;
            }

            int newX = playerX;
            int newY = playerY;

            switch (e.KeyCode)
            {
                case Keys.W:
                case Keys.Up:
                    newY--;
                    break;
                case Keys.S:
                case Keys.Down:
                    newY++;
                    break;
                case Keys.A:
                case Keys.Left:
                    newX--;
                    break;
                case Keys.D:
                case Keys.Right:
                    newX++;
                    break;
                case Keys.C:
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    AnnounceLocation(false);
                    return;
                case Keys.Enter:
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    EnterStoryFromCurrentEvent();
                    return;
                case Keys.G:
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    TriggerEvent();
                    return;
                case Keys.Escape:
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    ExitRequested?.Invoke();
                    return;
                default:
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (newX < 0 || newX >= map.Width || newY < 0 || newY >= map.Height)
            {
                Say("撞到邊界了，走不出去！");
                return;
            }

            if (wallSet.Contains($"{newX},{newY}"))
            {
                Say("前面有障礙物，換個方向吧。");
                return;
            }

            playerX = newX;
            playerY = newY;
            AnnounceLocation(true);
        }

        // 防呆檢查：地點／事件不可以踩在牆上，否則玩家永遠走不到
        private string ValidateMapObjects(GameMap target)
        {
            var problems = new List<string>();

            foreach (var loc in target.Locations)
            {
                if (wallSet.Contains($"{loc.X},{loc.Y}"))
                {
                    problems.Add($"地點「{loc.Name}」");
                }
            }

            foreach (var ev in target.Events)
            {
                if (wallSet.Contains($"{ev.X},{ev.Y}"))
                {
                    problems.Add($"事件（{ev.X},{ev.Y} → {ev.Chapter}）");
                }
            }

            return problems.Count == 0
                ? ""
                : $"⚠ 警告：地圖「{target.Name}」有物件位於牆壁上，玩家無法抵達：{string.Join("、", problems)}。";
        }

        private void AnnounceLocation(bool includeDescription, string warning = "", bool interrupt = true)
        {
            if (map == null)
            {
                return;
            }

            var location = map.Locations.Find(l => l.X == playerX && l.Y == playerY);
            string message;

            if (location != null)
            {
                message = $"{location.Name}。目前位置：X {playerX}，Y {playerY}。";
                if (includeDescription)
                {
                    message += location.Description;
                }
            }
            else
            {
                message = $"目前位置：X {playerX}，Y {playerY}。";
            }

            Say(message + warning, interrupt);
        }

        private void EnterStoryFromCurrentEvent()
        {
            if (map == null)
            {
                return;
            }

            var mapEvent = map.Events.Find(ev => ev.X == playerX && ev.Y == playerY);
            if (mapEvent == null)
            {
                Say("這裡沒有可進入的劇情。");
                return;
            }

            StoryRequested?.Invoke(mapEvent.Chapter);
        }

        private void TriggerEvent()
        {
            if (map == null)
            {
                return;
            }

            var mapEvent = map.Events.Find(ev => ev.X == playerX && ev.Y == playerY);
            if (mapEvent == null)
            {
                Say("這裡沒有可觸發的事件。");
                return;
            }

            Say($"{mapEvent.Hint}（劇情章節：{mapEvent.Chapter}）");
        }
    }
}
