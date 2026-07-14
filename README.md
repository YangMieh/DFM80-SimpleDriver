# DFM80 簡易驅動

A lightweight desktop driver for the **darkFlash DFM80** series mouse (also sold as **aigo GM80** in China). Single-file `.exe`, no installer, nothing phoning home.

**Version: v1.0.0**

## Why

The official driver is bloated and hides the mouse's most useful switches. This tool exposes everything stored in the mouse's onboard memory, plus a **wake-on-move** feature the official driver never offered:

- **Wake on move** — when idle the mouse throttles down to save power, and any input instantly restores your polling rate. No more waking a sleeping mouse with a click that accidentally registers.
- DPI: 1–6 stages (1/2/3/6, cleanly divides the firmware's fixed 6-slot cycle), per-stage DPI, active stage
- Polling rate: 125 / 250 / 500 / 1000 Hz
- E-sports mode, lift-off distance, button debounce
- Battery level, firmware version, config export/import, factory reset
- Light / dark / follow-system theme; Traditional Chinese / English / Simplified Chinese

Settings are written to the mouse itself, so they persist across computers and connection modes.

## How to use

Download the single `.exe` from Releases, double-click it — no installation. Connect the mouse via **USB cable or the 2.4G dongle** (Bluetooth mode cannot be configured), click **Connect**, adjust, then **Apply**.

Turning on **Wake on move** also enables "minimize to tray on close" so the background power-saving keeps running.

## How it works

The protocol was reverse-engineered from darkFlash's own web driver. The app is a thin C# shell hosting a local page over WebView2 (so WebHID works); "wake on move" is done in the background by monitoring system idle time and adjusting the mouse's polling rate. Full protocol notes are in [PROTOCOL.md](PROTOCOL.md).

## License

**Source-available, not open for modification.** You may download, use and redistribute this software verbatim for free, but **modifying, commercial use, and reverse engineering are not permitted**. See [LICENSE.txt](LICENSE.txt).

<!-- -->

# DFM80 簡易驅動（中文說明）

**darkFlash DFM80** 系列滑鼠（大陸版即 **aigo 游龍 GM80**）的輕量桌面驅動。單一 `.exe`、免安裝、不連任何伺服器。

**版本：v1.0.0**

## 為什麼做這個

官方驅動又肥又把最實用的開關藏起來。本工具把存在滑鼠機身記憶體裡的設定全部開放，還多做了一個**官方沒有的移動喚醒**：

- **移動喚醒** — 閒置時滑鼠自動降頻省電，一有操作立即恢復你的輪詢率。再也不用「點一下」叫醒睡著的滑鼠、也不會被那一下點擊誤觸。
- DPI：1～6 檔位（開放 1/2/3/6，能整除韌體固定的 6 格循環）、各檔 DPI、目前檔位
- 輪詢率：125 / 250 / 500 / 1000 Hz
- 電競模式、LOD 靜默高度、按鍵去抖動
- 電量、韌體版本、設定匯出／匯入、還原原廠
- 亮／深／跟隨系統主題；繁中／English／簡中

設定寫入滑鼠本體，換電腦、換連線模式都持續生效。

## 使用方式

從 Releases 下載單一 `.exe`，雙擊即用、免安裝。以 **USB 線或 2.4G 接收器**連接滑鼠（藍牙模式無法設定），按「連接滑鼠」選擇裝置，調整後按「套用」。

開啟「移動喚醒」時會一併開啟「關閉時最小化到系統匣」，讓背景省電持續運作。

## 原理

協定逆向自 darkFlash 官方網頁驅動。本程式是一層輕量 C# 殼，用 WebView2 載入本機頁面（讓 WebHID 可用）；「移動喚醒」由背景監測系統閒置時間、調整滑鼠輪詢率達成。完整協定筆記見 [PROTOCOL.md](PROTOCOL.md)。

## 授權

**原始碼公開，但不開放修改。** 可免費下載、使用、原封轉載本軟體，但**禁止修改、商業使用與逆向工程**。詳見 [LICENSE.txt](LICENSE.txt)。

作者：[小咩（YangMieh）](https://github.com/YangMieh)
