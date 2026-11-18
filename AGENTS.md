# AGENTS.md - 《八荒大陸》無障礙文字冒險 RPG 開發指南

本專案是一套專為視障者（盲人玩家）與無障礙體驗設計的 Windows 桌面文字冒險與地圖探索遊戲，採用 **C# (.NET 10 / Windows Forms)** 開發，並以 **VS Code** 作為主要開發環境。

所有 AI 協作代理（Agents）在修改、重構或新增任何程式碼前，**必須嚴格遵守以下核心規範與架構原則**。

---

## 1. 核心無障礙設計原則（最高優先級，不可違反）

本遊戲的核心目標是讓玩家**完全不依賴滑鼠與視覺畫面**，僅透過鍵盤與螢幕報讀軟體（**NVDA**）即可享受毫秒級零延遲的流暢遊玩體驗：

1. **全鍵盤操作（Keyboard-First）**：
   - **全遊戲只有一個 `Form`**，該 Form 必須設定 `this.KeyPreview = true;`，統一攔截鍵盤事件（`W/A/S/D`、方向鍵、`Enter`、`Space`、`Esc`、`R`、`C`）後，依目前畫面分派給對應 `UserControl` 的 `HandleKey(KeyEventArgs e)`。各畫面本身不另設 `KeyPreview`（`UserControl` 也沒有這個屬性）。
   - 所有按鈕與互動元件必須明確設定 `TabIndex` 順序，並提供 `Alt + 快捷鍵`（例如 `"開始遊戲(&S)"`）。
   - 處理完自訂按鍵事件後，務必加上 `e.Handled = true;` 與 `e.SuppressKeyPress = true;`，避免觸發 Windows 預設的系統警告音效。
2. **原生控制項與無障礙屬性**：
   - 使用標準 WinForms 原生元件（唯一的 `Form`、承載畫面的 `UserControl`、`Button`、`Label`、`TextBox`）建構介面，嚴禁使用純自繪畫布（如 PictureBox 繪圖、Pygame 式畫布）呈現核心資訊。
   - 每個互動元件都必須填寫清晰的 `AccessibleName` 與 `AccessibleDescription`。
   - **按鈕焦點語音聯動**：各畫面的按鈕在獲得焦點（`GotFocus` 或 `Enter` 事件）時，必須主動呼叫 `ScreenReader.Speak(...)` 朗讀按鈕名稱與操作說明。
3. **即時語音播報（`prism.dll` + NVDA 單通道引擎）**：
   - **廢除舊版 UIA Live Region 通知**（不再使用 `LiveAnnouncementLabel`、`StoryLiveLabel` 與 `AccessibilityNotifyClients`）。
   - 所有動態變化的遊戲狀態（如移動後的新座標、撞牆警告、劇情推進的新句子），統一透過靜態管理類別 **`ScreenReader.Speak(string text, bool interrupt = true)`** 呼叫底層 `prism.dll`（搭配 `nvdaControllerClient64.dll`）直接驅動 NVDA 朗讀。
   - 僅使用**單一語音通道**（預設 `interrupt = true` 立刻切斷上一句並播報最新訊息），字串傳遞統一採用 UTF-8 編碼（`UnmanagedType.LPUTF8Str`）。
4. **防誤觸與重聽機制**：
   - 劇情畫面需提供重聽按鍵（`R` 鍵）；地圖畫面需提供當前座標查詢鍵（`C` 鍵）；地圖按 `Esc` 返回主選單，不結束程式。
5. **單視窗無縫切換（嚴禁另開視窗）**：
   - 主選單、劇情、地圖都是嵌在唯一 `Form` 裡的 `UserControl`（`MenuView`、`StoryView`、`MapView`）。**禁止**再 `new Form()`、`.Show()`、`.Hide()`，也**禁止**依賴 `FormClosed` 事件做畫面切換。
   - 畫面切換只透過 `Visible` 與 `BringToFront()` 完成，並立刻把焦點移到新畫面的第一個可聚焦控制項。
   - 標題列固定為「八荒大陸」，**不得**出現「劇情模式」、「地圖探索」等透露目前畫面的字樣，讓玩家完全察覺不到切換。
   - 劇情尾端的 `teleport` 指令由主 Form 統一接收並切換到地圖畫面；從地圖進入的劇情播完後回到原來的地圖（保留玩家座標）。

---

## 2. 專案檔案架構與職責分工

本專案採用「多檔案模組化」設計，所有 `.cs` 檔案皆位於相同的命名空間 **`namespace AccessibleGridGame`** 下（請注意：部分檔案若尚未存在，請依規範自動建立）：

- **`Program.cs`**：程式唯一進入點（`Main()`）、唯一容器 `MainMenuForm`（標題固定「八荒大陸」）與主選單畫面 `MenuView`。負責啟動／結束時初始化與釋放 `ScreenReader`、以 `KeyPreview` 統一分派鍵盤事件、用 `Visible` + `BringToFront()` 在三個畫面間無縫切換，並提供上下方向鍵切換「開始遊戲」與「結束遊戲」。
- **`ScreenReader.cs`**：依照 `prism.h` 封裝 `prism.dll` 的 C API（`prism_config_init`、`prism_init`、`prism_registry_create_best`、`prism_backend_initialize`、`prism_backend_speak`、`prism_backend_stop`、`prism_backend_free`、`prism_shutdown`），提供全域靜態方法 `Initialize()`、`Speak()`、`Stop()`、`Shutdown()` 供所有畫面呼叫。注意 `prism_registry_create_best` 取得的後端可能已初始化，`prism_backend_initialize` 回傳 `15`（`PRISM_ERROR_ALREADY_INITIALIZED`）時應視為成功。
- **`Story.cs`**：無障礙劇情畫面（`StoryView`，`UserControl`），負責讀取外部 JSON 劇情、由主 Form 呼叫 `Play(chapter)` 開始播放、按 `Enter` 逐句推進、`R` 重聽，並透過 `ScreenReader.Speak()` 朗讀；以靜態集合 `playedChapters` 防止同一執行階段重複觸發已播放章節；結束時用 `Completed` 事件通知主 Form。
- **`GameForm.cs`**：地圖探索畫面（`MapView`，`UserControl`；檔案名稱延用 `GameForm.cs`），讀取 `map.json`，處理 `WASD` / 方向鍵移動、邊界與障礙物碰撞檢測、座標語音播報與事件觸發點檢查；以 `ExitRequested` / `StoryRequested` 事件通知主 Form，不自行開關視窗。
- **`TeleportEngine.cs`**：劇情項目解析與傳送目的地驗證，供 `StoryView` 與 `MapView` 共用。
- **`story.json`**：純文字劇情資料庫（UTF-8 編碼）。**禁止將大量劇情文本硬寫（Hardcode）在 C# 程式碼中**。
- **`map.json`**：存放所有地圖尺寸、障礙物與劇情觸發座標資料，供 `GameForm.cs`（`MapView`）讀取。
- **`prism.h`**：Prism 官方 C API 標頭檔，作為 P/Invoke 簽章的唯一依據；修改 `ScreenReader.cs` 前請先對照此檔。
- **`*.csproj`**：專案組態檔（目標框架為 `net10.0-windows`，啟用 `<UseWindowsForms>true</UseWindowsForms>` 與 `<Nullable>enable</Nullable>`）。

---

## 3. 資料格式規範（JSON 驅動）

- **`story.json` 結構**：
  以「章節名稱」作為 Key，對應的「對話字串陣列」作為 Value（未來預留擴充 `speaker` 與 `voice` 欄位以支援 Qwen3-TTS 角色配音）：
  ```json
  {
    "第一段": [
      "八荒大陸，一片被荒野吞噬的世界。",
      "八塊荒地環繞四方，荒野之中妖怪橫行，時不時便衝入人族領地燒殺擄掠。"
    ]
  }
  ```
  陣列內可混排傳送指令（通常放在章節最後一筆），由主 Form 接到後切換到地圖畫面：
  ```json
  { "teleport": { "map": "神秘空間", "x": 2, "y": 2, "announcement": "白光一閃，你被傳送到了神秘空間。" } }
  ```
- **外部檔案與 DLL 自動複製規則**：
  專案根目錄下的外部資料檔（`story.json`、`map.json`、未來的 `.wav`/`.mp3` 音效檔）以及原生函式庫（`prism.dll`、`nvdaControllerClient64.dll`），都必須在 `.csproj` 中設定 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`，確保執行 `dotnet run` 或 `dotnet publish` 打包成單一 `.exe` 時檔案不會遺失。

---

## 4. 程式碼風格與編譯要求

- **零警告原則**：專案已啟用 `<Nullable>enable</Nullable>`，所有事件處理函式的 `sender` 參數必須明確標註為可為 Null（例如 `object? sender`），避免產生 `CS8622` 編譯器警告。
- **輕量化依賴**：優先使用 .NET 10 內建標準函式庫（如 `System.Text.Json`、`System.Media`）。若未來引入多音軌背景音樂、左右聲道定位或 Qwen3-TTS 角色配音播放，優先選用 **`NAudio`** 套件（播放配音時需注意呼叫 `ScreenReader.Stop()` 避免與 NVDA 語音重疊）。
- **不依賴視覺化設計器**：由於開發環境為 VS Code，所有 UI 元件皆透過純 C# 程式碼在建構子中初始化排版，**請勿生成 `.Designer.cs` 或 `.resx` 檔案**。

---

## 5. 常用開發與打包指令（供 AI 執行終端機驗證使用）

- **建置與執行測試**：`dotnet run`
- **單一執行檔打包（Windows 64-bit）**：
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

---

## 6. AI 性格設定

請用幽默搞笑吐槽的方式跟我討論，並且不失專業，做每件事情的時候都要詢問我的意見，而且要很活潑。
