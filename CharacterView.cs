using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    /// <summary>
    /// 角色資料模型，對應 character.json。
    /// 以後升級或劇情改數值都改 json，不用動 UI 程式碼。
    /// </summary>
    public class PlayerState
    {
        public string Name { get; set; } = "";
        public string Realm { get; set; } = "";
        public int Level { get; set; }
        public int Hp { get; set; }
        public int Mana { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
    }

    /// <summary>
    /// 角色資訊畫面（UserControl）：上下方向鍵逐欄瀏覽，Enter / Esc 返回選單。
    /// 欄位用 Button 而非 Label：Button 是 NVDA 認得的互動控制項，能取得真正的鍵盤焦點，
    /// 讓 NVDA 進入 focus 模式，方向鍵才會傳進程式（Label 無法做到這點）。
    /// </summary>
    public class CharacterView : UserControl
    {
        private readonly Button[] fieldButtons;
        private readonly string[] fieldLines;
        private readonly Label lblHint;
        private int focusedIndex;

        public event Action? BackRequested;

        public CharacterView()
        {
            this.BackColor = SystemColors.Control;

            PlayerState player = LoadPlayer();

            fieldLines = new[]
            {
                $"姓名：{player.Name}",
                $"境界：{player.Realm}",
                $"等級：{player.Level}",
                $"HP：{player.Hp}",
                $"靈力：{player.Mana}",
                $"攻擊：{player.Attack}",
                $"防禦：{player.Defense}"
            };

            // 用外觀像文字的 Button，保留原生可聚焦能力給 NVDA
            fieldButtons = new Button[fieldLines.Length];
            for (int i = 0; i < fieldLines.Length; i++)
            {
                int index = i;
                Button btn = new Button();
                btn.Text = fieldLines[i];
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;
                btn.Location = new Point(30, 20 + i * 32);
                btn.Size = new Size(420, 28);
                btn.TextAlign = ContentAlignment.MiddleLeft;
                btn.Font = new Font("微軟正黑體", 13, FontStyle.Bold);
                btn.TabIndex = i;
                btn.AccessibleName = fieldLines[i];
                btn.AccessibleDescription = "角色資訊欄位，按上下方向鍵切換，按 Enter 或 Esc 返回選單";
                btn.GotFocus += (_, _) => focusedIndex = index;

                fieldButtons[i] = btn;
                this.Controls.Add(btn);
            }

            lblHint = new Label();
            lblHint.Location = new Point(30, 20 + fieldLines.Length * 32 + 8);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;
            this.Controls.Add(lblHint);
        }

        public void Activate()
        {
            focusedIndex = 0;
            Highlight();
            UpdateHint();
            // 關鍵：把真正的鍵盤焦點放到按鈕上，NVDA 才會進入 focus 模式
            fieldButtons[0].Focus();
            ScreenReader.Speak(fieldLines[0]);
        }

        /// <summary>由唯一的 Form 統一轉送鍵盤事件。</summary>
        public void HandleKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                int delta = e.KeyCode == Keys.Up ? -1 : 1;
                focusedIndex = (focusedIndex + delta + fieldLines.Length) % fieldLines.Length;
                fieldButtons[focusedIndex].Focus();
                Highlight();
                UpdateHint();
                ScreenReader.Speak(fieldLines[focusedIndex]);
            }
            else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ScreenReader.Speak("返回選單。");
                BackRequested?.Invoke();
            }
        }

        /// <summary>目前欄位反白，讓明眼開發者也看得到選到哪一項。</summary>
        private void Highlight()
        {
            for (int i = 0; i < fieldButtons.Length; i++)
            {
                fieldButtons[i].BackColor = i == focusedIndex ? SystemColors.Highlight : SystemColors.Control;
                fieldButtons[i].ForeColor = i == focusedIndex ? SystemColors.HighlightText : SystemColors.ControlText;
            }
        }

        /// <summary>提示列顯示目前欄次，方便老鐵確認按鍵到底有沒有進來。</summary>
        private void UpdateHint()
        {
            lblHint.Text = $"操作提示：上下方向鍵切換欄位（目前第 {focusedIndex + 1} 項／共 {fieldLines.Length} 項），Enter 或 Esc 返回選單";
            lblHint.AccessibleName = lblHint.Text;
            lblHint.AccessibleDescription = "目前欄位與操作說明";
        }

        private static PlayerState LoadPlayer()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "character.json");
            if (!File.Exists(path))
            {
                path = "character.json";
            }

            if (!File.Exists(path))
            {
                return new PlayerState { Name = "未知", Realm = "未知" };
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<PlayerState>(json, TeleportEngine.JsonOptions)
                       ?? new PlayerState { Name = "未知", Realm = "未知" };
            }
            catch
            {
                return new PlayerState { Name = "未知", Realm = "未知" };
            }
        }
    }
}
