# AGENTS.md - 《八荒大陸》無障礙文字冒險 RPG 開發指南

本專案是一套專為視障者（盲人玩家）與無障礙體驗設計的 Windows 桌面文字冒險與地圖探索遊戲，採用 **C# (.NET 10 / Windows Forms)** 開發，並以 **VS Code** 作為主要開發環境。

所有 AI 協作代理（Agents）在修改、重構或新增任何程式碼前，**必須嚴格遵守以下核心規範與架構原則**。

---

## 1. 核心無障礙設計原則（最高優先級，不可違反）

本遊戲的核心目標是讓玩家**完全不依賴滑鼠與視覺畫面**，僅透過鍵盤與螢幕報讀軟體（如 **NVDA**、**Windows 內建朗讀程式**）即可流暢遊玩：

1. **全鍵盤操作（Keyboard-First）**：
   - 所有視窗必須設定 `this.KeyPreview = true;`，確保鍵盤事件（`W/A/S/D`、方向鍵、`Enter`、`Space`、`Esc`、`R`、`C`）能被正確攔截與處理。
   - 所有按鈕與互動元件必須明確設定 `TabIndex` 順序，並提供 `Alt + 快捷鍵`（例如 `"開始遊戲(&S)"`）。
   - 處理完自訂按鍵事件後，務必加上 `e.Handled = true;` 與 `e.SuppressKeyPress = true;`，避免觸發 Windows 預設的系統警告音效。
2. **原生控制項與無障礙屬性**：
   - 嚴禁使用純自繪畫布（如 PictureBox 繪圖、Pygame 式畫布）呈現核心資訊，必須使用標準 WinForms 原生元件（`Form`、`Button`、`Label`、`TextBox`）。
   - 每個互動元件都必須填寫清晰的 `AccessibleName` 與 `AccessibleDescription`。
3. **即時語音播報（Live Region）**：
   - 任何動態變化的遊戲狀態（如移動後的新座標、撞牆警告、劇情推進的新句子），都必須透過自訂的 `LiveAnnouncementLabel` / `StoryLiveLabel` 輸出。
   - 該元件必須設定 `LiveSetting = AutomationLiveSetting.Assertive`，並在更新文字時同步觸發 `AccessibilityNotifyClients(AccessibleEvents.NameChange, -1)` 與 `AccessibleEvents.LiveRegionChanged`，確保 NVDA 與朗讀程式能第一時間自動朗讀。
4. **防誤觸與重聽機制**：
   - 劇情模式需提供重聽按鍵（`R` 鍵）；探索地圖需提供當前座標查詢鍵（`C` 鍵）。
   - 切換視窗（如從主選單進入劇情，或從地圖進入劇情）時，必須先隱藏原視窗（`this.Hide()`），並在子視窗關閉事件（`FormClosed`）中恢復顯示與焦點（`.Focus()`）。

---

## 2. 專案檔案架構與職責分工

本專案採用「多檔案模組化」設計，所有 `.cs` 檔案皆位於相同的命名空間 **`namespace AccessibleGridGame`** 下：

- **`Program.cs`**：程式唯一進入點（`Main()`）與遊戲主選單（`MainMenuForm`），提供上下方向鍵切換「開始遊戲」與「結束遊戲」。
- **`Story.cs`**：無障礙劇情播放器（`StoryForm`），負責讀取外部 JSON 劇情、按 `Enter` 逐句推進，並透過靜態集合 `playedChapters` 防止同一執行階段重複觸發已播放章節。
- **`GameForm.cs`**：地圖探索視窗（`GameForm`），處理 `WASD` / 方向鍵移動、邊界碰撞檢測、座標報讀與事件觸發點檢查。
- **`story.json`**：純文字劇情資料庫（UTF-8 編碼）。**禁止將大量劇情文本硬寫（Hardcode）在 C# 程式碼中**。
- **`map.json`**：存放所有地圖資料，方便gameform.cs讀取
- **`*.csproj`**：專案組態檔（目標框架為 `net10.0-windows`，啟用 `<UseWindowsForms>true</UseWindowsForms>` 與 `<Nullable>enable</Nullable>`）。
請注意，有些檔案可能不存在此自行建立
---

## 3. 資料格式規範（JSON 驅動）

- **`story.json` 結構**：
  以「章節名稱」作為 Key，對應的「對話字串陣列」作為 Value：
  ```json
  {
    "第一段": [
      "八荒大陸，一片被荒野吞噬的世界。",
      "八塊荒地環繞四方，荒野之中妖怪橫行，時不時便衝入人族領地燒殺擄掠。"
    ]
  }
  ```
- **檔案複製規則**：新增任何外部資料檔（如 `story.json` 或未來的 `.wav`/`.mp3` 音效檔）時，必須提醒或協助在 `.csproj` 中加入 `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`，確保執行 `dotnet publish` 打包成單一 `.exe` 時檔案不會遺失。

---

## 4. 程式碼風格與編譯要求

- **零警告原則**：專案已啟用 `<Nullable>enable</Nullable>`，所有事件處理函式的 `sender` 參數必須明確標註為可為 Null（例如 `object? sender`），避免產生 `CS8622` 編譯器警告。
- **輕量化依賴**：優先使用 .NET 10 內建標準函式庫（如 `System.Text.Json`、`System.Media`）。若未來引入音訊功能（如多音軌或左右聲道定位），優先選用 **`NAudio`** 套件。
- **不依賴視覺化設計器**：由於開發環境為 VS Code，所有 UI 元件皆透過純 C# 程式碼在建構子中初始化排版，**請勿生成 `.Designer.cs` 或 `.resx` 檔案**。

---

## 5. 常用開發與打包指令（供 AI 執行終端機驗證使用）

- **建置與執行測試**：`dotnet run`
- **單一執行檔打包（Windows 64-bit）**：
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`


---


## 6. ai性格設定

請用幽默搞笑吐槽的方式跟我討論，並且不失專業，做每件事情的時候都要詢問我的意見，而且要很活潑。