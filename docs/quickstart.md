# Quickstart

目标：在同一局域网内让 PICO 头显连接 PC receiver，并在 PC 端看到 tracking 数据。

## 前置条件

- Unity 项目使用 `2022.3.62f3`。
- PICO 头显和 PC 在同一个局域网。
- PC 允许 TCP `63901` 和 UDP `29888` 通过防火墙。
- PC Python 版本 `>= 3.10`。
- 如果需要手动安装 APK，下载 Android SDK Platform Tools 获取 `adb`：<https://developer.android.com/tools/releases/platform-tools>。

## 1. 启动 PC receiver

```bash
cd pc_receiver
pip install -e .
pico-bridge-receiver -v --video camera --viz
```

如果只想用源码入口调试：

```bash
cd pc_receiver
python bridge.py -v --video camera --viz
```

默认 receiver 会：

- 监听 TCP `63901`
- 通过 UDP `29888` 广播发现信息
- 接收并打印低频连接、视频和状态日志
- 等头显请求后打开 PC camera，并在 Rerun 窗口显示 tracking 可视化

逐帧排查 tracking 时再打开详细输出：

```bash
python bridge.py --print-tracking
```

## 2. 启动 PICO 端

推荐使用已发布 APK。开发时也可以用 Unity 打开项目后构建安装。

已发布 APK 安装路径：

1. 下载 Android SDK Platform Tools，并确认 `adb` 在 `PATH` 中，或在命令里使用 `platform-tools/adb` 的完整路径。
2. 用 USB 数据线连接 PICO 头显和 PC。
3. 在头显里允许 USB 调试授权。
4. 确认 PC 能看到设备：

```bash
adb devices
```

5. 安装 APK：

```bash
adb install -r path/to/pico-bridge.apk
```

Unity 开发路径：

1. 第一次 clone 后，打开 Unity Hub，选择 `Add` / `Add project from disk`，把仓库根目录加入 Hub。
2. 用 Unity `2022.3.62f3` 从 Hub 打开该项目。
3. 进入 Editor 后，手动打开 `Assets/Scenes/SampleScene.unity`。Unity 不会可靠地根据 Build Settings 自动打开主场景。
4. 确认 Android 平台启用 PICO Loader。
5. 如当前场景缺少桥接对象，执行 `PicoBridge > Setup Scene`。
6. 构建并安装到 PICO 头显。

## 3. 连接

1. 确认 PC receiver 正在运行。
2. 在头显内 PicoBridge 面板连接 PC。
3. PC 端日志应显示设备连接和 tracking 帧更新。

## 成功标准

- PC receiver 显示 connected 状态。
- tracking 帧持续到达，`fps` 大于 0。
- 头显 UI 的 tracking 信号项按实际设备亮起。
- 如果启用了视频模式，头显能看到 PC 摄像头或测试图画面。

## 常见失败点

- PICO 和 PC 不在同一网段。
- PC 防火墙拦截 TCP `63901` 或 UDP `29888`。
- 多网卡机器广播了错误 IP。此时用 `PicoBridge(advertise_ip="...")` 或 CLI 参数指定地址。
- Unity `Build & Run` 加 USB 调试会影响 WebRTC 预览流畅度。性能验证请用独立安装 APK、拔掉 USB 后测试。

更多排障见 [`troubleshooting.md`](troubleshooting.md)。
