# Il2CppDumper

[![Build status](https://ci.appveyor.com/api/projects/status/anhqw33vcpmp8ofa?svg=true)](https://ci.appveyor.com/project/Perfare/il2cppdumper/branch/master/artifacts)

Unity il2cpp逆向工程

## 本分支增强

* Sidecar hint 机制：当存在 `<global-metadata>.hint.json` 时，优先使用其中的 `code_registration_rva`/`metadata_registration_rva` 初始化，减少手动输入与启发式搜索。
* Hint 字段不完整时自动回退到原有初始化流程（auto search / symbol search / manual）。
* 日志增强：启动时明确提示是否使用 hint、使用 rva 还是 runtimeVA、以及不使用的原因。
* 稳定性增强：对 section 扫描中的大块读取增加长度保护，降低异常节区导致崩溃的概率。

## 功能

* 还原DLL文件（不包含代码），可用于提取 `MonoBehaviour` 和 `MonoScript`
* 支持 ELF, ELF64, Mach-O, PE, NSO 和 WASM 格式
* 支持 Unity 5.3 - 2022.2
* 生成 IDA 和 Ghidra 的脚本，帮助 IDA 和 Ghidra 更好的分析 il2cpp 文件
* 生成结构体头文件
* 支持从内存 dump 的 `libil2cpp.so` 文件以绕过保护
* 支持绕过简单的 PE 保护

## 使用说明

直接运行 `Il2CppDumper.exe` 并依次选择 il2cpp 的可执行文件和 `global-metadata.dat` 文件，然后根据提示输入相应信息。

程序运行完成后将在当前运行目录下生成输出文件。

### 命令行

```
Il2CppDumper.exe <executable-file> <global-metadata> <output-directory>
```

### 输出文件

#### DummyDll

文件夹，包含所有还原的 DLL 文件。

使用 [dnSpy](https://github.com/0xd4d/dnSpy)、[ILSpy](https://github.com/icsharpcode/ILSpy) 或其他 .Net 反编译工具即可查看具体信息。

可用于提取 Unity 的 `MonoBehaviour` 和 `MonoScript`，适用于 [UtinyRipper](https://github.com/mafaca/UtinyRipper) 或 [UABE](https://7daystodie.com/forums/showthread.php?22675-Unity-Assets-Bundle-Extractor) 等。

#### ida.py

用于 IDA。

#### ida_with_struct.py

用于 IDA，读取 `il2cpp.h` 文件并在 IDA 中应用结构信息。

#### il2cpp.h

包含结构体的头文件。

#### ghidra.py

用于 Ghidra。

#### Il2CppBinaryNinja

用于 BinaryNinja。

#### ghidra_wasm.py

用于 Ghidra，和 [ghidra-wasm-plugin](https://github.com/nneonneo/ghidra-wasm-plugin) 一起工作。

#### script.json

用于 IDA 和 Ghidra 脚本。

#### stringliteral.json

包含所有 stringLiteral 信息。

### 关于 config.json

* `DumpMethod`，`DumpField`，`DumpProperty`，`DumpAttribute`，`DumpFieldOffset`, `DumpMethodOffset`, `DumpTypeDefIndex`
  * 是否在 `dump.cs` 输出相应的内容

* `GenerateDummyDll`，`GenerateScript`
  * 是否生成这些内容

* `DummyDllAddToken`
  * 是否在 DummyDll 中添加 token

* `RequireAnyKey`
  * 在程序结束时是否需要按键退出

* `ForceIl2CppVersion`，`ForceVersion`
  * 当 `ForceIl2CppVersion` 为 `true` 时，程序将根据 `ForceVersion` 指定的版本读取 il2cpp 的可执行文件（Metadata 仍然使用 header 里的版本），在部分低版本的 il2cpp 中可能会用到

* `ForceDump`
  * 强制将文件视为 dump 文件

* `NoRedirectedPointer`
  * 将 dump 文件中的指针视为未重定向的，从某些设备 dump 出的文件需要设置该项为 `true`

## 常见问题

#### `ERROR: Metadata file supplied is not valid metadata file.`

`global-metadata.dat` 已被加密。关于解密的问题请去相关论坛寻求帮助，请不要在 issues 提问。

如果你的文件是 `libil2cpp.so` 并且你拥有一台已 root 的安卓手机，你可以尝试另一个项目 [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够无视 `global-metadata.dat` 加密。

#### `ERROR: Can't use auto mode to process file, try manual mode.`

请注意 PC 平台的可执行文件通常是 `GameAssembly.dll` 或 `*Assembly.dll`。

#### `ERROR: This file may be protected.`

Il2CppDumper 检测到可执行文件已被保护，可以使用 `GameGuardian` 从游戏内存中 dump `libil2cpp.so`，然后使用 Il2CppDumper 载入按提示操作，可绕过大部分保护。

如果你拥有一台已 root 的安卓手机，你可以尝试 [Zygisk-Il2CppDumper](https://github.com/Perfare/Zygisk-Il2CppDumper)，它能够绕过几乎所有保护。

## License

本项目遵循 MIT License（见 `LICENSE`），请在 fork/分发时保留版权声明与许可文本。

## 感谢

- Jumboperson - [Il2CppDumper](https://github.com/Jumboperson/Il2CppDumper)
