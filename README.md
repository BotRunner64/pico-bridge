# PICO Bridge

PICO 头显与 PC 之间的 tracking 数据桥接工具。头显端采集 6DoF 追踪数据（头部、手柄、手势、身体），通过 TCP 实时发送到 PC 端 Python server。

## 环境要求

- Unity `2022.3.62f3`
- Python `3.10+`
- 目标平台：Android / PICO 头显
- 渲染管线：URP

## 快速开始

### 1. PC 端启动 server

```bash
cd python
python bridge.py -v
```

启动后会：
- 监听 TCP 63901 等待头显连接
- UDP 广播自身 IP 到端口 29888（头显自动发现）

### 2. Unity Editor 测试（无需头显）

1. Unity 打开项目
2. 菜单 `PicoBridge > Setup Scene` 自动创建 GameObject
3. 确保 PC 端 `bridge.py` 已运行
4. 点 Play

Editor 模式下会发送模拟 tracking 数据，可以验证 TCP 连接和协议解析。

### 3. 头显部署

1. `PicoBridge > Validate Project Settings` 检查配置
2. `File > Build Settings > Android > Build And Run`
3. 头显启动后自动发现 PC server 并连接
4. PC 终端可看到实时 tracking 数据流

## 目录结构

```
Assets/Scripts/PicoBridge/
├── PicoBridgeManager.cs          # 主控：TCP + UDP 发现 + tracking 采集
├── Network/
│   ├── ByteBuffer.cs             # TCP 流缓冲区
│   ├── NetCMD.cs                 # 协议常量
│   ├── NetPacket.cs              # 解析后的包结构
│   ├── PackageHandle.cs          # 二进制协议编解码
│   ├── PicoTcpClient.cs          # TCP 客户端（自动重连、心跳）
│   └── UdpDiscovery.cs           # UDP 广播监听，自动发现 server
├── Tracking/
│   ├── PicoTrackingCollector.cs   # PXR 原生 API 采集 tracking 数据
│   └── MockTrackingData.cs        # Editor 模式模拟数据
├── UI/
│   └── PicoBridgeUI.cs            # IMGUI 连接面板
└── Editor/
    └── PicoBridgeSceneSetup.cs    # 菜单工具：自动搭建 scene

python/
├── bridge.py                      # CLI 入口
├── pico_bridge/
│   ├── protocol.py                # 二进制协议编解码
│   ├── tcp_server.py              # asyncio TCP server
│   ├── discovery.py               # UDP 广播
│   └── tracking.py                # tracking 数据解析
└── tests/
    └── test_protocol.py           # 协议单元测试
```

## 协议格式

```
[HEAD:1][CMD:1][LEN:4 LE][DATA:N][TIMESTAMP:8 LE][END:1]
```

| 方向 | HEAD | END |
|------|------|-----|
| VR → PC | `0x3F` | `0xA5` |
| PC → VR | `0xCF` | `0xA5` |

主要命令：

| CMD | 值 | 说明 |
|-----|------|------|
| CONNECT | `0x19` | 连接握手，payload 为 `deviceSN\|-1` |
| SEND_VERSION | `0x6C` | 版本信息 |
| TO_CONTROLLER_FUNCTION | `0x6D` | VR→PC 功能消息（含 Tracking） |
| CLIENT_HEARTBEAT | `0x23` | 心跳（10s 间隔） |
| FROM_CONTROLLER_COMMON_FUNCTION | `0x5F` | PC→VR 功能消息 |
| TCPIP | `0x7E` | UDP 发现广播 |

## Python CLI 参数

```
python bridge.py [OPTIONS]

--tcp-port          TCP 监听端口（默认 63901）
--no-print-tracking 不打印 tracking 帧
--advertise-ip      指定 UDP 广播的 IP（默认自动检测）
--no-discovery      禁用 UDP 广播
-v, --verbose       详细日志
```

## 运行测试

```bash
cd python
python -m pytest tests/ -v
```

## 开发配置

项目配置已通过代码设置完成：

- `Packages/manifest.json`：声明了 PICO SDK 和 Live Preview embedded package
- `Assets/Resources/PXR_ProjectSetting.asset`：已开启 handTracking、bodyTracking、videoSeeThrough
- `ProjectSettings/ProjectSettings.asset`：applicationIdentifier 为 `com.picobridge.app`，ForceInternetPermission 已开启
- XR Plug-in Management：Android 平台已选择 PICO Loader

## 开发约定

1. 运行时代码放在 `Assets/Scripts/PicoBridge/` 下
2. 编辑器代码放在 `Assets/Scripts/PicoBridge/Editor/` 下
3. 新增文件保留 `.meta` 文件
4. 不提交 `Library/`、`Temp/`、`Obj/`、`Logs/`、`Build/`、`UserSettings/`
