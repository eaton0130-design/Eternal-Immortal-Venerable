using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    /// <summary>
    /// 劇情畫面（UserControl）。不再另開視窗，由 MainMenuForm 無縫切換進出。
    /// 所有劇情文字都走 ScreenReader.Speak，不使用 UIA Live Region。
    /// </summary>
    public class StoryView : UserControl
    {
        // 只要遊戲不關閉重開，播放過的劇情段落名稱都會被記在這裡
        private static readonly HashSet<string> playedChapters = new HashSet<string>();

        private List<StoryEntry> currentEntries = new List<StoryEntry>();
        private int currentIndex;
        private string chapterName = "";
        private readonly Label lblDialogue;
        private TeleportRequest? pendingTeleport;
        private bool finished;

        /// <summary>劇情結束時通知主 Form；有傳送指令時一併帶過去。</summary>
        public event Action<TeleportRequest?>? Completed;

        public StoryView()
        {
            this.BackColor = SystemColors.Control;

            lblDialogue = new Label();
            lblDialogue.Location = new Point(30, 35);
            lblDialogue.Size = new Size(440, 130);
            lblDialogue.Font = new Font("微軟正黑體", 13, FontStyle.Bold);
            lblDialogue.TabIndex = 0;
            lblDialogue.TabStop = true;
            lblDialogue.AccessibleName = "劇情對話";
            lblDialogue.AccessibleDescription = "顯示目前劇情句子，按 Enter 換下一句，按 R 重聽";

            Label lblHint = new Label();
            lblHint.Text = "操作提示：按 Enter 鍵繼續下一句（按 R 鍵重聽本句）";
            lblHint.Location = new Point(30, 175);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;
            lblHint.AccessibleName = "劇情操作提示";
            lblHint.AccessibleDescription = "按 Enter 繼續下一句，按 R 重聽本句";

            this.Controls.Add(lblDialogue);
            this.Controls.Add(lblHint);
        }

        /// <summary>由主 Form 在切換到本畫面後呼叫，開始播放指定章節。</summary>
        public void Play(string targetChapter)
        {
            chapterName = targetChapter;
            currentEntries = new List<StoryEntry>();
            currentIndex = 0;
            pendingTeleport = null;
            finished = false;
            lblDialogue.Focus();
            LoadAndStartStory();
        }

        /// <summary>更新畫面文字，並透過單一語音通道播報。</summary>
        private void Say(string text, bool interrupt = true)
        {
            lblDialogue.Text = text;
            lblDialogue.AccessibleName = text;
            ScreenReader.Speak(text, interrupt);
        }

        private void Complete()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            Completed?.Invoke(pendingTeleport);
        }

        private void LoadAndStartStory()
        {
            // 1. 檢查這段劇情是否已經播放過（本次執行期間不重複觸發）
            if (playedChapters.Contains(chapterName))
            {
                Say($"「{chapterName}」已經體驗過了，無法再次觸發（重開遊戲後重置）。");
                Complete();
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
                Say("找不到 story.json 劇情檔案！請確認檔案放在程式同目錄下。");
                Complete();
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
                        Say(currentEntries[0].Text ?? "");
                    }
                    else if (pendingTeleport != null)
                    {
                        // 章節只有傳送指令、沒有文字：直接通知主 Form 傳送
                        Complete();
                    }
                    else
                    {
                        // 章節尚未撰寫（例如先留空佔位的「第二段」）：安全離開
                        Say($"「{chapterName}」劇情尚未撰寫。");
                        Complete();
                    }
                }
                else
                {
                    Say($"在 story.json 中找不到「{chapterName}」的劇情資料。");
                    Complete();
                }
            }
            catch (Exception ex)
            {
                Say($"讀取劇情 JSON 失敗：{ex.Message}");
                Complete();
            }
        }

        /// <summary>由唯一的 Form 統一轉送鍵盤事件。</summary>
        public void HandleKey(KeyEventArgs e)
        {
            if (finished)
            {
                return;
            }

            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                currentIndex++;

                if (currentIndex < currentEntries.Count)
                {
                    Say(currentEntries[currentIndex].Text ?? "");
                }
                else
                {
                    playedChapters.Add(chapterName);
                    Say("本段劇情結束。");
                    Complete();
                }
            }
            else if (e.KeyCode == Keys.R)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (currentIndex < currentEntries.Count)
                {
                    Say(currentEntries[currentIndex].Text ?? "");
                }
                else
                {
                    Say("已經是最後一句，按 Enter 結束本段劇情。");
                }
            }
        }
    }
}
