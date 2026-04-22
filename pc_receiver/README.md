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
