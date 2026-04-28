# PC Receiver

PICO Bridge 的 PC 端接收子工程，单独隔离在 `pc_receiver/` 下。

## 快速开始

```bash
cd pc_receiver
python bridge.py -v
```

如果希望按标准 Python 包方式使用：

```bash
cd pc_receiver
pip install -e .
pico-bridge-receiver -v
```

如需实时 3D 可视化，安装可选依赖后使用 `--viz` 启动 Rerun，或用 `--viz-connect` 连接已运行的 viewer：

```bash
cd pc_receiver
pip install -e ".[viz]"
pico-bridge-receiver --viz
pico-bridge-receiver --viz-connect
```

## 目录结构

```text
pc_receiver/
├── bridge.py              # 本地开发入口包装器
├── pyproject.toml         # Python 包元数据
├── src/pico_bridge/       # 接收端实现
└── tests/                 # Python 单元测试
```

## 运行测试

```bash
cd pc_receiver
python -m pytest tests -v
```
## WebRTC 视频预览

第一版视频预览使用 WebRTC 传输 `test-pattern` 到 PICO，tracking/control 仍走现有 TCP 协议。

```bash
cd pc_receiver
pip install -e .
python bridge.py --video test-pattern
```

真实摄像头使用 `camera` 源，Linux 默认读取 `/dev/video0`，也可以手动指定设备：

```bash
python bridge.py --video camera --camera-device /dev/video0
```

Intel RealSense 摄像头使用 `realsense` 源，只读取 RGB color stream。安装项目依赖后启动：

```bash
pip install -e .
python bridge.py --video realsense
```

如果有多台 RealSense，可用序列号指定设备：

```bash
python bridge.py --video realsense --camera-device 0123456789
```
