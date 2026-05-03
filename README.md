# PICO Bridge

PICO Bridge 把 PICO 4 / PICO 4 Ultra 的头显、手柄、手部、身体和 Motion Tracker 数据发送到 PC，并支持按需把 PC 摄像头画面回传到头显。

## Quick Start

1. PICO 和 PC 连接同一个局域网。
2. 使用前在 PICO 开发者菜单中关闭安全边界。
3. 在 PICO `设置 > 交互` 中打开“手势和控制器自动切换”。
4. 安装 APK，或用 Unity `2022.3.62f3` 构建安装。
5. 在头显中启动 PICO Bridge 应用。

```bash
cd pc_receiver
pip install -e .
pico-bridge-receiver -v --video camera --viz
```

6. 在头显内 PicoBridge 面板连接 PC receiver。

手动安装 APK：

```bash
sudo apt update
sudo apt install android-tools-adb
adb devices
adb install -r path/to/pico-bridge.apk
```

全身动捕需要先在 PICO 系统里配置 Motion Tracker，并完成校准。

## Docs

- PC 接口：[`docs/pc-receiver.md`](docs/pc-receiver.md)
- Unity 结构和开发：[`docs/unity-development.md`](docs/unity-development.md)
