# Il2CppDumper（本分支）

基于源仓库 `Il2CppDumper v6.7.46` 的改版，主要目标是提升“基于 hint 的初始化成功率”和整体稳定性，避免因为 `CodeRegistration/MetadataRegistration` 不准确导致 `RVA/Offset = -1` 大面积出现。
同时优化了检查逻辑，提高了对映像魔改的游戏的global-metadata数据的兼容性，不会轻易出现结构问题导致的dump失败报错情况。

## 本分支增强

> [!NOTE]
> - **Hint 初始化**：支持读取 `<metadata>.hint.json`（或手动指定 `hint.json`）直接初始化 `CodeRegistration/MetadataRegistration`。
> - **回退与候选搜索**：当 hint 不可靠时，会自动尝试更多候选并做基础合理性校验，提高初始化成功率。
> - **稳定性**：对 section 扫描的大块读取做了保护；对明显异常的 count 做容错处理，降低崩溃概率。

## 快速使用

### 交互模式

直接运行 `Il2CppDumper.exe` 并依次选择：

1. il2cpp 可执行文件（PC 通常为 `GameAssembly.dll` / `*Assembly.dll`）
2. metadata 文件（`*.dat`，文件名不要求必须是 `global-metadata.dat`）
3.（可选）hint 文件（`*.json`，用于手动指定 hint；不选则默认尝试读取 `<metadata>.hint.json`）

### 命令行

```
Il2CppDumper.exe <executable-file> <metadata.dat> <output-directory> [hint.json]
```

## 输出

- `DummyDll/`：还原的 DLL（不包含代码）
- `DumpSDK/`：`dump.cs` / `il2cpp.h` / `script.json` / `stringliteral.json` 等

## License

MIT，见 `LICENSE`。

