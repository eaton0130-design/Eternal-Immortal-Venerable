using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using NAudio.Wave;

namespace AccessibleGridGame
{
    /// <summary>
    /// 劇情畫面（UserControl）。不再另開視窗，由 MainMenuForm 無縫切換進出。
    /// 沒有配音的句子走 ScreenReader.Speak；有配音的句子先讓 NVDA 閉嘴，再用 NAudio 播 wav。
    /// </summary>
    public class StoryView : UserControl
    {
        // 只要遊戲不關閉重開，播放過的劇情段落名稱都會被記在這裡
        private static readonly HashSet<string> playedChapters = new HashSet<string>();

        private List<StoryEntry> currentEntries = new List<StoryEntry>();
        private int currentIndex;
        private string chapterName = "";
        private readonly Label lblDialogue;
        private readonly Label lblHint;
        private TeleportRequest? pendingTeleport;
        private bool finished;

        // NAudio 播放狀態：等語音講完才接受換句，並用 token 擋掉上一句的遲到事件
        private IWavePlayer? wavePlayer;
        private AudioFileReader? audioReader;
        private bool isPlayingVoice;
        private bool pendingAdvance;
        private int voiceToken;

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
            lblDialogue.AccessibleDescription = "顯示目前劇情句子，按 Enter 換下一句，按 R 用 NVDA 重聽文字，按 V 重聽配音";

            lblHint = new Label();
            SetHint("操作提示：Enter 下一句，R 用 NVDA 重聽文字，V 重聽配音");
            lblHint.Location = new Point(30, 175);
            lblHint.AutoSize = true;
            lblHint.Font = new Font("微軟正黑體", 10);
            lblHint.ForeColor = Color.DimGray;

            this.Controls.Add(lblDialogue);
            this.Controls.Add(lblHint);
        }

        /// <summary>由主 Form 在切換到本畫面後呼叫，開始播放指定章節。</summary>
        public void Play(string targetChapter)
        {
            StopVoice();
            chapterName = targetChapter;
            currentEntries = new List<StoryEntry>();
            currentIndex = 0;
            pendingTeleport = null;
            finished = false;
            SetHint("操作提示：Enter 下一句，R 用 NVDA 重聽文字，V 重聽配音");
            lblDialogue.Focus();
            LoadAndStartStory();
        }

        /// <summary>
        /// 更新畫面文字。有 voice 就播配音（NVDA 先閉嘴，避免兩張嘴打架）；
        /// 沒有 voice 或音效檔出問題時，降級回原本的 NVDA 朗讀。
        /// </summary>
        private void Say(string text, string? voicePath = null)
        {
            lblDialogue.Text = text;
            lblDialogue.AccessibleName = text;

            if (!string.IsNullOrWhiteSpace(voicePath))
            {
                PlayVoice(text, voicePath);
            }
            else
            {
                StopVoice();
                ScreenReader.Speak(text);
            }
        }

        private void SetHint(string text)
        {
            lblHint.Text = text;
            lblHint.AccessibleName = "劇情操作提示";
            lblHint.AccessibleDescription = text;
        }

        /// <summary>停止目前語音並釋放資源，同時讓還在路上的完成事件失效。</summary>
        private void StopVoice()
        {
            voiceToken++;
            isPlayingVoice = false;
            pendingAdvance = false;

            try
            {
                wavePlayer?.Stop();
            }
            catch
            {
                // 音效裝置臨時消失也不該讓劇情崩掉。
            }

            DisposeVoice();
        }

        private void DisposeVoice()
        {
            try
            {
                wavePlayer?.Dispose();
            }
            catch
            {
                // 靜默降級。
            }
            finally
            {
                wavePlayer = null;
            }

            try
            {
                audioReader?.Dispose();
            }
            catch
            {
                // 靜默降級。
            }
            finally
            {
                audioReader = null;
            }
        }

        /// <summary>用 NAudio 播放角色配音；檔案不存在或格式不對時自動改回 NVDA 唸字。</summary>
        private void PlayVoice(string fallbackText, string voicePath)
        {
            StopVoice();
            int token = voiceToken;

            // 玩家選擇「只播配音」：先叫 NVDA 閉嘴，文字仍留在畫面上給低視角玩家看。
            ScreenReader.Stop();

            string fullPath = Path.IsPathRooted(voicePath)
                ? voicePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, voicePath);

            if (!File.Exists(fullPath))
            {
                ScreenReader.Speak(fallbackText);
                SetHint("配音檔還沒準備好，已用 NVDA 朗讀文字；按 V 可再試一次。");
                return;
            }

            try
            {
                audioReader = new AudioFileReader(fullPath);
                wavePlayer = new WaveOut();
                wavePlayer.Init(audioReader);
                wavePlayer.PlaybackStopped += (_, _) => OnPlaybackStopped(token);
                wavePlayer.Play();
                isPlayingVoice = true;
                SetHint("配音播放中，播放完畢後按 Enter 繼續。");
            }
            catch (Exception)
            {
                // 音檔壞了、沒有音效裝置都當成沒配音，劇情照樣能走。
                DisposeVoice();
                ScreenReader.Speak(fallbackText);
                SetHint("配音播放失敗，已用 NVDA 朗讀文字；按 V 可再試一次。");
            }
        }

        /// <summary>語音播完（背景執行緒）後，回到 UI 執行緒處理排隊的換句。</summary>
        private void OnPlaybackStopped(int token)
        {
            if (token != voiceToken || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => OnVoiceFinishedOnUiThread(token)));
            }
            else
            {
                OnVoiceFinishedOnUiThread(token);
            }
        }

        private void OnVoiceFinishedOnUiThread(int token)
        {
            if (token != voiceToken || finished)
            {
                return;
            }

            isPlayingVoice = false;
            DisposeVoice();

            if (pendingAdvance)
            {
                // 玩家在語音播放中按過 Enter：等它講完再自動換句，不漏聽也不卡住。
                pendingAdvance = false;
                SetHint("操作提示：Enter 下一句，R 用 NVDA 重聽文字，V 重聽配音");
                Advance();
            }
            else
            {
                SetHint("配音播放完畢：Enter 下一句，R 用 NVDA 重聽文字，V 重聽配音。");
            }
        }

        private void Complete()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            StopVoice();
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
                        Say(currentEntries[0].Text ?? "", currentEntries[0].Voice);
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

        private void Advance()
        {
            currentIndex++;

            if (currentIndex < currentEntries.Count)
            {
                Say(currentEntries[currentIndex].Text ?? "", currentEntries[currentIndex].Voice);
            }
            else
            {
                playedChapters.Add(chapterName);
                Say("本段劇情結束。");
                Complete();
            }
        }

        /// <summary>R 鍵：中斷配音，改由 NVDA 把目前句子念一次（沒配音的句子也適用）。</summary>
        private void ReplayText()
        {
            if (currentIndex >= currentEntries.Count)
            {
                ScreenReader.Speak("已經是最後一句，按 Enter 結束本段劇情。");
                return;
            }

            StopVoice();
            string text = currentEntries[currentIndex].Text ?? "";
            lblDialogue.Text = text;
            lblDialogue.AccessibleName = text;
            ScreenReader.Speak(text);
            SetHint("已用 NVDA 重聽文字；按 V 重聽配音，按 Enter 繼續下一句。");
        }

        /// <summary>V 鍵：重播目前句子的配音；沒有配音或檔案失效時自動降級回 NVDA 朗讀。</summary>
        private void ReplayVoice()
        {
            if (currentIndex >= currentEntries.Count)
            {
                ScreenReader.Speak("已經是最後一句，按 Enter 結束本段劇情。");
                return;
            }

            StoryEntry entry = currentEntries[currentIndex];
            string text = entry.Text ?? "";
            lblDialogue.Text = text;
            lblDialogue.AccessibleName = text;

            if (string.IsNullOrWhiteSpace(entry.Voice))
            {
                StopVoice();
                ScreenReader.Speak(text);
                SetHint("這句還沒有配音，已用 NVDA 朗讀文字；按 R 可再聽一次。");
                return;
            }

            PlayVoice(text, entry.Voice);
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

                if (isPlayingVoice)
                {
                    // 語音還在講：先排隊，播完自動換句。玩家按了有反應，才不會以為當機。
                    pendingAdvance = true;
                    SetHint("配音播放中，結束後將自動換下一句，請稍候…");
                    return;
                }

                Advance();
            }
            else if (e.KeyCode == Keys.R)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (isPlayingVoice)
                {
                    // 配音還沒講完，不讓玩家插隊中斷；用 NVDA 回一句吐槽，按幾次就嗆幾次。
                    SetHint("系統提示：播放配音呢，你吵什麼吵，等配音播完再按。");
                    ScreenReader.Speak("系統提示：播放配音呢，你吵什麼吵，等配音播完再按。");
                    return;
                }

                ReplayText();
            }
            else if (e.KeyCode == Keys.V)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                if (isPlayingVoice)
                {
                    SetHint("系統提示：播放配音呢，你要叫它重播你等它播完在按。");
                    ScreenReader.Speak("系統提示：播放配音呢，你要叫它重播你等它播完在按。");
                    return;
                }

                ReplayVoice();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopVoice();
            }

            base.Dispose(disposing);
        }
    }
}
