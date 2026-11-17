using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace AccessibleGridGame
{
    // 支援即時語音播報的無障礙地圖狀態顯示元件
    public class LiveAnnouncementLabel : Label
    {
        public LiveAnnouncementLabel()
        {
            this.LiveSetting = AutomationLiveSetting.Assertive;
        }

        public void Speak(string text)
        {
            this.Text = text;
            this.AccessibleName = text;
            this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }

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

    // 地圖探索視窗：WASD / 方向鍵移動、C 報座標、G 觸發劇情、Esc 離開
    public class GameForm : Form
    {
        private GameMap? map;
        private int playerX;
        private int playerY;
        private readonly HashSet<string> wallSet = new HashSet<string>();
        private LiveAnnouncementLabel lblStatus = null!;
        private Label lblHint = null!;

        // 傳送進場参数（由劇情引擎傳入）；若為 null 則用 map.json 的預設起點
        private string? pendingMapName;
        private int? pendingX;
        private int? pendingY;
        private string? pendingAnnouncement;

        public GameForm()
        {
            this.Text = "八荒大陸 - 地圖探索";
            this.Size = new Size(560, 260);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.KeyDown += GameForm_KeyDown;

            lblStatus = new LiveAnnouncementLabel();
            lblStatus.Location = new Point(30, 30);
            lblStatus.Size = new Size(500, 130);
            lblStatus.Font = new Font("微軟正黑體", 13, FontStyle.Bold);
            lblStatus.TabIndex = 0;
            lblStatus.TabStop = true;
            lblStatus.AccessibleName = "地圖狀態播報";
            lblStatus.AccessibleDescription = "顯示目前位置、移動結果與事件提示";

            lblHint = new Label();
            lblHint.Text = "操作提示：W/A/S/D 或方向鍵移動，C 鍵查詢座標，G 鍵觸發劇情，Esc 鍵離開";
            lblHint.Location = new Point(30, 180);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;

            this.Controls.Add(lblStatus);
            this.Controls.Add(lblHint);

            this.Shown += (s, e) => LoadMapAndStart();
        }

        // 供外部（劇情播完時）呼叫：指定要進入的地圖、座標與語音提示
        public void EnterMap(string mapName, int x, int y, string announcement)
        {
            pendingMapName = mapName;
            pendingX = x;
            pendingY = y;
            pendingAnnouncement = announcement;
        }

        private void LoadMapAndStart()
        {
            // 若有傳送指令，優先載入指定地圖
            if (!string.IsNullOrEmpty(pendingMapName))
            {
                var target = TeleportEngine.FindMap(pendingMapName);
                if (target == null)
                {
                    lblStatus.Speak($"找不到名為「{pendingMapName}」的地圖！");
                    return;
                }

                if (!TeleportEngine.IsValidDestination(target, pendingX ?? target.Start.X, pendingY ?? target.Start.Y))
                {
                    lblStatus.Speak($"傳送座標無效：{pendingMapName} ({pendingX},{pendingY})。");
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
                lblStatus.Speak($"{announce}目前位置：X {playerX}，Y {playerY}。{warning}");
                this.Text = $"八荒大陸 - {map.Name}";
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
                lblStatus.Speak("找不到 map.json 地圖檔案！");
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                var collection = JsonSerializer.Deserialize<MapCollection>(jsonContent, TeleportEngine.JsonOptions);

                if (collection == null || collection.Maps.Count == 0)
                {
                    lblStatus.Speak("map.json 地圖資料格式不正確！");
                    return;
                }

                map = collection.Maps[0];
                foreach (var wall in map.Walls)
                {
                    wallSet.Add($"{wall.X},{wall.Y}");
                }

                playerX = map.Start.X;
                playerY = map.Start.Y;
                this.Text = $"八荒大陸 - {map.Name}";
                AnnounceLocation(true, ValidateMapObjects(map));
            }
            catch (Exception ex)
            {
                lblStatus.Speak($"讀取地圖 JSON 失敗：{ex.Message}");
            }
        }

        private void GameForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (map == null) return;

            int newX = playerX;
            int newY = playerY;
            bool moved = false;

            switch (e.KeyCode)
            {
                case Keys.W:
                case Keys.Up:
                    newY--;
                    moved = true;
                    break;
                case Keys.S:
                case Keys.Down:
                    newY++;
                    moved = true;
                    break;
                case Keys.A:
                case Keys.Left:
                    newX--;
                    moved = true;
                    break;
                case Keys.D:
                case Keys.Right:
                    newX++;
                    moved = true;
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
                    this.Close();
                    return;
                default:
                    return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (newX < 0 || newX >= map.Width || newY < 0 || newY >= map.Height)
            {
                lblStatus.Speak("撞到邊界了，走不出去！");
                return;
            }

            if (wallSet.Contains($"{newX},{newY}"))
            {
                lblStatus.Speak("前面有障礙物，換個方向吧。");
                return;
            }

            playerX = newX;
            playerY = newY;
            AnnounceLocation(moved);
        }

        // 防呆檢查：地點／事件不可以踩在牆上，否則玩家永遠走不到（例如牢籠被牆擋住的烏龍）
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

        private void AnnounceLocation(bool includeDescription, string warning = "")
        {
            if (map == null) return;

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

            lblStatus.Speak(message + warning);
        }

        private void EnterStoryFromCurrentEvent()
        {
            if (map == null) return;

            var mapEvent = map.Events.Find(ev => ev.X == playerX && ev.Y == playerY);
            if (mapEvent == null)
            {
                lblStatus.Speak("這裡沒有可進入的劇情。");
                return;
            }

            StoryForm story = new StoryForm(mapEvent.Chapter);
            this.Hide();
            story.FormClosed += (s, args) =>
            {
                this.Show();
                lblStatus.Focus();
            };
            story.Show();
        }

        private void TriggerEvent()
        {
            if (map == null) return;

            var mapEvent = map.Events.Find(ev => ev.X == playerX && ev.Y == playerY);
            if (mapEvent == null)
            {
                lblStatus.Speak("這裡沒有可觸發的事件。");
                return;
            }

            lblStatus.Speak($"{mapEvent.Hint}（劇情章節：{mapEvent.Chapter}）");
        }
    }
}
