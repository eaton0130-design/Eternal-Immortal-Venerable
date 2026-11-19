using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AccessibleGridGame
{
    /// <summary>
    /// 主選單畫面。它不是視窗，而是被嵌在唯一 Form 裡的 UserControl。
    /// </summary>
    public class MenuView : UserControl
    {
        private readonly Button btnStart;
        private readonly Button btnExit;

        public event Action? StartRequested;
        public event Action? ExitRequested;

        public MenuView()
        {
            this.BackColor = SystemColors.Control;

            Label lblTitle = new Label();
            lblTitle.Text = "八荒大陸";
            lblTitle.Font = new Font("微軟正黑體", 18, FontStyle.Bold);
            lblTitle.Location = new Point(115, 25);
            lblTitle.AutoSize = true;
            lblTitle.TabIndex = 0;

            btnStart = new Button();
            btnStart.Text = "開始遊戲(&S)";
            btnStart.Location = new Point(80, 85);
            btnStart.Size = new Size(180, 42);
            btnStart.Font = new Font("微軟正黑體", 12);
            btnStart.TabIndex = 1;
            btnStart.AccessibleName = "開始遊戲";
            btnStart.AccessibleDescription = "按下 Enter 開始播放第一段劇情";
            btnStart.GotFocus += BtnStart_GotFocus;
            btnStart.Click += (s, e) => StartRequested?.Invoke();

            btnExit = new Button();
            btnExit.Text = "結束遊戲(&Q)";
            btnExit.Location = new Point(80, 145);
            btnExit.Size = new Size(180, 42);
            btnExit.Font = new Font("微軟正黑體", 12);
            btnExit.TabIndex = 2;
            btnExit.AccessibleName = "結束遊戲";
            btnExit.AccessibleDescription = "按下 Enter 關閉並離開遊戲";
            btnExit.GotFocus += BtnExit_GotFocus;
            btnExit.Click += (s, e) =>
            {
                ScreenReader.Speak("結束遊戲。");
                ExitRequested?.Invoke();
            };

            this.Controls.Add(lblTitle);
            this.Controls.Add(btnStart);
            this.Controls.Add(btnExit);
        }

        public void FocusStart()
        {
            btnStart.Focus();
        }

        /// <summary>由唯一的 Form 統一轉送鍵盤事件。</summary>
        public void HandleKey(KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)
            {
                return;
            }

            // 只有兩個按鈕，上下鍵互換即可；Enter 留給按鈕自己觸發 Click。
            e.Handled = true;
            e.SuppressKeyPress = true;
            Button target = btnStart.Focused ? btnExit : btnStart;
            target.Focus();
        }

        private void BtnStart_GotFocus(object? sender, EventArgs e)
        {
            ScreenReader.Speak("開始遊戲。按下 Enter 開始播放第一段劇情");
        }

        private void BtnExit_GotFocus(object? sender, EventArgs e)
        {
            ScreenReader.Speak("結束遊戲。按下 Enter 關閉並離開遊戲");
        }
    }

    /// <summary>
    /// 唯一的視窗：主選單、劇情、地圖、遊戲選單、角色資訊都在這裡面無縫切換，不另開任何視窗。
    /// 標題固定為「八荒大陸」，不透露目前是哪一種畫面。
    /// </summary>
    public class MainMenuForm : Form
    {
        private readonly MenuView menuView;
        private readonly StoryView storyView;
        private readonly MapView mapView;
        private readonly GameMenuView gameMenuView;
        private readonly CharacterView characterView;
        private readonly List<UserControl> allViews;
        private UserControl? currentView;

        // 劇情結束後要回主選單還是回地圖
        private bool storyReturnToMap;

        public MainMenuForm()
        {
            this.Text = "八荒大陸";
            this.Size = new Size(560, 300);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;

            menuView = new MenuView();
            storyView = new StoryView();
            mapView = new MapView();
            gameMenuView = new GameMenuView();
            characterView = new CharacterView();

            allViews = new List<UserControl> { menuView, storyView, mapView, gameMenuView, characterView };

            menuView.StartRequested += () => StartStory("第一段", false);
            menuView.ExitRequested += () => Application.Exit();

            storyView.Completed += OnStoryCompleted;

            mapView.ExitRequested += ShowMenu;
            mapView.StoryRequested += chapter => StartStory(chapter, true);
            mapView.MenuRequested += ShowGameMenu;

            gameMenuView.ItemSelected += OnGameMenuItemSelected;
            gameMenuView.BackRequested += ReturnToMap;
            characterView.BackRequested += ShowGameMenu;

            foreach (UserControl view in allViews)
            {
                view.Dock = DockStyle.Fill;
                view.Visible = false;
                this.Controls.Add(view);
            }

            ShowMenu();
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // 只有目前畫面會收到鍵盤，所以 Player 感覺不到畫面切換。
            if (currentView == menuView)
            {
                menuView.HandleKey(e);
            }
            else if (currentView == storyView)
            {
                storyView.HandleKey(e);
            }
            else if (currentView == mapView)
            {
                mapView.HandleKey(e);
            }
            else if (currentView == gameMenuView)
            {
                gameMenuView.HandleKey(e);
            }
            else if (currentView == characterView)
            {
                characterView.HandleKey(e);
            }
        }

        private void SwitchTo(UserControl view)
        {
            foreach (UserControl item in allViews)
            {
                item.Visible = item == view;
            }

            currentView = view;
            view.BringToFront();
        }

        private void ShowMenu()
        {
            SwitchTo(menuView);
            menuView.FocusStart();
        }

        private void StartStory(string chapter, bool returnToMap)
        {
            storyReturnToMap = returnToMap;
            SwitchTo(storyView);
            storyView.Play(chapter);
        }

        private void OnStoryCompleted(TeleportRequest? teleport)
        {
            if (teleport != null)
            {
                // 劇情尾端的傳送指令：直接把玩家放進指定地圖
                mapView.EnterMap(teleport.Map, teleport.X, teleport.Y, teleport.Announcement);
                SwitchTo(mapView);
                mapView.Activate();
            }
            else if (storyReturnToMap)
            {
                // 從地圖內的事件進入劇情，播完就回原來的地圖
                SwitchTo(mapView);
                mapView.Activate();
            }
            else
            {
                ShowMenu();
            }
        }

        private void ShowGameMenu()
        {
            SwitchTo(gameMenuView);
            gameMenuView.FocusFirst();
        }

        private void OnGameMenuItemSelected(int index)
        {
            if (index == 0)
            {
                SwitchTo(characterView);
                characterView.Activate();
            }
            else
            {
                // 背包、裝備、技能先占位，等以後擴充
                gameMenuView.ShowNotImplemented(index);
            }
        }

        private void ReturnToMap()
        {
            SwitchTo(mapView);
            mapView.Activate();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ScreenReader.Initialize();
            try
            {
                Application.Run(new MainMenuForm());
            }
            finally
            {
                ScreenReader.Shutdown();
            }
        }
    }
}
