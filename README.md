# Il2CppDumper

[![Build status](https://ci.appveyor.com/api/projects/status/anhqw33vcpmp8ofa?svg=true)](https://ci.appveyor.com/project/Perfare/il2cppdumper/branch/master/artifacts)

> Unity IL2CPP 逆向工程工具（本仓库为增强分支）

## 目录

- [本分支增强](#本分支增强)
- [功能](#功能)
- [使用说明](#使用说明)
  - [交互模式](#交互模式)
  - [命令行](#命令行)
- [输出文件](#输出文件)
  - [目录结构](#目录结构)
  - [DummyDll](#dummydll)
  - [其它输出](#其它输出)
- [关于 config.json](#关于-configjson)
- [常见问题](#常见问题)
- [License](#license)
- [感谢](#感谢)

## 本分支增强

- Hint 初始化：支持读取 `<metadata>.hint.json`（或手动指定 `hint.json`）直接初始化 `CodeRegistration/MetadataRegistration`。
- 稳定性：对 section 扫描的大块读取做了保护，降低异常节区导致崩溃的概率。

## 功能

- 还原DLL文件（不包含代码），可用于提取 `MonoBehaviour` 和 `MonoScript`
- 支持 ELF, ELF64, Mach-O, PE, NSO 和 WASM 格式
- 支持 Unity 5.3 - 2022.2
- 生成 IDA 和 Ghidra 的脚本，帮助 IDA 和 Ghidra 更好的分析 il2cpp 文件
- 生成结构体头文件
- 支持从内存 dump 的 `libil2cpp.so` 文件以绕过保护
- 支持绕过简单的 PE 保护

## 使用说明

### 交互模式

直接运行 `Il2CppDumper.exe` 并依次选择：

1. il2cpp 可执行文件（PC 通常为 `GameAssembly.dll` / `*Assembly.dll`）
2. metadata 文件（`*.dat`，文件名不要求必须是 `global-metadata.dat`）
3.（可选）hint 文件（`*.json`，用于手动指定 hint；不选则默认读取 `<metadata>.hint.json`）

未指定输出目录时，默认输出到相对路径 `./DumpSDK/`。

### 命令行

```text
Il2CppDumper.exe <executable-file> <metadata.dat> <output-directory> [hint.json]
```

## 输出文件

### 目录结构

- **`DummyDll/`**
  - 还原的 DLL 文件（不包含代码）
- **`DumpSDK/`**
  - 代码/脚本相关产物（`dump.cs` / `il2cpp.h` / `script.json` / `stringliteral.json` 等）

> 说明：如果你指定的 `<output-directory>` 本身就叫 `DumpSDK`，则不会出现 `DumpSDK/DumpSDK` 嵌套。

### DummyDll

文件夹，包含所有还原的 DLL 文件。

使用 [dnSpy](https://github.com/0xd4d/dnSpy)、[ILSpy](https://github.com/icsharpcode/ILSpy) 或其他 .Net 反编译工具即可查看具体信息。

可用于提取 Unity 的 `MonoBehaviour` 和 `MonoScript`，适用于 [UtinyRipper](https://github.com/mafaca/UtinyRipper) 或 [UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor) 等。

### 其它输出

| 文件 | 用途 |
| --- | --- |
| `ida.py` | 用于 IDA。 |
| `ida_with_struct.py` | 用于 IDA，读取 `il2cpp.h` 文件并在 IDA 中应用结构信息。 |
| `il2cpp.h` | 包含结构体的头文件。 |
| `ghidra.py` | 用于 Ghidra。 |
| `Il2CppBinaryNinja/` | 用于 BinaryNinja。 |
| `ghidra_wasm.py` | 用于 Ghidra，和 [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin) 一起工作。 |
| `script.json` | 用于 IDA 和 Ghidra 脚本。 |
| `stringliteral.json` | 包含所有 stringLiteral 信息。 |

## 关于 config.json

| 字段 | 说明 |
| --- | --- |
| `DumpMethod` / `DumpField` / `DumpProperty` / `DumpAttribute` / `DumpFieldOffset` / `DumpMethodOffset` / `DumpTypeDefIndex` | 是否在 `dump.cs` 输出相应的内容。 |
| `GenerateDummyDll` | 是否生成 `DummyDll/`。 |
| `GenerateStruct` | 是否生成 `DumpSDK/` 下的 `il2cpp.h` / `script.json` / `stringliteral.json` 等。 |
| `DummyDllAddToken` | 是否在 DummyDll 中添加 token。 |
| `RequireAnyKey` | 在程序结束时是否需要按键退出。 |
| `ForceIl2CppVersion` / `ForceVersion` | 当 `ForceIl2CppVersion` 为 `true` 时，程序将根据 `ForceVersion` 指定的版本读取 il2cpp 的可执行文件（Metadata 仍然使用 header 里的版本），在部分低版本的 il2cpp 中可能会用到。 |
| `ForceDump` | 强制将文件视为 dump 文件。 |
| `NoRedirectedPointer` | 将 dump 文件中的指针视为未重定向的，从某些设备 dump 出的文件需要设置该项为 `true`。 |

## 常见问题

### `ERROR: Metadata file supplied is not valid metadata file.`

`metadata.dat` 已被加密或不是有效的 metadata。关于解密的问题请去相关论坛寻求帮助，请不要在 issues 提问。

如果你的文件是 `libil2cpp.so` 并且你拥有一台已 root 的安卓手机，你可以尝试另一个项目 [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够无视 `global-metadata.dat` 加密。

### `ERROR: Can't use auto mode to process file, try manual mode.`

请注意 PC 平台的可执行文件通常是 `GameAssembly.dll` 或 `*Assembly.dll`。

### `ERROR: This file may be protected.`

Il2CppDumper 检测到可执行文件已被保护，可以使用 `GameGuardian` 从游戏内存中 dump `libil2cpp.so`，然后使用 Il2CppDumper 载入按提示操作，可绕过大部分保护。

如果你拥有一台已 root 的安卓手机，你可以尝试 [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够绕过几乎所有保护。

## License

本项目遵循 MIT License（见 `LICENSE`），请在 fork/分发时保留版权声明与许可文本。

## 感谢

- Jumboperson - [Il2CppDumper](https://github.com/Jumboperson/Il2CppDumper)
