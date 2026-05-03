# Development

## 目录约定

- Unity runtime 代码放在 `Assets/Scripts/PicoBridge/`。
- Unity editor-only 代码放在 `Editor/` 文件夹下。
- Android native plugin assets 放在 `Assets/Plugins/Android/`。
- Python receiver 放在 `pc_receiver/`。
- 文档放在 `docs/`，根 `README.md` 只做入口。

## Unity 约定

- Render Pipeline 使用 Built-in 3D。
- 运行时代码不要创建、删除、重建或自动迁移 UI 层级。
- UI 层级通过编辑器工具或手动 prefab/scene 编辑维护。
- 添加、移动、删除 Unity asset 时保留 `.meta` 文件。
- 不提交 `Library/`、`Temp/`、`Obj/`、`Logs/`、`Build/`、`Builds/`、`UserSettings/`。
- 不把生成的 `*.csproj` / `*.sln` 当作源文件维护。

## Python 约定

- `pc_receiver/src/pico_bridge/` 是 package 实现。
- `pc_receiver/bridge.py` 是本地开发入口。
- `pc_receiver/tests/` 是单元测试。
- 下游项目 adapter 不放进 `pico_bridge`。

## 验证

Python：

```bash
cd pc_receiver
pytest tests -q
```

Unity：

- 用 Unity `2022.3.62f3` 打开项目。
- 确认 Console 无编译错误。
- 执行 `PicoBridge > Validate Project Settings`。
- 真机确认 passthrough、tracking、连接和视频行为。

命令行 APK 构建见 [`release.md`](release.md)。

## 修改文档

- 优先让每篇文档服务一个读者和一个任务。
- 命令必须可复制。
- 避免在根 `README.md` 放排障、API 和发布细节。
- 同一事实只维护在一个主要位置，其他地方用链接引用。
