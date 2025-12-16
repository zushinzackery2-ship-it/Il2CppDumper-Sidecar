using System;
using System.Collections.Generic;
using System.Linq;

namespace Il2CppDumper
{
    public class SectionHelper
    {
        private List<SearchSection> exec;
        private List<SearchSection> data;
        private List<SearchSection> bss;
        private readonly Il2Cpp il2Cpp;
        private readonly int methodCount;
        private readonly int typeDefinitionsCount;
        private readonly long metadataUsagesCount;
        private readonly int imageCount;
        private bool pointerInExec;

        public List<SearchSection> Exec => exec;
        public List<SearchSection> Data => data;
        public List<SearchSection> Bss => bss;

        public SectionHelper(Il2Cpp il2Cpp, int methodCount, int typeDefinitionsCount, long metadataUsagesCount, int imageCount)
        {
            this.il2Cpp = il2Cpp;
            this.methodCount = methodCount;
            this.typeDefinitionsCount = typeDefinitionsCount;
            this.metadataUsagesCount = metadataUsagesCount;
            this.imageCount = imageCount;
        }

        public void SetSection(SearchSectionType type, Elf32_Phdr[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.p_offset,
                        offsetEnd = section.p_offset + section.p_filesz,
                        address = section.p_vaddr,
                        addressEnd = section.p_vaddr + section.p_memsz
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, Elf64_Phdr[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.p_offset,
                        offsetEnd = section.p_offset + section.p_filesz,
                        address = section.p_vaddr,
                        addressEnd = section.p_vaddr + section.p_memsz
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, MachoSection[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.offset,
                        offsetEnd = section.offset + section.size,
                        address = section.addr,
                        addressEnd = section.addr + section.size
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, MachoSection64Bit[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.offset,
                        offsetEnd = section.offset + section.size,
                        address = section.addr,
                        addressEnd = section.addr + section.size
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, ulong imageBase, SectionHeader[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.PointerToRawData,
                        offsetEnd = section.PointerToRawData + section.SizeOfRawData,
                        address = section.VirtualAddress + imageBase,
                        addressEnd = section.VirtualAddress + section.VirtualSize + imageBase
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, params NSOSegmentHeader[] sections)
        {
            var secs = new List<SearchSection>();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    secs.Add(new SearchSection
                    {
                        offset = section.FileOffset,
                        offsetEnd = section.FileOffset + section.DecompressedSize,
                        address = section.MemoryOffset,
                        addressEnd = section.MemoryOffset + section.DecompressedSize
                    });
                }
            }
            SetSection(type, secs);
        }

        public void SetSection(SearchSectionType type, params SearchSection[] secs)
        {
            SetSection(type, secs.ToList());
        }

        private void SetSection(SearchSectionType type, List<SearchSection> secs)
        {
            switch (type)
            {
                case SearchSectionType.Exec:
                    exec = secs;
                    break;
                case SearchSectionType.Data:
                    data = secs;
                    break;
                case SearchSectionType.Bss:
                    bss = secs;
                    break;
            }
        }

        public ulong FindCodeRegistration()
        {
            if (il2Cpp.Version >= 24.2)
            {
                ulong codeRegistration;
                if (il2Cpp is ElfBase)
                {
                    codeRegistration = FindCodeRegistrationExec();
                    if (codeRegistration == 0)
                    {
                        codeRegistration = FindCodeRegistrationData();
                    }
                    else
                    {
                        pointerInExec = true;
                    }
                }
                else
                {
                    codeRegistration = FindCodeRegistrationData();
                    if (codeRegistration == 0)
                    {
                        codeRegistration = FindCodeRegistrationExec();
                        pointerInExec = true;
                    }
                }
                if (codeRegistration == 0)
                {
                    codeRegistration = FindCodeRegistrationByCodeGenModules();
                }
                return codeRegistration;
            }
            return FindCodeRegistrationOld();
        }

        public ulong FindMetadataRegistration()
        {
            if (il2Cpp.Version < 19)
            {
                return 0;
            }
            if (il2Cpp.Version >= 27)
            {
                var mr = FindMetadataRegistrationV21();
                if (mr == 0)
                {
                    mr = FindMetadataRegistrationByMetadataUsages();
                }
                return mr;
            }
            var mrOld = FindMetadataRegistrationOld();
            if (mrOld == 0)
            {
                mrOld = FindMetadataRegistrationByMetadataUsages();
            }
            return mrOld;
        }

        private ulong FindCodeRegistrationOld()
        {
            foreach (var section in data)
            {
                il2Cpp.Position = section.offset;
                while (il2Cpp.Position < section.offsetEnd)
                {
                    var addr = il2Cpp.Position;
                    if (il2Cpp.ReadIntPtr() == methodCount)
                    {
                        try
                        {
                            var pointer = il2Cpp.MapVATR(il2Cpp.ReadUIntPtr());
                            if (CheckPointerRangeDataRa(pointer))
                            {
                                var pointers = il2Cpp.ReadClassArray<ulong>(pointer, methodCount);
                                if (CheckPointerRangeExecVa(pointers))
                                {
                                    return addr - section.offset + section.address;
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                    il2Cpp.Position = addr + il2Cpp.PointerSize;
                }
            }

            return 0ul;
        }

        private ulong FindMetadataRegistrationOld()
        {
            foreach (var section in data)
            {
                il2Cpp.Position = section.offset;
                var end = Math.Min(section.offsetEnd, il2Cpp.Length) - il2Cpp.PointerSize;
                while (il2Cpp.Position < end)
                {
                    var addr = il2Cpp.Position;
                    if (il2Cpp.ReadIntPtr() == typeDefinitionsCount)
                    {
                        try
                        {
                            il2Cpp.Position += il2Cpp.PointerSize * 2;
                            var pointer = il2Cpp.MapVATR(il2Cpp.ReadUIntPtr());
                            if (CheckPointerRangeDataRa(pointer))
                            {
                                var pointers = il2Cpp.ReadClassArray<ulong>(pointer, metadataUsagesCount);
                                if (CheckPointerRangeBssVa(pointers))
                                {
                                    return addr - il2Cpp.PointerSize * 12 - section.offset + section.address;
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                    il2Cpp.Position = addr + il2Cpp.PointerSize;
                }
            }

            return 0ul;
        }

        private ulong FindMetadataRegistrationV21()
        {
            foreach (var section in data)
            {
                il2Cpp.Position = section.offset;
                var end = Math.Min(section.offsetEnd, il2Cpp.Length) - il2Cpp.PointerSize;
                while (il2Cpp.Position < end)
                {
                    var addr = il2Cpp.Position;
                    if (il2Cpp.ReadIntPtr() == typeDefinitionsCount)
                    {
                        il2Cpp.Position += il2Cpp.PointerSize;
                        if (il2Cpp.ReadIntPtr() == typeDefinitionsCount)
                        {
                            try
                            {
                                var pointer = il2Cpp.MapVATR(il2Cpp.ReadUIntPtr());
                                if (CheckPointerRangeDataRa(pointer))
                                {
                                    var pointers = il2Cpp.ReadClassArray<ulong>(pointer, typeDefinitionsCount);
                                    bool flag;
                                    if (pointerInExec)
                                    {
                                        flag = CheckPointerRangeExecVa(pointers);
                                    }
                                    else
                                    {
                                        flag = CheckPointerRangeDataVa(pointers);
                                    }
                                    if (flag)
                                    {
                                        return addr - il2Cpp.PointerSize * 10 - section.offset + section.address;
                                    }
                                }
                            }
                            catch
                            {
                                // ignored
                            }
                        }
                    }
                    il2Cpp.Position = addr + il2Cpp.PointerSize;
                }
            }

            return 0ul;
        }

        private bool CheckPointerRangeDataRa(ulong pointer)
        {
            return data.Any(x => pointer >= x.offset && pointer <= x.offsetEnd);
        }

        private bool CheckPointerRangeExecVa(ulong[] pointers)
        {
            return pointers.All(x => exec.Any(y => x >= y.address && x <= y.addressEnd));
        }

        private bool CheckPointerRangeDataVa(ulong[] pointers)
        {
            return pointers.All(x => data.Any(y => x >= y.address && x <= y.addressEnd));
        }

        private bool CheckPointerRangeBssVa(ulong[] pointers)
        {
            return pointers.All(x => bss.Any(y => x >= y.address && x <= y.addressEnd));
        }

        private static readonly byte[] featureBytes = { 0x6D, 0x73, 0x63, 0x6F, 0x72, 0x6C, 0x69, 0x62, 0x2E, 0x64, 0x6C, 0x6C, 0x00 }; //mscorlib.dll

        private ulong FindCodeRegistrationData()
        {
            return FindCodeRegistration2019(data);
        }

        private ulong FindCodeRegistrationExec()
        {
            return FindCodeRegistration2019(exec);
        }

        private ulong FindCodeRegistration2019(List<SearchSection> secs)
        {
            foreach (var sec in secs)
            {
                il2Cpp.Position = sec.offset;
                var secLen = sec.offsetEnd - sec.offset;
                if (secLen <= 0 || secLen > int.MaxValue)
                {
                    continue;
                }
                var buff = il2Cpp.ReadBytes((int)secLen);
                foreach (var index in buff.Search(featureBytes))
                {
                    var dllva = (ulong)index + sec.address;
                    foreach (var refva in FindReference(dllva))
                    {
                        foreach (var refva2 in FindReference(refva))
                        {
                            if (il2Cpp.Version >= 27)
                            {
                                for (int i = imageCount - 1; i >= 0; i--)
                                {
                                    foreach (var refva3 in FindReference(refva2 - (ulong)i * il2Cpp.PointerSize))
                                    {
                                        il2Cpp.Position = il2Cpp.MapVATR(refva3 - il2Cpp.PointerSize);
                                        if (il2Cpp.ReadIntPtr() == imageCount)
                                        {
                                            if (il2Cpp.Version >= 29)
                                            {
                                                return refva3 - il2Cpp.PointerSize * 14;
                                            }
                                            return refva3 - il2Cpp.PointerSize * 13;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < imageCount; i++)
                                {
                                    foreach (var refva3 in FindReference(refva2 - (ulong)i * il2Cpp.PointerSize))
                                    {
                                        return refva3 - il2Cpp.PointerSize * 13;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return 0ul;
        }

        private ulong FindCodeRegistrationByCodeGenModules()
        {
            if (imageCount <= 0)
            {
                return 0ul;
            }

            foreach (var sec in data)
            {
                il2Cpp.Position = sec.offset;
                var secLen = sec.offsetEnd - sec.offset;
                if (secLen <= 0 || secLen > int.MaxValue)
                {
                    continue;
                }
                var buff = il2Cpp.ReadBytes((int)secLen);
                var stepI = (int)il2Cpp.PointerSize;
                var end = buff.Length - stepI * 2;
                for (var index = 0; index <= end; index += stepI)
                {
                    var count = stepI == 8
                        ? BitConverter.ToUInt64(buff, index)
                        : BitConverter.ToUInt32(buff, index);
                    if (count != (ulong)imageCount)
                    {
                        continue;
                    }
                    var next = index + stepI;
                    var ptr = stepI == 8
                        ? BitConverter.ToUInt64(buff, next)
                        : BitConverter.ToUInt32(buff, next);
                    if (ptr == 0)
                    {
                        continue;
                    }
                    if (!TryCheckCodeGenModulesArray(ptr))
                    {
                        continue;
                    }
                    var hit = (ulong)index + sec.address;
                    var best = FindBestCodeRegistrationStartFromHit(hit, ptr);
                    if (best != 0)
                    {
                        return best;
                    }
                }
            }
            return 0ul;
        }

        private ulong FindBestCodeRegistrationStartFromHit(ulong codeGenModulesCountAddress, ulong codeGenModules)
        {
            var bestScore = -1;
            ulong bestAddr = 0;
            var stepU = (ulong)il2Cpp.PointerSize;
            var maxBack = 64;
            for (var back = 0; back <= maxBack; back++)
            {
                var start = codeGenModulesCountAddress - (ulong)back * stepU;
                try
                {
                    var cr = il2Cpp.MapVATR<Il2CppCodeRegistration>(start);
                    if (cr.codeGenModulesCount != (ulong)imageCount)
                    {
                        continue;
                    }
                    if (cr.codeGenModules != codeGenModules)
                    {
                        continue;
                    }

                    var score = 0;
                    if (cr.invokerPointersCount > 0 && cr.invokerPointers != 0)
                    {
                        score += 2;
                        if (TryCheckPointerArrayInExec(cr.invokerPointers, cr.invokerPointersCount))
                        {
                            score += 6;
                        }
                    }
                    if (cr.genericMethodPointersCount > 0 && cr.genericMethodPointers != 0)
                    {
                        score += 2;
                        if (TryCheckPointerArrayInExec(cr.genericMethodPointers, cr.genericMethodPointersCount))
                        {
                            score += 6;
                        }
                    }
                    if (cr.reversePInvokeWrapperCount == 0 || cr.reversePInvokeWrappers != 0)
                    {
                        score += 1;
                    }
                    if (cr.unresolvedVirtualCallCount == 0 || cr.unresolvedVirtualCallPointers != 0)
                    {
                        score += 1;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAddr = start;
                    }
                }
                catch
                {
                }
            }

            return bestAddr;
        }

        private bool TryCheckPointerArrayInExec(ulong arrayPtr, ulong count)
        {
            try
            {
                if (arrayPtr == 0 || count == 0)
                {
                    return false;
                }
                var ra = il2Cpp.MapVATR(arrayPtr);
                if (ra == 0)
                {
                    return false;
                }
                var sample = (int)Math.Min((ulong)3, count);
                il2Cpp.Position = ra;
                for (var i = 0; i < sample; i++)
                {
                    var p = il2Cpp.ReadUIntPtr();
                    if (p == 0)
                    {
                        return false;
                    }
                    if (!exec.Any(y => p >= y.address && p <= y.addressEnd))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryCheckCodeGenModulesArray(ulong codeGenModules)
        {
            try
            {
                var ra = il2Cpp.MapVATR(codeGenModules);
                if (ra == 0)
                {
                    return false;
                }
                il2Cpp.Position = ra;
                var sample = Math.Min(imageCount, 3);
                for (var i = 0; i < sample; i++)
                {
                    var p = il2Cpp.ReadUIntPtr();
                    if (p == 0)
                    {
                        return false;
                    }
                    var m = il2Cpp.MapVATR<Il2CppCodeGenModule>(p);
                    var name = il2Cpp.ReadStringToNull(il2Cpp.MapVATR(m.moduleName));
                    if (string.IsNullOrEmpty(name))
                    {
                        return false;
                    }
                    if (name.IndexOf(".dll", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private ulong FindMetadataRegistrationByMetadataUsages()
        {
            if (metadataUsagesCount <= 0)
            {
                return 0ul;
            }

            foreach (var sec in data)
            {
                il2Cpp.Position = sec.offset;
                var secLen = sec.offsetEnd - sec.offset;
                if (secLen <= 0 || secLen > int.MaxValue)
                {
                    continue;
                }
                var buff = il2Cpp.ReadBytes((int)secLen);
                var stepI = (int)il2Cpp.PointerSize;
                var end = buff.Length - stepI * 2;
                for (var index = 0; index <= end; index += stepI)
                {
                    var count = stepI == 8
                        ? BitConverter.ToUInt64(buff, index)
                        : BitConverter.ToUInt32(buff, index);
                    if (count != (ulong)metadataUsagesCount)
                    {
                        continue;
                    }
                    var next = index + stepI;
                    var ptr = stepI == 8
                        ? BitConverter.ToUInt64(buff, next)
                        : BitConverter.ToUInt32(buff, next);
                    if (ptr == 0)
                    {
                        continue;
                    }
                    if (!TryCheckPointerArrayInBss(ptr, (ulong)metadataUsagesCount))
                    {
                        continue;
                    }
                    var hit = (ulong)index + sec.address;
                    var best = FindBestMetadataRegistrationStartFromHit(hit, ptr);
                    if (best != 0)
                    {
                        return best;
                    }
                }
            }
            return 0ul;
        }

        private ulong FindBestMetadataRegistrationStartFromHit(ulong metadataUsagesCountAddress, ulong metadataUsages)
        {
            var bestScore = -1;
            ulong bestAddr = 0;
            var stepU = (ulong)il2Cpp.PointerSize;
            var maxBack = 64;
            for (var back = 0; back <= maxBack; back++)
            {
                var start = metadataUsagesCountAddress - (ulong)back * stepU;
                try
                {
                    var mr = il2Cpp.MapVATR<Il2CppMetadataRegistration>(start);
                    if (mr.metadataUsagesCount != (ulong)metadataUsagesCount)
                    {
                        continue;
                    }
                    if (mr.metadataUsages != metadataUsages)
                    {
                        continue;
                    }

                    var score = 0;
                    if (mr.typesCount > 0 && mr.types != 0)
                    {
                        score += 1;
                    }
                    if (mr.fieldOffsetsCount > 0 && mr.fieldOffsets != 0)
                    {
                        score += 1;
                    }
                    if (TryCheckPointerArrayInBss(mr.metadataUsages, (ulong)metadataUsagesCount))
                    {
                        score += 6;
                    }
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAddr = start;
                    }
                }
                catch
                {
                }
            }
            return bestAddr;
        }

        private bool TryCheckPointerArrayInBss(ulong arrayPtr, ulong count)
        {
            try
            {
                if (arrayPtr == 0 || count == 0)
                {
                    return false;
                }
                var ra = il2Cpp.MapVATR(arrayPtr);
                if (ra == 0)
                {
                    return false;
                }
                var sample = (int)Math.Min((ulong)3, count);
                il2Cpp.Position = ra;
                for (var i = 0; i < sample; i++)
                {
                    var p = il2Cpp.ReadUIntPtr();
                    if (p == 0)
                    {
                        return false;
                    }
                    if (!bss.Any(y => p >= y.address && p <= y.addressEnd))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IEnumerable<ulong> FindReference(ulong addr)
        {
            foreach (var dataSec in data)
            {
                var position = dataSec.offset;
                var end = Math.Min(dataSec.offsetEnd, il2Cpp.Length) - il2Cpp.PointerSize;
                while (position < end)
                {
                    il2Cpp.Position = position;
                    if (il2Cpp.ReadUIntPtr() == addr)
                    {
                        yield return position - dataSec.offset + dataSec.address;
                    }
                    position += il2Cpp.PointerSize;
                }
            }
        }
    }
}
