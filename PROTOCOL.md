# DFM80 HID 協定文件（逆向自官方網頁驅動）

> 來源：darkFlash 官方網頁驅動 `https://www.darkflash.tw/driver/dfm80/`
> 協定本體 chunk：`_next/static/chunks/515-d6bbe126f1e026cd.js`（美化版存於 `reference/official_webdriver_chunk515_pretty.js`，僅本機參考、不進 git）
> 裝置 profile chunk：`238.b11bfd8c21f8f90b.js`（OEM 內部型號名 "G3 PRO"）
> 逆向日期：2026-07-14

## 1. 裝置與連線

- WebHID 過濾器：`{ usagePage: 0xFF01, usage: 0x10 }`（vendor-defined collection）
- 已知 VendorID：
  - `0xA8A4` (43172) = 有線連接
  - `0xA8A5` (43173) = 2.4G 接收器
  - `0x3837` (14391) = 亦見於過濾清單（滑鼠/鍵盤共用）
- 官方對「滑鼠」的判定：collections 含 GENERIC_DESKTOP/MOUSE，或 vendor-defined collection 且 VID 屬上列
- 多裝置時官方取 `vendorId` 排序後第一個

## 2. 傳輸層

- **送出**：output report，`device.sendReport(0, data)`，`data` 為 64 bytes（程式內慣用 65 元素陣列，`[0]` 是 reportId=0，`slice(1)` 才上線）
- **回應**：下一個 `inputreport` 事件，`new Uint8Array(e.data.buffer)`（**不含 reportId**，索引從 0 起算）
- 官方有指令佇列（一次一發），讀取 race 500ms timeout（逾時回空陣列）
- 所有指令第 1 個 payload byte（送出陣列的 `[1]`）固定 `0x55`

### 非同步事件封包（inputreport 主動上報）

| header | 意義 | 內容 |
|---|---|---|
| `[0xAA, 0xFA]`, `o[8]==16` | 即時狀態 | `dpi檔位 = o[9]-1`、`輪詢率檔 = o[10]-1`（滑鼠上按 DPI 鍵會發） |
| `[0xAA, 0xFA]`, `o[8]==17` | 燈效變更 | `light = o[9]` |
| `[0xAA, 0x30]` | 電量 | `battery% = o[8]`、`charge_flag = o[9]` |
| `[0xAA, 0xED]` | 滑鼠在線狀態 | `mouse_status = o[8]`（經接收器時滑鼠本體是否在線） |

## 3. 指令一覽（送出陣列以 65 元素、含 `[0]`=reportId 表示）

### 3.1 讀設定 cmd 0x0E

送：`[0, 0x55, 0x0E, 1, 11, 48, 0, 0, 0, 0, 0, ...0]`
回應 `t`（inputreport bytes）：

| index | 欄位 | 說明 |
|---|---|---|
| t[9] | light_mode | 燈效模式 |
| t[10] | report_rate | 1-based；DFM80 檔位表 = [125, 500, 1000] Hz（官方 UI 顯示 `t[10]-1` 為索引） |
| t[11] | dpi_count | DPI 檔位數 1–6 |
| t[12] | dpi_index | 目前檔位，1-based |
| t[13..24] | dpi1~6 | 各檔 DPI，16-bit **little-endian**（lo, hi） |
| t[48] | scroll_flag | |
| t[49] | lod_value | LOD 高度 |
| t[50] | sensor_flag | |
| t[51] | key_respond | 按鍵防抖(ms) |
| **t[52]** | **sleep_light** | **休眠時間（分鐘）**，官方預設 3 |
| t[53] | highspeed_mode | |
| **t[55]** | **喚醒/移動燈效** | **低 4 bit = wakeup_flag（喚醒方式）**、高 4 bit = move_light_flag |

- 無效判定：`t[13..15]` 全 0 或全 0xFF → 裝置沒回有效設定，官方套預設值
- 官方預設（DFM80 / G3 PRO profile）：`report_rate=3, dpi_count=6, dpi_index=2(0-based), DPI=[800,1600,2400,3200,5000,12000], lod=1, key_respond=8, sleep_light=3, highspeed=0, wakeup_flag=1, move_light_flag=1`；DPI 範圍 200–12000
- i18n 對照：`wakeUp`=唤醒方式（`moveWakeUp`=移动唤醒 / `clickWakeUp`=单击唤醒）、`sleepSetting`=休眠设置（單位「分钟」，2.4G/藍牙模式下閒置後休眠）
- **wakeup_flag 數值→語意：待真機翻轉實測**（出貨值 1 = 現況「單擊喚醒」的行為）

### 3.2 寫設定 cmd 0x0F

送（65 元素）：
```
[0]=0  [1]=0x55  [2]=0x0F  [3]=174  [4]=10  [5]=47  [6]=1  [7]=1  [8]=1  [9]=0
[10]=light_mode
[11]=report_rate            // 1-based（讀到的 t[10] 原值）
[12]=dpi_count
[13]=dpi_index              // 1-based
[14..25]=dpi1~6 lo,hi       // little-endian
[26..48]=0
[49]=scroll_flag
[50]=lod_value
[51]=sensor_flag
[52]=key_respond
[53]=sleep_light            // 分鐘
[54]=highspeed_mode
[55]=(wakeup_flag<<4)|move_light_flag
```
⚠️ 讀寫 nibble 位置**不對稱是官方原樣**（讀：wakeup 在低 nibble；寫：wakeup 在高 nibble），照抄勿自行「修正」。
⚠️ 寫入陣列含 reportId 偏移，所以欄位 index 比讀取多 1（wire 上其實同位置）。

### 3.3 讀按鍵 cmd 0x08

送：`[0, 0x55, 0x08, 1, 11, 44, 0...]`
回應：`t[0]==0xAA && t[1]==8` 有效；`o = t.slice(8)`，6 顆鍵各 4 bytes：`{type: o[4i], code1: o[4i+1], code2: o[4i+2], code3: o[4i+3]}`

### 3.4 寫按鍵 cmd 0x09

送：`[0, 0x55, 0x09, 165, 34, 44, 0, 0, 0, <鍵0 4bytes>, <鍵1>, ...]`，從 index 9 起每鍵 4 bytes（官方一次寫 5 顆 + 特殊）
- type `32`=滑鼠鍵（code1 bitmask：1=左、2=右、4=中、8=後退、16=前進）
- type `33`=特殊（code1 85=DPI 循環、code1 56 + code2 1/255 = DPI +/−、97=?）
- 恢復預設 = `resetAllMouseKeys`（同 cmd 0x09 寫回預設表，尾端 [53..56]=33,97,0,0）

### 3.5 其他

| cmd | 功能 | 格式 |
|---|---|---|
| 0x03 | 韌體版本 | `[0,0x55,3]` → 版本字元在 t[23],t[24],t[25]（ASCII 數字，組成 x.y.z） |
| 0x30 | 查電量 | `[0,0x55,48,1,11,46,1,1,1,0,0]` → 回應走 `[0xAA,0x30]` 事件（battery=o[8], charge=o[9]） |
| 0xED | 連線狀態 | `[0,0x55,237,1]` → t[8]（滑鼠是否在線） |
| 0x21 | 燈效模式 | `[0,0x55,33,0,0,3,0,0,0,0,0, mode]` |
| 0x0D/0x10/0x06 | 巨集讀寫 | 未用到，見參考檔 |

## 4. 注意事項

- 設定存滑鼠機身（斷電保存），用 2.4G/USB 設定完，藍牙模式照樣生效
- 藍牙模式下 vendor collection 不一定可用 → 一律建議接 2.4G 接收器或 USB 線來設定
- 指令與非同步事件共用 input channel，實作要先過濾事件封包再配對指令回應

## 5. 移動喚醒調查結論（2026-07-14，重要）

**DFM80 韌體不支援「移動喚醒」，這是硬體/韌體層限制，軟體無法達成。** 完整證據鏈：

1. **`wakeup_flag` byte 無效**：t[55] 低 4 bit 是喚醒旗標，但真機實測寫 1 讀回 0（標準寫／nibble 對調／寫 wire55 全試過），韌體收到但不存這個 byte。
2. **電競模式 `highspeed_mode` 無效**：一度以為它能觸發移動喚醒（主控保持活躍），真機多次測試確認休眠後晃動仍不醒。
3. **官方桌面驅動無喚醒欄位**：反編譯 `DeviceDriver.exe`（Ghidra，5065 函式），寫設定完整欄位清單只有 report_rate/dpi_flag/dpi_index/dpi_group*/scroll_flag/liftoff_height/sensor_flag/button_respondtime/sleep_light/e-sports_flag——**完全沒有任何 wake/awake/dormant 欄位**。
4. **跨品牌同平台協定相同**：抓 K-snake X11（完全不同品牌）的 yjx2012 網頁驅動，喚醒寫入 `t[54]=highspeed_mode, t[55]=wakeup_flag<<4|move_light_flag` 跟 DFM80 100% 相同。整個 yjx2012 平台（darkflash / K-snake / aigo 游龙 GM80…）共用同一套協定，無替代喚醒指令。

> aigo 游龙 GM80 = DFM80 大陸版（同源同驅動同協定，非市售可下載的獨立驅動能改變結論）。
> 唯一理論路徑＝改寫韌體（PAW3395 感測器硬體或許支援 rest-mode motion detection），但無原廠燒錄工具＝變磚風險，不採行。

**其他欄位語意修正（真機實測）**：
- `dpi_count`（wire byte 11）韌體固定循環 6 格，寫別的值無效（官方桌面版也固定寫 6）。→ 網頁改用「1236 填充」：只開放 1/2/3/6（整除 6），用重複排列鋪滿 6 格模擬 N 檔循環。
- `report_rate` 韌體值 **1/2/3/4 = 125/250/500/1000 Hz**（共 4 檔；官方網頁驅動漏顯 250Hz 導致 1000 錯位成 500，本專案已修正為 4 檔）。
- 移動喚醒（電競模式）開啟時滑鼠自動以 1000Hz 運作，屬韌體行為，UI 僅「顯示鎖定」不寫 report_rate（強制寫 report_rate 會干擾，已移除）。

## 6. 網址參數（除錯用）

- `?debug=1` 顯示「進階設定」+「診斷」面板（raw config、factory backup、HID hex log）
- `?theme=system|dark|light` 強制主題
- `?demobatt=<0-100>` 模擬電量顯示（截圖/展示用，不影響實機）
