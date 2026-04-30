# Release

当前建议发布为内部/合作方 alpha 包，而不是公开商店版本。

发布物应包含：

- PICO APK
- PC receiver wheel 或源码安装说明
- release notes
- 可选 `sha256sums.txt`

## 发布前检查

版本和签名：

- 更新 Unity `PlayerSettings.bundleVersion`。
- 递增 `AndroidBundleVersionCode`。
- 配置正式 Android keystore；不要用空签名状态做可升级发布。
- 确认 Android package name。当前是 `com.picobridge.app`。
- 更新 `pc_receiver/pyproject.toml` 的 `version`。

功能验证：

- PC receiver 单元测试通过。
- Unity Console 无编译错误。
- PICO 独立安装 APK 后可以连接 PC receiver。
- tracking 持续到达。
- 如果发布视频能力，验证 `test-pattern`、`camera` 或 `realsense` 中本次声明支持的模式。

性能验证：

- 不要用 Unity `Build & Run` + USB 调试作为最终性能结论。
- WebRTC 预览流畅度请用独立安装 APK、拔掉 USB、纯网络连接测试。

## 构建 PC wheel

```bash
cd pc_receiver
python -m build
```

如果本机缺少 `build`：

```bash
python -m pip install build
python -m build
```

产物在：

```text
pc_receiver/dist/
```

## 构建 PICO APK

从仓库根目录运行命令行构建入口：

```bash
/home/wubingqian/Unity/Hub/Editor/2022.3.62f3/Editor/Unity \
  -batchmode -quit \
  -projectPath "$PWD" \
  -executeMethod PicoBridge.Editor.PicoBridgeBuild.BuildAndroidApkFromCommandLine \
  -picoBridgeBuildPath /tmp/pico-bridge-v0.1.0-alpha.apk
```

也可以从 Unity Editor 手动切到 Android 后构建。

## 推荐命名

```text
pico-bridge-pico-v0.1.0-alpha.apk
pico_bridge_pc_receiver-0.1.0-py3-none-any.whl
release-notes-v0.1.0-alpha.md
sha256sums.txt
```

## Release notes 模板

```markdown
# PICO Bridge v0.1.0-alpha

## 支持内容

- PICO tracking 到 PC receiver
- UDP discovery
- WebRTC PC camera preview: [声明本次支持的模式]

## 安装

- PICO: 安装 APK
- PC: `pip install pico_bridge_pc_receiver-0.1.0-py3-none-any.whl`

## 端口

- TCP: 63901
- UDP discovery: 29888

## 已知问题

- [填写本次确认的问题]

## 验证

- [填写测试命令和真机结果]
```

## Git tag

建议使用语义版本加发布阶段：

```bash
git tag v0.1.0-alpha
```

如果创建 release commit，commit message 遵守仓库 Lore Commit Protocol。
