# pico-bridge 实施计划

**日期：** 2026-04-22
**仓库：** `pico-bridge`
**参考：** PICO Unity 官方文档、`../XRoboToolkit-Unity-Client`
**目标：** 在 PICO 头显与 PC receiver CLI 之间建立低延迟姿态桥接，并以可验证方式推进 PC 摄像头画面到头显显示。

---

## 1. 关键发现与设计决策

从 XRobo 参考项目中提取的两个核心事实，决定了整体实施策略：

**协议是有方向的。** VR → PC 使用 `HEAD=0x3F`，PC → VR 使用 `HEAD=0xCF`。`PACKET_CCMD_TO_CONTROLLER_FUNCTION` 实际是 `0x6D`（不是 `0x5F`）。`0x5F` 是 PC 发给 VR 的通用 function 命令。

**视频链路不是简单的 UDP 推帧。** `RemoteCameraWindow` 在 Android 侧创建 `MediaDecoder`，调用 `startServer(port, false)` 监听。Unity 通过 TCP function `StartReceivePcCamera` 告知 PC 端 IP/端口/分辨率/帧率/码率，由 PC 端主动推流。`UdpReceiver` 虽能接收 `PackageHandle` UDP 包，但原版并未用它喂视频帧。

**因此分两阶段：**

- **阶段 A** — TCP 姿态桥接（可独立验证）
- **阶段 B** — 视频协议 spike + H.264 推流（依赖 spike 结果）

---

## 2. 当前仓库状态

| 项目 | 状态 |
| --- | --- |
| Unity 版本 | `2022.3.62f3` |
| PICO SDK | `Packages/PICO-Unity-Integration-SDK`，package name `com.unity.xr.picoxr` |
| Live Preview | `Packages/Unity-Live-Preview-Plugin`，package name `com.unity.pico.livepreview` |
| manifest.json | 缺少对两个 embedded package 的显式声明，**需修复** |
| LitJson | 已在 PICO SDK 内，不新增 JSON 依赖 |

---

## 3. 协议规范

### 3.1 包格式

```
[HEAD:1][CMD:1][LEN:4 LE][DATA:N][TIMESTAMP:8 LE][END:1]
```

`DEFAULT_PACKAGE_SIZE = 15`（空包长度），`LEN` = payload 字节数。

### 3.2 方向与常量

| 方向 | HEAD | END |
| --- | --- | --- |
| VR → PC | `0x3F` | `0xA5` |
| PC → VR | `0xCF` | `0xA5` |

| 常量 | 值 | 方向 | 用途 |
| --- | ---: | --- | --- |
| `PACKET_CCMD_CONNECT` | `0x19` | VR→PC | 连接，payload `deviceSN\|-1` |
| `PACKET_CCMD_SEND_VERSION` | `0x6C` | VR→PC | 版本握手 |
| `PACKET_CCMD_TO_CONTROLLER_FUNCTION` | `0x6D` | VR→PC | 姿态 JSON / VR function |
| `PACKET_CCMD_CLIENT_HEARTBEAT` | `0x23` | VR→PC | 心跳 |
| `PACKET_CMD_FROM_CONTROLLER_COMMON_FUNCTION` | `0x5F` | PC→VR | PC function |
| `PACKET_CMD_CUSTOM_TO_VR` | `0x71` | PC→VR | 自定义二进制 |
| `PACKET_CMD_CUSTOM_TO_PC` | `0x72` | VR→PC | 自定义二进制 |

---

## 4. 文件布局

### 4.1 Unity

```
Assets/Scripts/PicoBridge/
├── Network/
│   ├── ByteBuffer.cs
│   ├── NetCMD.cs
│   ├── NetPacket.cs
│   ├── PackageHandle.cs
│   ├── PicoTcpClient.cs
│   └── NetUtils.cs
├── Tracking/
│   └── PicoTrackingData.cs
├── UI/
│   ├── PicoBridgeUI.cs
│   └── PicoBridgeLog.cs
└── Camera/                          # 阶段 B
    ├── RemoteCameraWindow.cs
    └── MediaDecoder.cs
```

与 XRobo 的关系：
- `ByteBuffer`/`NetCMD`/`NetPacket`/`PackageHandle` — 从 XRobo 精简复制
- `PicoTcpClient` — 替代 `TcpHandler`，去掉 `LogWindow`/`FPSDisplay` 依赖
- `PicoTrackingData` — 替代 `TrackingData`，去掉录制和 VST 逻辑
- `PicoBridgeUI` — UGUI/TMP 实现，不依赖 `CustomButton`/`Toast`/`LogView`

### 4.2 PC Receiver

```
pc_receiver/
├── bridge.py                        # 本地开发入口包装器
├── pyproject.toml                   # Python 包元数据
└── src/pico_bridge/
    ├── __init__.py
    ├── cli.py
    ├── protocol.py
    ├── tcp_server.py
    ├── tracking.py
    └── video_sender.py              # 阶段 B
```

---

## 5. 里程碑

### M0：项目依赖修复

**输入：** 当前 manifest.json 缺少 embedded package 声明
**产出：** Unity 能正确解析所有 package

1. `Packages/manifest.json` 添加：
   ```json
   "com.unity.xr.picoxr": "file:PICO-Unity-Integration-SDK",
   "com.unity.pico.livepreview": "file:Unity-Live-Preview-Plugin"
   ```
2. Unity batchmode 验证 package resolve
3. 确认 `Assets/Scripts/PicoBridge/` 目录和 `.meta` 创建策略

### M1：协议和 PC receiver

**输入：** 协议规范（Section 3）
**产出：** PC receiver CLI 能接收并解析 VR 发来的所有包类型
**依赖：** 无（可与 M0 并行）

1. 实现 `protocol.py`：
   - `pack(cmd, data, *, direction)` / `feed(data) -> Iterable[Packet]`
   - TCP buffer 累积，处理半包/多包
   - PC 端接收时接受 `HEAD=0x3F`，发送时使用 `HEAD=0xCF`
2. 单元测试覆盖：
   - VR→PC 包（与 XRobo `PackageHandle.Pack` 等价）
   - PC→VR 包头为 `0xCF`
   - 半包、多包、错误 head/end
3. 实现 `tcp_server.py`：
   - `asyncio.start_server` 监听 `63901`
   - 解析 `0x19` connect / `0x6C` version / `0x23` heartbeat / `0x6D` tracking
   - 暴露 `on_tracking_data(callback)` 接口
4. 实现 `bridge.py` / `pico_bridge.cli` CLI：
   ```bash
   python pc_receiver/bridge.py --tcp-port 63901 --print-tracking [--video disabled]
   ```

### M2：Unity tracking client

**输入：** 协议规范 + 文件布局（Section 4.1）
**产出：** PICO 头显能连接 PC 并发送姿态数据
**依赖：** M0（package 解析正常）

1. Network primitives：`ByteBuffer`、`NetCMD`、`NetPacket`、`PackageHandle`
2. `PicoTcpClient`：连接、重连、心跳、发送队列
3. `PicoTrackingData`：
   - Head pose — PICO SDK / Unity XR
   - Controller pose + buttons — `PXR_Input` / `InputDevices`
   - Hand tracking — PICO hand tracking API，不可用时返回状态而非报错
   - Body / Motion Tracker — 标记可选，设备/SDK/权限满足时才启用
4. `PicoBridgeUI`（World Space Canvas）：
   - PC IP 输入框（`PlayerPrefs` 持久化，默认 `192.168.1.100`）
   - Connect / Disconnect 按钮
   - Head / Controller / Hand / Body / Motion toggles
   - Send Tracking toggle
   - Camera Preview toggle（阶段 B 前显示 "video spike pending"）
   - Status text：连接状态、本机 IP、错误、发送 FPS
5. Unity compile check

**Tracking JSON 结构**（保持接近 XRobo，方便 PC 端复用）：

```json
{
  "predictTime": 0,
  "appState": {"focus": true},
  "Head": {},
  "Controller": {},
  "Hand": {},
  "Body": {},
  "Motion": {},
  "timeStampNs": 0,
  "Input": 0
}
```

**实现原则：**
- 不把 `PXR_Enterprise.GetPredictedDisplayTime()` 作为必须路径
- 所有 PICO API 调用包一层 try/fallback，避免 Editor 或无权限设备崩溃

### M3：视频 spike

**输入：** XRobo 的 `MediaDecoder.cs` + `robotassistant_lib` AAR
**产出：** 已验证的 `MediaDecoder` transport 协议文档
**依赖：** M2（需要 TCP 通道发送 function）

1. 复制 `robotassistant_lib-i18n-release.aar`、`fbolibrary.aar`、`MediaDecoder.cs`
2. 写最小 `RemoteCameraWindow`：只负责 start/update/release
3. Python 监听 `StartReceivePcCamera` function
4. 验证 `MediaDecoder.startServer` 监听的是 TCP 还是 UDP，是否需要长度前缀
5. 记录验证结果：成功则进入 M4，失败则记录失败样例和传输假设

**spike 必须产出以下任一证据：**
- 抓包确认 transport 协议
- 最小 Python sender 使 `MediaDecoder.isUpdateFrame()` 返回 true 且画面更新

### M4：视频集成

**输入：** M3 spike 验证结果
**产出：** PC 摄像头画面在头显浮窗显示
**依赖：** M3（spike 成功）

**优先路线 — 兼容 XRobo Android decoder：**

1. `RemoteCameraWindow` 改写：
   - 去掉 `CustomButton`/`NetworkCommander`
   - 用 `RawImage` 显示（与 XRobo 已验证路径一致）
   - `StartListen` 后通过 `PicoTcpClient.SendFunctionValue("StartReceivePcCamera", json)` 通知 Python
2. `video_sender.py`：
   - OpenCV 打开摄像头
   - ffmpeg subprocess 生成 Annex-B H.264 bytestream
   - 按 spike 验证出的 transport 发送到 `ip:port`
3. UI 接入 Camera Preview toggle
4. Start/Stop/Error recovery
5. 设备实测延迟、分辨率、码率和重连

**备选路线 — 自有 Unity 解码链路：**
- 如果 `robotassistant_lib` 协议无法验证或 license 不可接受，不继续盲接 H.264
- 可先实现 MJPEG/PNG 帧传输到 `Texture2D.LoadImage` 作为低性能 fallback
- H.264 自有路线需要 Android `MediaCodec` JNI 插件，不在本阶段混做

---

## 6. Android 配置

**阶段 A 权限：** `INTERNET`（必须）、`ACCESS_NETWORK_STATE`（可选）

**阶段 B 权限：** PC 摄像头在 PC 端采集，头显显示远端画面通常不需要 Android `CAMERA`。仅在启用 PICO VST / 本机摄像头 / Enterprise camera API 时才需额外权限。

**不做的事：**
- 不把 PICO SDK 复制进 `Assets/`
- 不复制整个 XRobo `Assets/Scripts`
- 不引入新 Unity package（除非验证证明必须）
- 不把 `Library/`/`Temp/`/`Obj/`/`Build/`/`UserSettings/` 纳入源码

---

## 7. 验收标准

### 阶段 A 完成

- [ ] `cd pc_receiver && python -m pytest tests/test_protocol.py -q` 全过
- [ ] Python server 能解析 `0x19`/`0x6C`/`0x23`/`0x6D` 包
- [ ] Unity `2022.3.62f3` 打开项目无 C# compile errors
- [ ] Package Manager 正确解析 PICO 和 Live Preview package
- [ ] PICO 设备连接 PC CLI，Head tracking JSON 在 PC 端稳定输出
- [ ] Controller/Hand/Body toggles 无设备或无权限时显示 unavailable，不崩溃

### 阶段 B 完成

- [ ] 已记录 `MediaDecoder` 真实 transport 协议
- [ ] UI Camera Preview toggle 触发 `StartReceivePcCamera`
- [ ] PC 摄像头画面在头显浮窗显示
- [ ] Start/Stop 可重复执行
- [ ] ffmpeg/OpenCV 退出时资源释放正常

---

## 8. 风险

| 风险 | 处理 |
| --- | --- |
| `MediaDecoder` 协议未知 | 阶段 B 先 spike，不写死 UDP NAL |
| Enterprise API 权限缺失 | tracking 基础功能不依赖 Enterprise，企业功能 runtime fallback |
| manifest 与 lock 不一致 | M0 先修 manifest，Unity 更新 lock |
| Body/Motion Tracker 设备缺失 | UI 显示 unavailable，默认关闭 |
| PC/头显不在同一网段 | UI 和 CLI 打印本机 IP、目标 IP、端口、连接状态 |
