# BreathM Scripts - 呼吸引導系統

## 專案概述

這是一個整合 Meta Quest Pro 情緒檢測與 Raspberry Pi 呼吸引導的互動系統。系統使用臉部表情追蹤來檢測用戶情緒，同時通過壓力感測器監控呼吸模式，並提供觸覺回饋來引導呼吸節奏。Unity 應用程式與 Raspberry Pi 通過 TCP 協議進行實時數據同步。

## 主要功能

### 1. 呼吸引導系統
- **即時呼吸檢測**：使用 BMP280 壓力感測器監控呼吸壓力變化
- **動態引導**：根據情緒狀態調整呼吸節奏（快樂時放鬆，憤怒時平靜）
- **觸覺回饋**：線性致動器提供物理回饋，模擬呼吸節奏
- **自適應過濾**：RealTimeFilter 類別實現低通濾波和平滑處理

### 2. 情緒檢測系統
- **臉部表情分析**：利用 Meta Quest Pro 的 OVRFaceExpressions 進行即時臉部追蹤
- **多情緒識別**：檢測快樂、憤怒、悲傷、驚訝、中性五種情緒
- **動態校正**：自動校正基準值以適應不同用戶
- **遲滯保護**：防止情緒狀態快速切換，提供穩定體驗

### 3. 實時同步通信
- **TCP 客戶端/伺服器**：Unity 與 Raspberry Pi 之間的雙向通信
- **數據同步**：呼吸數據和情緒狀態實時共享
- **控制器輸入**：Quest 控制器 B 鍵切換系統激活狀態

## 關鍵函數說明

### Python 模組 (Raspberry Pi)

#### `rpi_server.py`
- `monitor_process_output(proc, conn)`: 監控子程序輸出，提取 SYNC_ 數據發送到 Unity
- `handle_command(command, conn)`: 處理來自 Unity 的命令（ACTIVATE/DEACTIVATE）
- `client_thread(conn)`: 處理單個客戶端連接的線程
- `main()`: 主函數，啟動 TCP 伺服器並管理子程序

#### `fix_version.py`
- `RealTimeFilter` 類別：
  - `__init__(self, fs, cutoff, order)`: 初始化濾波器參數
  - `filter(self, data)`: 應用低通濾波
  - `reset(self)`: 重置濾波器狀態
- `validate_stable(pressure_data, threshold)`: 驗證壓力數據穩定性
- `move_linear_actuator(distance, direction)`: 控制線性致動器運動
- `guide_breathing_logic(pressure_data, emotion_state)`: 主要的呼吸引導邏輯

#### `self_check.py`
- `self_check_bmp280()`: 檢查 BMP280 感測器連接和讀取
- `setup_motor_gpio()`: 配置馬達 GPIO 引腳
- `test_motor_movement()`: 測試馬達運動功能
- `run_self_check()`: 執行完整自檢程序

### C# 腳本 (Unity)

#### `RpiTCPClient.cs`
- `ConnectToServer()`: 建立與 Raspberry Pi 的 TCP 連接
- `OnActivateButtonClicked()`: 處理激活命令
- `OnDeactivateButtonClicked()`: 處理停用命令
- `SendMessage(string message)`: 發送消息到 Raspberry Pi

#### `EmotionDetector.cs`
- `Start()`: 初始化情緒檢測器，開始校正階段
- `Update()`: 每幀更新情緒計算
- `ReadAndSmoothRaw()`: 讀取並平滑臉部表情原始數據
- `Calibrate()`: 執行校正程序，建立基準值
- `CalculateEmotionV4()`: 核心情緒計算算法
- `GetScore()`: 計算各情緒的分數
- `ApplyHysteresis()`: 應用遲滯邏輯防止狀態抖動
- `UpdateDynamicBaseline()`: 動態更新基準值

#### `EmotionVisualizer.cs`
- `Start()`: 初始化 UI 組件
- `Update()`: 更新情緒顯示界面

## 環境架設

### 硬體需求
- **Raspberry Pi** (建議 4B 或更新版本)
- **BMP280 壓力感測器** (I2C 接口)
- **線性致動器** (12V DC，帶驅動電路)
- **Meta Quest Pro** 頭顯
- **電源供應** (12V 適配器給致動器，5V 給 RPi)
- **連接線** (I2C、GPIO、電源線)

### 軟體環境

#### Raspberry Pi 設定
1. **安裝 Raspberry Pi OS** (64-bit 版本)
2. **更新系統**:
   ```bash
   sudo apt update && sudo apt upgrade
   ```
3. **安裝 Python 3 和必要庫**:
   ```bash
   sudo apt install python3 python3-pip python3-numpy python3-scipy
   pip3 install smbus2 bmp280
   ```
4. **配置 GPIO**:
   ```bash
   sudo apt install python3-rpi.gpio
   ```
5. **啟用 I2C 接口**:
   ```bash
   sudo raspi-config
   # 進入 Interfacing Options > I2C > Enable
   ```

#### Unity 專案設定
1. **安裝 Unity Hub 和 Unity 編輯器** (建議 2021.3+ 版本)
2. **創建新 3D 專案**
3. **安裝 Oculus Integration**:
   - 從 Unity Asset Store 下載並安裝 Oculus Integration
   - 或從 GitHub: https://github.com/oculus-samples/Unity-Integration
4. **配置 XR 設定**:
   - 進入 Edit > Project Settings > XR Plug-in Management
   - 啟用 Oculus
5. **匯入腳本**:
   - 將所有 C# 腳本放入 Assets/Scripts/ 資料夾
   - 將 Python 腳本放入 Assets/StreamingAssets/ (可選，用於參考)

### 網路配置
1. **確保 RPi 和 Quest 在同一網路**
2. **在 RpiTCPClient.cs 中設定正確的 IP 地址**:
   ```csharp
   public string serverIp = "你的RPi IP地址";
   ```
3. **檢查防火牆設定** (如果有問題，允許端口 5005)

### 硬體連接
1. **BMP280 連接**:
   - VCC → 3.3V (RPi pin 1)
   - GND → GND (RPi pin 6)
   - SCL → GPIO 3 (RPi pin 5)
   - SDA → GPIO 2 (RPi pin 3)

2. **線性致動器連接**:
   - 參考 fix_version.py 中的引腳定義 (in1=23, in2=24, en=25)
   - 確保電源供應充足 (12V, 建議 2A 以上)

## 使用方法

### 啟動系統
1. **在 Raspberry Pi 上執行**:
   ```bash
   cd /path/to/breathm_scripts/ToNTUT
   python3 rpi_server.py
   ```

2. **在 Unity 中**:
   - 開啟專案
   - 確保場景包含所有必要的 GameObject (帶有腳本組件)
   - 建置並部署到 Quest Pro

3. **佩戴 Quest Pro**:
   - 啟動應用程式
   - 按 B 鍵激活系統
   - 跟隨觸覺回饋進行呼吸練習

### 調試與監控
- **RPi 控制台**: 查看呼吸數據和系統狀態
- **Unity Debug Log**: 監控情緒檢測和通信狀態
- **自檢程序**: 運行 `python3 self_check.py` 檢查硬體連接

## 檔案結構

```
ToNTUT/
├── rpi_server.py          # TCP 伺服器主程序
├── fix_version.py          # 呼吸引導邏輯
├── self_check.py           # 硬體自檢程序
├── demo_version.py         # 示範版本
├── RpiTCPClient.cs         # Unity TCP 客戶端
├── EmotionDetector.cs      # 情緒檢測器
├── EmotionManager.cs       # 情緒狀態管理
└── EmotionVisualizer.cs    # 情緒 UI 顯示
```

## 注意事項

- 確保所有設備在同一網路環境中
- 首次使用前先執行自檢程序確認硬體正常
- 情緒檢測需要良好的光線條件
- 系統設計用於放鬆和冥想目的，請勿用於醫療用途

## 故障排除

### 常見問題
1. **連接失敗**: 檢查 IP 地址和網路連接
2. **感測器無響應**: 確認 I2C 啟用和接線正確
3. **馬達不動**: 檢查 GPIO 引腳和電源供應
4. **情緒檢測不準確**: 調整校正時間和靈敏度參數

### 日誌位置
- RPi: 控制台輸出
- Unity: Console 視窗或 adb logcat (Android 建置)