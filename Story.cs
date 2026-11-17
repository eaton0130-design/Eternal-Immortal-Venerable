using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace AccessibleGridGame
{
    // 支援即時語音播報的無障礙劇情顯示元件
    public class StoryLiveLabel : Label
    {
        public StoryLiveLabel()
        {
            // 設定為 Assertive，每次按 Enter 換句時，NVDA / 朗讀程式會立刻朗讀新句子
            this.LiveSetting = AutomationLiveSetting.Assertive;
        }

        public void Speak(string text)
        {
            this.Text = text;
            this.AccessibleName = text;
            this.AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        }
    }

    public class StoryForm : Form
    {
        // 【靜態記錄表】只要遊戲不關閉重開，播放過的劇情段落名稱都會被記在這裡
        private static HashSet<string> playedChapters = new HashSet<string>();

        private List<StoryEntry> currentEntries = new List<StoryEntry>();
        private int currentIndex = 0;
        private string chapterName;
        private StoryLiveLabel lblDialogue;
        private TeleportRequest? pendingTeleport;

        public StoryForm(string targetChapter)
        {
            this.chapterName = targetChapter;

            // 1. 視窗基本設定
            this.Text = $"劇情模式 - {chapterName}";
            this.Size = new Size(520, 260);
            this.StartPosition = FormStartPosition.CenterScreen;

            // 讓視窗優先攔截鍵盤按鍵（確保隨時按 Enter 都能推進劇情）
            this.KeyPreview = true;
            this.KeyDown += StoryForm_KeyDown;

            // 2. 劇情文字顯示區
            lblDialogue = new StoryLiveLabel();
            lblDialogue.Location = new Point(30, 35);
            lblDialogue.Size = new Size(440, 130);
            lblDialogue.Font = new Font("微軟正黑體", 13, FontStyle.Bold);
            lblDialogue.TabIndex = 0;
            lblDialogue.TabStop = true;
            lblDialogue.AccessibleName = "劇情對話";
            lblDialogue.AccessibleDescription = "顯示目前劇情句子，按 Enter 換下一句，按 R 重聽";

            // 3. 底部按鍵操作提示
            Label lblHint = new Label();
            lblHint.Text = "操作提示：按 Enter 鍵繼續下一句（按 R 鍵重聽本句）";
            lblHint.Location = new Point(30, 175);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;

            this.Controls.Add(lblDialogue);
            this.Controls.Add(lblHint);

            // 視窗顯示後開始載入並播放劇情
            this.Shown += (s, e) => LoadAndStartStory();
        }

        private void LoadAndStartStory()
        {
            // 1. 檢查這段劇情是否已經播放過（本次執行期間不重複觸發）
            if (playedChapters.Contains(chapterName))
            {
                MessageBox.Show($"「{chapterName}」已經體驗過了，無法再次觸發（重開遊戲後重置）。", "劇情提示");
                this.Close();
                return;
            }

            // 2. 讀取 story.json 檔案
            string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "story.json");
            if (!File.Exists(jsonPath))
            {
                // 若在 VS Code 開發模式執行，嘗試往專案根目錄尋找
                jsonPath = "story.json";
            }

            if (!File.Exists(jsonPath))
            {
                MessageBox.Show("找不到 story.json 劇情檔案！請確認檔案放在程式同目錄下。", "錯誤");
                this.Close();
                return;
            }

            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                using var document = JsonDocument.Parse(jsonContent);

                if (document.RootElement.TryGetProperty(chapterName, out var chapterElement)
                    && chapterElement.ValueKind == JsonValueKind.Array)
                {
                    currentEntries = TeleportEngine.ParseEntries(chapterElement);

                    // 從結尾找出傳送指令（可在章節最後加入 { "teleport": {...} }）
                    pendingTeleport = currentEntries.FindLast(e => e.IsTeleport)?.Teleport;

                    // 只保留文字句子用來播放
                    currentEntries = currentEntries.FindAll(e => !e.IsTeleport);

                    if (currentEntries.Count > 0)
                    {
                        currentIndex = 0;
                        lblDialogue.Speak(currentEntries[0].Text ?? "");
                    }
                    else if (pendingTeleport != null)
                    {
                        // 章節只有傳送指令、沒有文字：直接傳送
                        ExecuteTeleport();
                    }
                    else
                    {
                        // 章節尚未撰寫（例如先留空佔位的「第二段」）：安全關閉，不當作錯誤
                        lblDialogue.Speak($"「{chapterName}」劇情尚未撰寫。");
                        MessageBox.Show($"「{chapterName}」劇情尚未撰寫，敬請期待！", "劇情提示");
                        this.Close();
                    }
                }
                else
                {
                    MessageBox.Show($"在 story.json 中找不到「{chapterName}」的劇情資料。", "錯誤");
                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"讀取劇情 JSON 失敗：{ex.Message}", "格式錯誤");
                this.Close();
            }
        }

        private void StoryForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                currentIndex++;

                // 檢查是否還有下一句
                if (currentIndex < currentEntries.Count)
                {
                    lblDialogue.Speak(currentEntries[currentIndex].Text ?? "");
                }
                else
                {
                    // 本段劇情全部結束：加入「已播放名單」
                    playedChapters.Add(chapterName);
                    lblDialogue.Speak("本段劇情結束。");

                    if (pendingTeleport != null)
                    {
                        // 有傳送指令：開啟地圖並傳送過去
                        ExecuteTeleport();
                    }
                    else
                    {
                        MessageBox.Show($"「{chapterName}」劇情已結束！", "章節完成");
                        this.Close();
                    }
                }
            }
            else if (e.KeyCode == Keys.R)
            {
                // 貼心無障礙功能：按 R 鍵可重聽當前這一句
                if (currentIndex < currentEntries.Count)
                {
                    lblDialogue.Speak(currentEntries[currentIndex].Text ?? "");
                }
            }
        }

        private void ExecuteTeleport()
        {
            if (pendingTeleport == null) return;

            var targetMap = TeleportEngine.FindMap(pendingTeleport.Map);
            if (targetMap == null)
            {
                MessageBox.Show($"找不到傳送目標地圖「{pendingTeleport.Map}」。", "傳送失敗");
                this.Close();
                return;
            }

            if (!TeleportEngine.IsValidDestination(targetMap, pendingTeleport.X, pendingTeleport.Y))
            {
                MessageBox.Show($"傳送座標無效：{pendingTeleport.Map} ({pendingTeleport.X},{pendingTeleport.Y})。", "傳送失敗");
                this.Close();
                return;
            }

            GameForm game = new GameForm();
            game.EnterMap(pendingTeleport.Map, pendingTeleport.X, pendingTeleport.Y, pendingTeleport.Announcement);
            this.Hide();
            game.FormClosed += (s, args) =>
            {
                this.Close();
            };
            game.Show();
        }
    }
}
