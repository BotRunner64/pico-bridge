# PICO Bridge 文档

PICO Bridge 把 PICO 4 / PICO 4 Ultra 的头显、手柄、手部、身体和 Motion Tracker 数据发送到 PC，并支持按需把 PC 摄像头画面回传到头显。

## 文档索引

| 主题 | 说明 |
| --- | --- |
| [PC 接口](pc-receiver.md) | Python 包安装、下游项目依赖方式、`PicoBridge` API、帧字段和坐标语义。 |
| [Unity 结构和开发](unity-development.md) | Unity 版本、项目结构、编辑器菜单、开发规则和验证步骤。 |

## 快速开始

1. PICO 和 PC 连接同一个局域网。
2. 使用前在 PICO 开发者菜单中关闭安全边界。
3. 在 PICO `设置 > 交互` 中打开“手势和控制器自动切换”。
4. 从 [GitHub Releases](https://github.com/BotRunner64/pico-bridge/releases) 下载 APK，或用 Unity `2022.3.62f3` 构建安装。
5. 在头显中启动 PICO Bridge 应用。

```bash
pip install https://github.com/BotRunner64/pico-bridge/releases/download/v0.1.0/pico_bridge-0.1.0-py3-none-any.whl
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

## 语言

- [English docs](../en/README.md)
- [仓库 README](../../README.md)
