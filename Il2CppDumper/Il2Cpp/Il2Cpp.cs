using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Il2CppDumper
{
    public abstract class Il2Cpp : BinaryStream
    {
        private Il2CppMetadataRegistration pMetadataRegistration;
        private Il2CppCodeRegistration pCodeRegistration;
        public ulong[] methodPointers;
        public ulong[] genericMethodPointers;
        public ulong[] invokerPointers;
        public ulong[] customAttributeGenerators;
        public ulong[] reversePInvokeWrappers;
        public ulong[] unresolvedVirtualCallPointers;
        private ulong[] fieldOffsets;
        public Il2CppType[] types;
        private readonly Dictionary<ulong, Il2CppType> typeDic = new();
        public ulong[] metadataUsages;
        private Il2CppGenericMethodFunctionsDefinitions[] genericMethodTable;
        public ulong[] genericInstPointers;
        public Il2CppGenericInst[] genericInsts;
        public Il2CppMethodSpec[] methodSpecs;
        public Dictionary<int, List<Il2CppMethodSpec>> methodDefinitionMethodSpecs = new();
        public Dictionary<Il2CppMethodSpec, ulong> methodSpecGenericMethodPointers = new();
        private bool fieldOffsetsArePointers;
        protected long metadataUsagesCount;
        public Dictionary<string, Il2CppCodeGenModule> codeGenModules;
        public Dictionary<string, ulong[]> codeGenModuleMethodPointers;
        public Dictionary<string, Dictionary<uint, Il2CppRGCTXDefinition[]>> rgctxsDictionary;
        public bool IsDumped;

        public int ExpectedImageCount { get; set; }
        public int ExpectedTypeDefinitionsCount { get; set; }
        public int ExpectedMethodCount { get; set; }

        public abstract ulong MapVATR(ulong addr);
        public abstract ulong MapRTVA(ulong addr);
        public abstract bool Search();
        public abstract bool PlusSearch(int methodCount, int typeDefinitionsCount, int imageCount);
        public abstract bool SymbolSearch();
        public abstract SectionHelper GetSectionHelper(int methodCount, int typeDefinitionsCount, int imageCount);
        public abstract bool CheckDump();

        protected Il2Cpp(Stream stream) : base(stream) { }

        public void SetProperties(double version, long metadataUsagesCount)
        {
            Version = version;
            this.metadataUsagesCount = metadataUsagesCount;
        }

        private bool IsMappable(ulong addr)
        {
            try
            {
                MapVATR(addr);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadPointerAt(ulong absAddr, out ulong value)
        {
            value = 0;
            var oldPos = Position;
            try
            {
                Position = MapVATR(absAddr);
                value = ReadUIntPtr();
                return value != 0 && IsMappable(value);
            }
            catch
            {
                return false;
            }
            finally
            {
                Position = oldPos;
            }
        }

        private int ScoreCodeRegistrationCandidate(ulong codeRegistration)
        {
            try
            {
                var reg = MapVATR<Il2CppCodeRegistration>(codeRegistration);
                if (Version >= 24.2)
                {
                    if (reg.codeGenModules == 0 || reg.codeGenModulesCount == 0)
                        return -1;
                    if (ExpectedImageCount > 0)
                    {
                        var expected = (ulong)ExpectedImageCount;
                        var min = expected > 1 ? expected / 2 : expected;
                        var max = expected * 2;
                        if (reg.codeGenModulesCount < min || reg.codeGenModulesCount > max)
                            return -1;
                    }
                }

                var score = 0;
                if (ExpectedImageCount > 0)
                {
                    var expected = (ulong)ExpectedImageCount;
                    if (reg.codeGenModulesCount == expected)
                        score += 1000;
                    else
                        score += 200;
                }
                if (reg.codeGenModules != 0 && IsMappable(reg.codeGenModules))
                    score += 200;

                if (ExpectedMethodCount > 0)
                {
                    var expected = (ulong)ExpectedMethodCount;
                    var gmpCount = reg.genericMethodPointersCount;
                    if (gmpCount > 0 && gmpCount <= expected * 200 && gmpCount <= 5000000)
                        score += 400;
                    else if (gmpCount == 0)
                        score += 10;
                    else
                        score -= 200;
                }
                if (reg.genericMethodPointers != 0 && IsMappable(reg.genericMethodPointers))
                    score += 50;

                if (reg.invokerPointers != 0 && IsMappable(reg.invokerPointers))
                    score += 50;

                return score;
            }
            catch
            {
                return -1;
            }
        }

        private int ScoreMetadataRegistrationCandidate(ulong metadataRegistration)
        {
            try
            {
                var reg = MapVATR<Il2CppMetadataRegistration>(metadataRegistration);
                if (reg.types == 0 || reg.typesCount <= 0)
                    return -1;

                var score = 0;
                if (ExpectedTypeDefinitionsCount > 0)
                {
                    var expected = (long)ExpectedTypeDefinitionsCount;
                    var diff = Math.Abs(reg.typesCount - expected);
                    if (diff == 0)
                        score += 1000;
                    else if (diff < expected / 10)
                        score += 300;
                    else
                        score -= 100;
                }
                if (IsMappable(reg.types))
                    score += 200;
                if (reg.methodSpecs != 0 && IsMappable(reg.methodSpecs))
                    score += 50;
                if (reg.fieldOffsets != 0 && IsMappable(reg.fieldOffsets))
                    score += 50;

                return score;
            }
            catch
            {
                return -1;
            }
        }

        public bool AutoPlusInit(ulong codeRegistration, ulong metadataRegistration)
        {
            var originalCodeRegistration = codeRegistration;
            var originalVersion = Version;

            if (metadataRegistration == 0)
            {
                return false;
            }

            var metadataCandidates = new List<ulong>(2);
            void addMetadataCandidate(ulong value)
            {
                if (value == 0)
                    return;
                if (!metadataCandidates.Contains(value))
                    metadataCandidates.Add(value);
            }

            addMetadataCandidate(metadataRegistration);
            if (TryReadPointerAt(metadataRegistration, out var derefMetadataRegistration))
            {
                addMetadataCandidate(derefMetadataRegistration);
            }

            var baseCodeRegistrations = new List<ulong>(2);
            void addBaseCodeRegistration(ulong value)
            {
                if (value == 0)
                    return;
                if (!baseCodeRegistrations.Contains(value))
                    baseCodeRegistrations.Add(value);
            }

            addBaseCodeRegistration(codeRegistration);
            addBaseCodeRegistration(originalCodeRegistration);
            if (TryReadPointerAt(codeRegistration, out var derefCodeRegistration))
            {
                addBaseCodeRegistration(derefCodeRegistration);
            }
            if (TryReadPointerAt(originalCodeRegistration, out var derefOriginalCodeRegistration))
            {
                addBaseCodeRegistration(derefOriginalCodeRegistration);
            }

            var candidates = new List<ulong>(5);
            void addCandidate(ulong value)
            {
                if (value == 0)
                    return;
                if (!candidates.Contains(value))
                    candidates.Add(value);
            }

            var stepSmall = (ulong)PointerSize;

            var maxDelta = (ulong)(PointerSize * 0x40);
            foreach (var baseCr in baseCodeRegistrations)
            {
                addCandidate(baseCr);
                for (ulong delta = stepSmall; delta <= maxDelta; delta += stepSmall)
                {
                    addCandidate(baseCr + delta);
                    if (baseCr >= delta)
                        addCandidate(baseCr - delta);
                }
            }

            var scoredMrs = metadataCandidates
                .Select(mr => new { mr, score = ScoreMetadataRegistrationCandidate(mr) })
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score)
                .ToList();
            if (scoredMrs.Count == 0)
            {
                scoredMrs = metadataCandidates.Select(mr => new { mr, score = 0 }).ToList();
            }

            var scoredCrs = candidates
                .Select(cr => new { cr, score = ScoreCodeRegistrationCandidate(cr) })
                .Where(x => x.score >= 0)
                .OrderByDescending(x => x.score)
                .ToList();
            if (scoredCrs.Count == 0)
            {
                scoredCrs = candidates.Select(cr => new { cr, score = 0 }).ToList();
            }

            foreach (var mrItem in scoredMrs)
            {
                var mr = mrItem.mr;
                Console.WriteLine("MetadataRegistration : {0:x}", mr);
                foreach (var crItem in scoredCrs)
                {
                    var cr = crItem.cr;
                    Console.WriteLine("CodeRegistration : {0:x}", cr);
                    try
                    {
                        Version = originalVersion;
                        Init(cr, mr);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"AutoPlusInit failed at {cr:x}: {ex.GetType().Name}");
                    }
                }
            }

            return false;
        }

        public virtual void Init(ulong codeRegistration, ulong metadataRegistration)
        {
            pCodeRegistration = MapVATR<Il2CppCodeRegistration>(codeRegistration);
            var limit = this is WebAssemblyMemory ? 0x35000u : 0x50000u; //TODO
            if (Version == 27 && pCodeRegistration.invokerPointersCount > limit)
            {
                Version = 27.1;
                Console.WriteLine($"Change il2cpp version to: {Version}");
                pCodeRegistration = MapVATR<Il2CppCodeRegistration>(codeRegistration);
            }
            if (Version == 27.1)
            {
                var pCodeGenModules = MapVATR<ulong>(pCodeRegistration.codeGenModules, pCodeRegistration.codeGenModulesCount);
                foreach (var pCodeGenModule in pCodeGenModules)
                {
                    var codeGenModule = MapVATR<Il2CppCodeGenModule>(pCodeGenModule);
                    if (codeGenModule.rgctxsCount > 0)
                    {
                        var rgctxs = MapVATR<Il2CppRGCTXDefinition>(codeGenModule.rgctxs, codeGenModule.rgctxsCount);
                        if (rgctxs.All(x => x.data.rgctxDataDummy > limit))
                        {
                            Version = 27.2;
                            Console.WriteLine($"Change il2cpp version to: {Version}");
                        }
                        break;
                    }
                }
            }
            if (Version == 24.4 && pCodeRegistration.invokerPointersCount > limit)
            {
                Version = 24.5;
                Console.WriteLine($"Change il2cpp version to: {Version}");
                pCodeRegistration = MapVATR<Il2CppCodeRegistration>(codeRegistration);
            }
            if (Version == 24.2 && pCodeRegistration.codeGenModules == 0) //TODO
            {
                Version = 24.3;
                Console.WriteLine($"Change il2cpp version to: {Version}");
                pCodeRegistration = MapVATR<Il2CppCodeRegistration>(codeRegistration);
            }

            if (Version >= 24.2)
            {
                if (pCodeRegistration.codeGenModulesCount == 0 || pCodeRegistration.codeGenModules == 0)
                {
                    throw new InvalidDataException("Invalid CodeRegistration: codeGenModules is null");
                }
                if (ExpectedImageCount > 0)
                {
                    var expected = (ulong)ExpectedImageCount;
                    var count = pCodeRegistration.codeGenModulesCount;
                    var min = expected > 1 ? expected / 2 : expected;
                    var max = expected * 2;
                    if (count < min || count > max)
                    {
                        throw new InvalidDataException($"Invalid CodeRegistration: codeGenModulesCount={count} expected~{expected}");
                    }
                }
                if (ExpectedMethodCount > 0)
                {
                    var expected = (ulong)ExpectedMethodCount;
                    var gmpCount = pCodeRegistration.genericMethodPointersCount;
                    if (gmpCount > expected * 200 && gmpCount > 2000000)
                    {
                        Console.WriteLine($"Skip genericMethodPointers: genericMethodPointersCount={gmpCount} expected~{expected}");
                        pCodeRegistration.genericMethodPointersCount = 0;
                        pCodeRegistration.genericMethodPointers = 0;
                    }
                    var invokerCount = pCodeRegistration.invokerPointersCount;
                    if (invokerCount > expected * 10 && invokerCount > 500000)
                    {
                        Console.WriteLine($"Skip invokerPointers: invokerPointersCount={invokerCount} expected~{expected}");
                        pCodeRegistration.invokerPointersCount = 0;
                        pCodeRegistration.invokerPointers = 0;
                    }
                    var ripCount = pCodeRegistration.reversePInvokeWrapperCount;
                    if (ripCount > expected * 10 && ripCount > 500000)
                    {
                        Console.WriteLine($"Skip reversePInvokeWrappers: reversePInvokeWrapperCount={ripCount} expected~{expected}");
                        pCodeRegistration.reversePInvokeWrapperCount = 0;
                        pCodeRegistration.reversePInvokeWrappers = 0;
                    }
                    var uvcCount = pCodeRegistration.unresolvedVirtualCallCount;
                    if (uvcCount > expected * 10 && uvcCount > 500000)
                    {
                        Console.WriteLine($"Skip unresolvedVirtualCallPointers: unresolvedVirtualCallCount={uvcCount} expected~{expected}");
                        pCodeRegistration.unresolvedVirtualCallCount = 0;
                        pCodeRegistration.unresolvedVirtualCallPointers = 0;
                    }
                }
            }
            Console.WriteLine($"pCodeRegistration.reversePInvokeWrapperCount={pCodeRegistration.reversePInvokeWrapperCount}");
            Console.WriteLine($"pCodeRegistration.genericMethodPointersCount={pCodeRegistration.genericMethodPointersCount}");
            Console.WriteLine($"pCodeRegistration.invokerPointersCount={pCodeRegistration.invokerPointersCount}");
            Console.WriteLine($"pCodeRegistration.codeGenModulesCount={pCodeRegistration.codeGenModulesCount}");
            Console.WriteLine($"pCodeRegistration.reversePInvokeWrappers=0x{pCodeRegistration.reversePInvokeWrappers:x}");
            Console.WriteLine($"pCodeRegistration.genericMethodPointers=0x{pCodeRegistration.genericMethodPointers:x}");
            Console.WriteLine($"pCodeRegistration.invokerPointers=0x{pCodeRegistration.invokerPointers:x}");
            Console.WriteLine($"pCodeRegistration.codeGenModules=0x{pCodeRegistration.codeGenModules:x}");
            Console.WriteLine($"pCodeRegistration.unresolvedVirtualCallCount={pCodeRegistration.unresolvedVirtualCallCount}");
            Console.WriteLine($"pCodeRegistration.unresolvedVirtualCallPointers=0x{pCodeRegistration.unresolvedVirtualCallPointers:x}");
            Console.WriteLine($"pCodeRegistration.unresolvedInstanceCallPointers=0x{pCodeRegistration.unresolvedInstanceCallPointers:x}");
            Console.WriteLine($"pCodeRegistration.unresolvedStaticCallPointers=0x{pCodeRegistration.unresolvedStaticCallPointers:x}");
            Console.WriteLine($"pCodeRegistration.interopDataCount={pCodeRegistration.interopDataCount}");
            Console.WriteLine($"pCodeRegistration.interopData=0x{pCodeRegistration.interopData:x}");
            Console.WriteLine($"pCodeRegistration.windowsRuntimeFactoryCount={pCodeRegistration.windowsRuntimeFactoryCount}");
            Console.WriteLine($"pCodeRegistration.windowsRuntimeFactoryTable=0x{pCodeRegistration.windowsRuntimeFactoryTable:x}");

            pMetadataRegistration = MapVATR<Il2CppMetadataRegistration>(metadataRegistration);

            Console.WriteLine($"pMetadataRegistration.typesCount={pMetadataRegistration.typesCount}");
            Console.WriteLine($"pMetadataRegistration.fieldOffsetsCount={pMetadataRegistration.fieldOffsetsCount}");
            Console.WriteLine($"pMetadataRegistration.methodSpecsCount={pMetadataRegistration.methodSpecsCount}");
            Console.WriteLine($"pMetadataRegistration.types=0x{pMetadataRegistration.types:x}");
            Console.WriteLine($"pMetadataRegistration.fieldOffsets=0x{pMetadataRegistration.fieldOffsets:x}");
            Console.WriteLine($"pMetadataRegistration.methodSpecs=0x{pMetadataRegistration.methodSpecs:x}");

            try
            {
                genericMethodPointers = MapVATR<ulong>(pCodeRegistration.genericMethodPointers, pCodeRegistration.genericMethodPointersCount);
            }
            catch
            {
                genericMethodPointers = Array.Empty<ulong>();
            }
            try
            {
                invokerPointers = MapVATR<ulong>(pCodeRegistration.invokerPointers, pCodeRegistration.invokerPointersCount);
            }
            catch
            {
                invokerPointers = Array.Empty<ulong>();
            }
            if (Version < 27)
            {
                customAttributeGenerators = MapVATR<ulong>(pCodeRegistration.customAttributeGenerators, pCodeRegistration.customAttributeCount);
            }
            if (Version > 16 && Version < 27)
            {
                metadataUsages = MapVATR<ulong>(pMetadataRegistration.metadataUsages, metadataUsagesCount);
            }
            if (Version >= 22)
            {
                if (pCodeRegistration.reversePInvokeWrapperCount != 0)
                {
                    try
                    {
                        reversePInvokeWrappers = MapVATR<ulong>(pCodeRegistration.reversePInvokeWrappers, pCodeRegistration.reversePInvokeWrapperCount);
                    }
                    catch
                    {
                        reversePInvokeWrappers = Array.Empty<ulong>();
                    }
                }
                if (pCodeRegistration.unresolvedVirtualCallCount != 0)
                {
                    try
                    {
                        unresolvedVirtualCallPointers = MapVATR<ulong>(pCodeRegistration.unresolvedVirtualCallPointers, pCodeRegistration.unresolvedVirtualCallCount);
                    }
                    catch
                    {
                        unresolvedVirtualCallPointers = Array.Empty<ulong>();
                    }
                }
            }
            genericInstPointers = MapVATR<ulong>(pMetadataRegistration.genericInsts, pMetadataRegistration.genericInstsCount);
            genericInsts = Array.ConvertAll(genericInstPointers, MapVATR<Il2CppGenericInst>);
            fieldOffsetsArePointers = Version > 21;
            if (Version == 21)
            {
                var fieldTest = MapVATR<uint>(pMetadataRegistration.fieldOffsets, 6);
                fieldOffsetsArePointers = fieldTest[0] == 0 && fieldTest[1] == 0 && fieldTest[2] == 0 && fieldTest[3] == 0 && fieldTest[4] == 0 && fieldTest[5] > 0;
            }
            if (fieldOffsetsArePointers)
            {
                fieldOffsets = MapVATR<ulong>(pMetadataRegistration.fieldOffsets, pMetadataRegistration.fieldOffsetsCount);
            }
            else
            {
                fieldOffsets = Array.ConvertAll(MapVATR<uint>(pMetadataRegistration.fieldOffsets, pMetadataRegistration.fieldOffsetsCount), x => (ulong)x);
            }
            var pTypes = MapVATR<ulong>(pMetadataRegistration.types, pMetadataRegistration.typesCount);
            types = new Il2CppType[pMetadataRegistration.typesCount];
            for (var i = 0; i < pMetadataRegistration.typesCount; ++i)
            {
                types[i] = MapVATR<Il2CppType>(pTypes[i]);
                types[i].Init(Version);
                typeDic.Add(pTypes[i], types[i]);
            }
            if (Version >= 24.2)
            {
                var pCodeGenModules = MapVATR<ulong>(pCodeRegistration.codeGenModules, pCodeRegistration.codeGenModulesCount);
                codeGenModules = new Dictionary<string, Il2CppCodeGenModule>(pCodeGenModules.Length, StringComparer.Ordinal);
                codeGenModuleMethodPointers = new Dictionary<string, ulong[]>(pCodeGenModules.Length, StringComparer.Ordinal);
                rgctxsDictionary = new Dictionary<string, Dictionary<uint, Il2CppRGCTXDefinition[]>>(pCodeGenModules.Length, StringComparer.Ordinal);
                foreach (var pCodeGenModule in pCodeGenModules)
                {
                    var codeGenModule = MapVATR<Il2CppCodeGenModule>(pCodeGenModule);
                    var moduleName = ReadStringToNull(MapVATR(codeGenModule.moduleName));
                    codeGenModules.Add(moduleName, codeGenModule);
                    ulong[] methodPointers;
                    try
                    {
                        methodPointers = MapVATR<ulong>(codeGenModule.methodPointers, codeGenModule.methodPointerCount);
                    }
                    catch
                    {
                        methodPointers = new ulong[codeGenModule.methodPointerCount];
                    }
                    codeGenModuleMethodPointers.Add(moduleName, methodPointers);

                    var rgctxsDefDictionary = new Dictionary<uint, Il2CppRGCTXDefinition[]>();
                    rgctxsDictionary.Add(moduleName, rgctxsDefDictionary);
                    if (codeGenModule.rgctxsCount > 0)
                    {
                        var rgctxs = MapVATR<Il2CppRGCTXDefinition>(codeGenModule.rgctxs, codeGenModule.rgctxsCount);
                        var rgctxRanges = MapVATR<Il2CppTokenRangePair>(codeGenModule.rgctxRanges, codeGenModule.rgctxRangesCount);
                        foreach (var rgctxRange in rgctxRanges)
                        {
                            var rgctxDefs = new Il2CppRGCTXDefinition[rgctxRange.range.length];
                            Array.Copy(rgctxs, rgctxRange.range.start, rgctxDefs, 0, rgctxRange.range.length);
                            rgctxsDefDictionary.Add(rgctxRange.token, rgctxDefs);
                        }
                    }
                }
            }
            else
            {
                methodPointers = MapVATR<ulong>(pCodeRegistration.methodPointers, pCodeRegistration.methodPointersCount);
            }
            genericMethodTable = MapVATR<Il2CppGenericMethodFunctionsDefinitions>(pMetadataRegistration.genericMethodTable, pMetadataRegistration.genericMethodTableCount);
            methodSpecs = MapVATR<Il2CppMethodSpec>(pMetadataRegistration.methodSpecs, pMetadataRegistration.methodSpecsCount);
            if (genericMethodPointers != null && genericMethodPointers.Length > 0)
            {
                foreach (var table in genericMethodTable)
                {
                    var methodSpec = methodSpecs[table.genericMethodIndex];
                    var methodDefinitionIndex = methodSpec.methodDefinitionIndex;
                    if (!methodDefinitionMethodSpecs.TryGetValue(methodDefinitionIndex, out var list))
                    {
                        list = new List<Il2CppMethodSpec>();
                        methodDefinitionMethodSpecs.Add(methodDefinitionIndex, list);
                    }
                    list.Add(methodSpec);
                    var idx = (int)table.indices.methodIndex;
                    if (idx >= 0 && idx < genericMethodPointers.Length)
                    {
                        methodSpecGenericMethodPointers.Add(methodSpec, genericMethodPointers[idx]);
                    }
                }
            }
        }

        public T MapVATR<T>(ulong addr) where T : new()
        {
            return ReadClass<T>(MapVATR(addr));
        }

        public T[] MapVATR<T>(ulong addr, ulong count) where T : new()
        {
            return ReadClassArray<T>(MapVATR(addr), count);
        }

        public T[] MapVATR<T>(ulong addr, long count) where T : new()
        {
            return ReadClassArray<T>(MapVATR(addr), count);
        }

        public int GetFieldOffsetFromIndex(int typeIndex, int fieldIndexInType, int fieldIndex, bool isValueType, bool isStatic)
        {
            try
            {
                var offset = -1;
                if (fieldOffsetsArePointers)
                {
                    var ptr = fieldOffsets[typeIndex];
                    if (ptr > 0)
                    {
                        Position = MapVATR(ptr) + 4ul * (ulong)fieldIndexInType;
                        offset = ReadInt32();
                    }
                }
                else
                {
                    offset = (int)fieldOffsets[fieldIndex];
                }
                if (offset > 0)
                {
                    if (isValueType && !isStatic)
                    {
                        if (Is32Bit)
                        {
                            offset -= 8;
                        }
                        else
                        {
                            offset -= 16;
                        }
                    }
                }
                return offset;
            }
            catch
            {
                return -1;
            }
        }

        public Il2CppType GetIl2CppType(ulong pointer)
        {
            if (!typeDic.TryGetValue(pointer, out var type))
            {
                return null;
            }
            return type;
        }

        public ulong GetMethodPointer(string imageName, Il2CppMethodDefinition methodDef)
        {
            if (Version >= 24.2)
            {
                var methodToken = methodDef.token;
                if (!codeGenModuleMethodPointers.TryGetValue(imageName, out var ptrs) || ptrs == null)
                {
                    return 0;
                }
                var methodPointerIndex = methodToken & 0x00FFFFFFu;
                if (methodPointerIndex == 0)
                {
                    return 0;
                }
                var idx = (int)methodPointerIndex - 1;
                if (idx < 0 || idx >= ptrs.Length)
                {
                    return 0;
                }
                return ptrs[idx];
            }
            else
            {
                var methodIndex = methodDef.methodIndex;
                if (methodIndex >= 0)
                {
                    return methodPointers[methodIndex];
                }
            }
            return 0;
        }

        public virtual ulong GetRVA(ulong pointer)
        {
            return pointer;
        }
    }
}
