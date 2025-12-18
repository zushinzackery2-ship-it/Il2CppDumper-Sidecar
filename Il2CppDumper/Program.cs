using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Il2CppDumper
{
    class Program
    {
        private static Config config;

        private static bool IsLikelyMetadataFile(byte[] file)
        {
            if (file == null || file.Length < 0x120)
                return false;
            var version = BitConverter.ToInt32(file, 4);
            if (version < 16 || version > 31)
                return false;
            var imagesSize = BitConverter.ToUInt32(file, 0xAC);
            var assembliesSize = BitConverter.ToUInt32(file, 0xB4);
            if (imagesSize == 0 || assembliesSize == 0)
                return false;
            if (imagesSize % 0x28 != 0)
                return false;
            if (assembliesSize % 0x40 != 0)
                return false;
            return true;
        }

        [STAThread]
        static void Main(string[] args)
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(AppDomain.CurrentDomain.BaseDirectory + @"config.json"));
            string il2cppPath = null;
            string metadataPath = null;
            string outputDir = null;
            string hintPath = null;

            if (args.Length == 1)
            {
                if (args[0] == "-h" || args[0] == "--help" || args[0] == "/?" || args[0] == "/h")
                {
                    ShowHelp();
                    return;
                }
            }
            if (args.Length > 4)
            {
                ShowHelp();
                return;
            }
            if (args.Length == 3 || args.Length == 4)
            {
                il2cppPath = args[0];
                metadataPath = args[1];
                outputDir = Path.GetFullPath(args[2]) + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(outputDir);
                if (args.Length == 4)
                {
                    hintPath = args[3];
                }
            }
            else if (args.Length == 2)
            {
                il2cppPath = args[0];
                metadataPath = args[1];
            }
            else if (args.Length > 1)
            {
                foreach (var arg in args)
                {
                    if (File.Exists(arg))
                    {
                        var file = File.ReadAllBytes(arg);
                        if (arg.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            hintPath = arg;
                        }
                        else if (IsLikelyMetadataFile(file))
                        {
                            metadataPath = arg;
                        }
                        else
                        {
                            il2cppPath = arg;
                        }
                    }
                    else if (Directory.Exists(arg))
                    {
                        outputDir = Path.GetFullPath(arg) + Path.DirectorySeparatorChar;
                    }
                }
            }
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.GetFullPath("DumpSDK") + Path.DirectorySeparatorChar;
                Directory.CreateDirectory(outputDir);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (il2cppPath == null)
                {
                    var ofd = new OpenFileDialog
                    {
                        Filter = "Il2Cpp binary file|*.*"
                    };
                    if (ofd.ShowDialog())
                    {
                        il2cppPath = ofd.FileName;
                        ofd.Filter = "Metadata (*.dat)|*.dat|All Files|*.*";
                        if (ofd.ShowDialog())
                        {
                            metadataPath = ofd.FileName;
                            var hfd = new OpenFileDialog
                            {
                                Title = "Hint json (optional)",
                                Filter = "Hint json (*.json)|*.json|All Files|*.*"
                            };
                            if (hfd.ShowDialog())
                            {
                                hintPath = hfd.FileName;
                            }
                        }
                        else
                        {
                            return;
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }
            if (il2cppPath == null)
            {
                ShowHelp();
                return;
            }
            if (metadataPath == null)
            {
                Console.WriteLine($"ERROR: Metadata file not found or encrypted.");
            }
            else
            {
                try
                {
                    if (Init(il2cppPath, metadataPath, hintPath, out var metadata, out var il2Cpp))
                    {
                        Dump(metadata, il2Cpp, outputDir);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            if (args.Length == 0 && config.RequireAnyKey && Environment.UserInteractive && !Console.IsInputRedirected)
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey(true);
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine($"usage: {AppDomain.CurrentDomain.FriendlyName} <executable-file> <metadata.dat> <output-directory> [hint.json]");
        }

        private static bool Init(string il2cppPath, string metadataPath, string hintPath, out Metadata metadata, out Il2Cpp il2Cpp)
        {
            Console.WriteLine("Initializing metadata...");
            var metadataBytes = File.ReadAllBytes(metadataPath);
            metadata = new Metadata(new MemoryStream(metadataBytes));
            Console.WriteLine($"Metadata Version: {metadata.Version}");

            Console.WriteLine("Initializing il2cpp file...");
            var il2cppBytes = File.ReadAllBytes(il2cppPath);
            var il2cppMagic = BitConverter.ToUInt32(il2cppBytes, 0);
            var il2CppMemory = new MemoryStream(il2cppBytes);
            switch (il2cppMagic)
            {
                default:
                    throw new NotSupportedException("ERROR: il2cpp file not supported.");
                case 0x6D736100:
                    var web = new WebAssembly(il2CppMemory);
                    il2Cpp = web.CreateMemory();
                    break;
                case 0x304F534E:
                    var nso = new NSO(il2CppMemory);
                    il2Cpp = nso.UnCompress();
                    break;
                case 0x905A4D: //PE
                    il2Cpp = new PE(il2CppMemory);
                    break;
                case 0x464c457f: //ELF
                    if (il2cppBytes[4] == 2) //ELF64
                    {
                        il2Cpp = new Elf64(il2CppMemory);
                    }
                    else
                    {
                        il2Cpp = new Elf(il2CppMemory);
                    }
                    break;
                case 0xCAFEBABE: //FAT Mach-O
                case 0xBEBAFECA:
                    var machofat = new MachoFat(new MemoryStream(il2cppBytes));
                    Console.Write("Select Platform: ");
                    for (var i = 0; i < machofat.fats.Length; i++)
                    {
                        var fat = machofat.fats[i];
                        Console.Write(fat.magic == 0xFEEDFACF ? $"{i + 1}.64bit " : $"{i + 1}.32bit ");
                    }
                    Console.WriteLine();
                    var key = Console.ReadKey(true);
                    var index = int.Parse(key.KeyChar.ToString()) - 1;
                    var magic = machofat.fats[index % 2].magic;
                    il2cppBytes = machofat.GetMacho(index % 2);
                    il2CppMemory = new MemoryStream(il2cppBytes);
                    if (magic == 0xFEEDFACF)
                        goto case 0xFEEDFACF;
                    else
                        goto case 0xFEEDFACE;
                case 0xFEEDFACF: // 64bit Mach-O
                    il2Cpp = new Macho64(il2CppMemory);
                    break;
                case 0xFEEDFACE: // 32bit Mach-O
                    il2Cpp = new Macho(il2CppMemory);
                    break;
            }
            var version = config.ForceIl2CppVersion ? config.ForceVersion : metadata.Version;
            il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
            Console.WriteLine($"Il2Cpp Version: {il2Cpp.Version}");

            if (TryInitFromHint(il2Cpp, metadata, metadataPath, hintPath))
            {
                return true;
            }
            if (config.ForceDump || il2Cpp.CheckDump())
            {
                if (il2Cpp is ElfBase elf)
                {
                    Console.WriteLine("Detected this may be a dump file.");
                    Console.WriteLine("Input il2cpp dump address or input 0 to force continue:");
                    var DumpAddr = Convert.ToUInt64(Console.ReadLine(), 16);
                    if (DumpAddr != 0)
                    {
                        il2Cpp.ImageBase = DumpAddr;
                        il2Cpp.IsDumped = true;
                        if (!config.NoRedirectedPointer)
                        {
                            elf.Reload();
                        }
                    }
                }
                else
                {
                    il2Cpp.IsDumped = true;
                }
            }

            Console.WriteLine("Searching...");
            try
            {
                var flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    if (!flag && il2Cpp is PE)
                    {
                        Console.WriteLine("Use custom PE loader");
                        try
                        {
                            il2Cpp = PELoader.Load(il2cppPath);
                            il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
                            flag = il2Cpp.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length, metadata.imageDefs.Length);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Custom PE loader failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
                if (!flag)
                {
                    flag = il2Cpp.Search();
                }
                if (!flag)
                {
                    flag = il2Cpp.SymbolSearch();
                }
                if (!flag)
                {
                    Console.WriteLine("ERROR: Can't use auto mode to process file, try manual mode.");
                    Console.Write("Input CodeRegistration: ");
                    var crLine = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(crLine))
                    {
                        Console.WriteLine("No manual input provided.");
                        return false;
                    }
                    var codeRegistration = Convert.ToUInt64(crLine, 16);
                    Console.Write("Input MetadataRegistration: ");
                    var mrLine = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(mrLine))
                    {
                        Console.WriteLine("No manual input provided.");
                        return false;
                    }
                    var metadataRegistration = Convert.ToUInt64(mrLine, 16);
                    il2Cpp.Init(codeRegistration, metadataRegistration);
                }
                if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
                {
                    var typeDef = metadata.typeDefs[0];
                    var il2CppType = il2Cpp.types[typeDef.byvalTypeIndex];
                    metadata.ImageBase = il2CppType.data.typeHandle - metadata.header.typeDefinitionsOffset;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                Console.WriteLine("ERROR: An error occurred while processing.");
                return false;
            }
            return true;
        }

        private static bool TryInitFromHint(Il2Cpp il2Cpp, Metadata metadata, string metadataPath, string hintPathOverride)
        {
            try
            {
                var hintPath = !string.IsNullOrWhiteSpace(hintPathOverride)
                    ? hintPathOverride
                    : metadataPath + ".hint.json";
                if (!File.Exists(hintPath))
                {
                    if (!string.IsNullOrWhiteSpace(hintPathOverride))
                    {
                        Console.WriteLine($"Hint not used: file not found: {hintPath}");
                    }
                    return false;
                }
                Console.WriteLine($"Hint found: {hintPath}");
                var hint = JsonSerializer.Deserialize<DumpHint>(File.ReadAllText(hintPath));
                if (hint == null)
                {
                    Console.WriteLine("Hint not used: parse returned null");
                    return false;
                }

                if (il2Cpp is PE)
                {
                    il2Cpp.ExpectedImageCount = metadata.imageDefs.Length;
                    il2Cpp.ExpectedTypeDefinitionsCount = metadata.typeDefs.Length;
                    il2Cpp.ExpectedMethodCount = metadata.methodDefs.Count(x => x.methodIndex >= 0);

                    var codeRegRva = ParseHexUlong(hint.il2cpp?.code_registration_rva);
                    var metaRegRva = ParseHexUlong(hint.il2cpp?.metadata_registration_rva);
                    var runtimeBase = ParseHexUlong(hint.module?.base_addr);
                    var codeRegRuntime = ParseHexUlong(hint.il2cpp?.code_registration);
                    var metaRegRuntime = ParseHexUlong(hint.il2cpp?.metadata_registration);

                    if (codeRegRva != 0 && metaRegRva != 0)
                    {
                        Console.WriteLine($"Hint mode: RVA codeRegRva=0x{codeRegRva:x} metaRegRva=0x{metaRegRva:x}");
                        var codeReg = il2Cpp.ImageBase + codeRegRva;
                        var metaReg = il2Cpp.ImageBase + metaRegRva;
                        Console.WriteLine($"Hint CodeRegistration : {codeReg:x}");
                        Console.WriteLine($"Hint MetadataRegistration : {metaReg:x}");
                        if (il2Cpp.AutoPlusInit(codeReg, metaReg))
                        {
                            return true;
                        }
                        Console.WriteLine("Hint RVA mode failed, trying RuntimeVA...");
                    }

                    if (runtimeBase == 0 || codeRegRuntime == 0 || metaRegRuntime == 0)
                    {
                        Console.WriteLine("Hint not used: missing module.base or il2cpp registrations");
                        return false;
                    }
                    if (codeRegRuntime < runtimeBase || metaRegRuntime < runtimeBase)
                    {
                        Console.WriteLine("Hint not used: runtime addresses are below module base");
                        return false;
                    }
                    Console.WriteLine($"Hint mode: RuntimeVA moduleBase=0x{runtimeBase:x}");
                    var codeReg2 = il2Cpp.ImageBase + (codeRegRuntime - runtimeBase);
                    var metaReg2 = il2Cpp.ImageBase + (metaRegRuntime - runtimeBase);
                    Console.WriteLine($"Hint CodeRegistration : {codeReg2:x}");
                    Console.WriteLine($"Hint MetadataRegistration : {metaReg2:x}");
                    return il2Cpp.AutoPlusInit(codeReg2, metaReg2);
                }

                Console.WriteLine("Hint not used: only PE is supported");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hint init failed: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static ulong ParseHexUlong(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }
            value = value.Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                value = value[2..];
            }
            if (ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
            return 0;
        }

        private sealed class DumpHint
        {
            public int schema { get; set; }
            public DumpHintModule module { get; set; }
            public DumpHintIl2Cpp il2cpp { get; set; }
        }

        private sealed class DumpHintModule
        {
            [JsonPropertyName("base")]
            public string base_addr { get; set; }
        }

        private sealed class DumpHintIl2Cpp
        {
            public string code_registration { get; set; }
            public string metadata_registration { get; set; }
            public string code_registration_rva { get; set; }
            public string metadata_registration_rva { get; set; }
        }

        private static void Dump(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            Console.WriteLine("Dumping...");
            var outputRoot = outputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var baseName = Path.GetFileName(outputRoot);
            var sdkDir = string.Equals(baseName, "DumpSDK", StringComparison.OrdinalIgnoreCase)
                ? outputRoot
                : Path.Combine(outputRoot, "DumpSDK");
            Directory.CreateDirectory(sdkDir);
            var sdkOutputDir = sdkDir + Path.DirectorySeparatorChar;
            var executor = new Il2CppExecutor(metadata, il2Cpp);
            var decompiler = new Il2CppDecompiler(executor);
            decompiler.Decompile(config, sdkOutputDir);
            Console.WriteLine("Done!");
            if (config.GenerateStruct)
            {
                Console.WriteLine("Generate struct...");
                var scriptGenerator = new StructGenerator(executor);
                scriptGenerator.WriteScript(sdkOutputDir);
                Console.WriteLine("Done!");
            }
            if (config.GenerateDummyDll)
            {
                Console.WriteLine("Generate dummy dll...");
                DummyAssemblyExporter.Export(executor, outputDir, config.DummyDllAddToken);
                Console.WriteLine("Done!");
            }
        }
    }
}
