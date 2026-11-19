using System;
using System.Drawing;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    /// <summary>
    /// 地圖內按 F1 叫出的遊戲選單（UserControl）。
    /// 上下鍵切換、Enter 進入、Esc 回地圖；行為跟主選單一致，不另開視窗。
    /// </summary>
    public class GameMenuView : UserControl
    {
        private readonly Button[] buttons;
        private readonly Label lblNotice;
        private int focusedIndex;

        /// <summary>0=角色資訊，1=背包，2=裝備，3=技能。</summary>
        public event Action<int>? ItemSelected;
        public event Action? BackRequested;

        public GameMenuView()
        {
            this.BackColor = SystemColors.Control;

            Label lblTitle = new Label();
            lblTitle.Text = "遊戲選單";
            lblTitle.Font = new Font("微軟正黑體", 18, FontStyle.Bold);
            lblTitle.Location = new Point(30, 20);
            lblTitle.AutoSize = true;
            lblTitle.TabIndex = 0;
            this.Controls.Add(lblTitle);

            string[] labels = { "角色資訊(&C)", "背包(&B)", "裝備(&E)", "技能(&S)" };
            string[] accessibleNames = { "角色資訊", "背包", "裝備", "技能" };
            string[] descriptions =
            {
                "按下 Enter 查看葉凌天的姓名、境界、等級與各項數值",
                "按下 Enter 查看背包（尚未開放）",
                "按下 Enter 查看裝備（尚未開放）",
                "按下 Enter 查看技能（尚未開放）"
            };

            buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                Button btn = new Button();
                btn.Text = labels[i];
                btn.Location = new Point(60, 70 + i * 45);
                btn.Size = new Size(180, 38);
                btn.Font = new Font("微軟正黑體", 12);
                btn.TabIndex = i + 1;
                btn.AccessibleName = accessibleNames[i];
                btn.AccessibleDescription = descriptions[i];
                btn.GotFocus += (_, _) =>
                {
                    focusedIndex = index;
                    ScreenReader.Speak($"{accessibleNames[index]}。{descriptions[index]}");
                };
                btn.Click += (_, _) => ItemSelected?.Invoke(index);

                buttons[i] = btn;
                this.Controls.Add(btn);
            }

            lblNotice = new Label();
            lblNotice.Text = "";
            lblNotice.Location = new Point(30, 255);
            lblNotice.MaximumSize = new Size(500, 0);
            lblNotice.AutoSize = true;
            lblNotice.Font = new Font("微軟正黑體", 10);
            lblNotice.ForeColor = Color.DimGray;
            lblNotice.AccessibleName = "選單提示";
            lblNotice.AccessibleDescription = "顯示功能是否開放";
            this.Controls.Add(lblNotice);
        }

        public void FocusFirst()
        {
            focusedIndex = 0;
            lblNotice.Text = "";
            buttons[0].Focus();
        }

        /// <summary>背包／裝備／技能尚未擴充，先給玩家一個明確回饋。</summary>
        public void ShowNotImplemented(int index)
        {
            string name = buttons[index].AccessibleName ?? "該功能";
            string message = $"{name}功能尚未開放，敬請期待。";
            lblNotice.Text = message;
            lblNotice.AccessibleName = message;
            ScreenReader.Speak(message);
        }

        /// <summary>由唯一的 Form 統一轉送鍵盤事件。</summary>
        public void HandleKey(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                int delta = e.KeyCode == Keys.Up ? -1 : 1;
                focusedIndex = (focusedIndex + delta + buttons.Length) % buttons.Length;
                buttons[focusedIndex].Focus();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ScreenReader.Speak("返回地圖。");
                BackRequested?.Invoke();
            }
            // Enter 留給按鈕自己觸發 Click，跟主選單行為一致。
        }
    }
}
