using System;
using System.Drawing;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    public class MainMenuForm : Form
    {
        private Button btnStart;
        private Button btnExit;

        public MainMenuForm()
        {
            // 1. 主選單視窗設定
            this.Text = "八荒大陸 - 遊戲主選單";
            this.Size = new Size(360, 260);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 2. 遊戲標題標籤
            Label lblTitle = new Label();
            lblTitle.Text = "八荒大陸";
            lblTitle.Font = new Font("微軟正黑體", 18, FontStyle.Bold);
            lblTitle.Location = new Point(115, 25);
            lblTitle.AutoSize = true;
            lblTitle.TabIndex = 0;

            // 3. 建立「開始遊戲」按鈕（按上下方向鍵或 Tab 可切換，也可按 Alt+S）
            btnStart = new Button();
            btnStart.Text = "開始遊戲(&S)";
            btnStart.Location = new Point(80, 85);
            btnStart.Size = new Size(180, 42);
            btnStart.Font = new Font("微軟正黑體", 12);
            btnStart.TabIndex = 1; // 焦點第 1 順位
            btnStart.AccessibleName = "開始遊戲";
            btnStart.AccessibleDescription = "按下 Enter 開始播放第一段劇情";
            btnStart.Click += BtnStart_Click;

            // 4. 建立「結束遊戲」按鈕（按上下方向鍵或 Tab 可切換，也可按 Alt+Q）
            btnExit = new Button();
            btnExit.Text = "結束遊戲(&Q)";
            btnExit.Location = new Point(80, 145);
            btnExit.Size = new Size(180, 42);
            btnExit.Font = new Font("微軟正黑體", 12);
            btnExit.TabIndex = 2; // 焦點第 2 順位
            btnExit.AccessibleName = "結束遊戲";
            btnExit.AccessibleDescription = "按下 Enter 關閉並離開遊戲";
            btnExit.Click += (s, e) => Application.Exit();

            // 5. 將元件加入視窗
            this.Controls.Add(lblTitle);
            this.Controls.Add(btnStart);
            this.Controls.Add(btnExit);

            // 視窗開啟時，確保焦點預設停在「開始遊戲」按鈕上
            this.Shown += (s, e) => btnStart.Focus();
        }

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            // 隱藏主選單，開啟劇情視窗並指定播放「第一段」
            this.Hide();
            StoryForm story = new StoryForm("第一段");

            // 當劇情播放完畢（或因為已經播過而關閉）時，自動重新顯示主選單並把焦點放回開始按鈕
            story.FormClosed += (s, args) =>
            {
                this.Show();
                btnStart.Focus();
            };

            story.Show();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainMenuForm());
        }
    }
}
