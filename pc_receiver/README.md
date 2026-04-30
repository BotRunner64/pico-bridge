# PC Receiver

这是 PICO Bridge 的 PC 端 Python 子项目。

完整 SDK 文档见 [`../docs/pc-receiver.md`](../docs/pc-receiver.md)。

## 快速启动

```bash
pip install -e .
pico-bridge-receiver -v --video camera --viz
```

源码调试入口：

```bash
python bridge.py -v --video camera --viz
```

## 开发验证

```bash
pytest tests -q
```

## 包结构

```text
bridge.py                 本地开发入口
src/pico_bridge/          receiver package
tests/                    单元测试
```

默认端口：

- TCP tracking/control：`63901`
- UDP discovery：`29888`
