using GrueGPT.Core.SaveFiles;

namespace GrueGPT.Core.Stories;

public enum Z3RestoreStatus
{
    Failed,
    ResumedAtProgramCounter,
    ResumedSaveOpcode
}

/// <summary>The result of validating and applying a Quetzal restore.</summary>
public sealed record Z3RestoreResult(Z3RestoreStatus Status, string? Error = null)
{
    public bool Succeeded => Status != Z3RestoreStatus.Failed;
}

/// <summary>Coordinates complete Version 3 execution-state saves and transactional restores.</summary>
public static class Z3SaveRestoreService
{
    private const string HostResumeChunkId = "GGPT";
    private const byte HostResumeChunkVersion = 1;

    /// <summary>Creates the Quetzal save used by the Z-machine SAVE opcode.</summary>
    public static byte[] CreateStorySaveFile(Z3ExecutionState state, int saveBranchAddress)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (saveBranchAddress < 0 || saveBranchAddress >= state.Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(saveBranchAddress));
        }

        return CreateSaveFile(state, saveBranchAddress, includeHostResumeMarker: false);
    }

    /// <summary>Creates a host-initiated save that resumes directly at the current instruction address.</summary>
    public static byte[] CreateHostSaveFile(Z3ExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return CreateSaveFile(state, state.ProgramCounter, includeHostResumeMarker: true);
    }

    /// <summary>
    /// Validates a Quetzal file completely before changing the running execution state.
    /// Unmarked Version 3 Quetzal saves resume as a successful SAVE opcode; host-created
    /// files carry a private marker and resume at their recorded program counter.
    /// </summary>
    public static Z3RestoreResult TryRestore(Z3ExecutionState state, ReadOnlySpan<byte> saveFile)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            QuetzalFile file = QuetzalFileCodec.Read(saveFile);
            int identityIndex = FindFirst(file, "IFhd");
            int memoryIndex = FindFirstMemoryChunk(file);
            int stackIndex = FindFirst(file, "Stks");
            if (identityIndex < 0 || memoryIndex < 0 || stackIndex < 0)
            {
                return Failure("The save file is missing IFhd, CMem/UMem, or Stks.");
            }

            if (identityIndex > memoryIndex || identityIndex > stackIndex)
            {
                return Failure("The IFhd chunk must precede the memory and stack chunks.");
            }

            QuetzalIfhd identity = QuetzalIfhd.Read(file.Chunks[identityIndex]);
            if (!MatchesStory(identity, state))
            {
                return Failure("The save file belongs to a different story.");
            }

            byte[] originalDynamicMemory = state.Memory.GetOriginalDynamicMemory();
            QuetzalChunk memoryChunk = file.Chunks[memoryIndex];
            byte[] restoredMemory = memoryChunk.Id switch
            {
                "CMem" => QuetzalMemoryCodec.ReadCMemChunk(memoryChunk, originalDynamicMemory),
                "UMem" => QuetzalMemoryCodec.ReadUMemChunk(memoryChunk, originalDynamicMemory.Length),
                _ => throw new InvalidDataException("The save file has no supported memory chunk."),
            };
            QuetzalStackState stacks = QuetzalStackCodec.ReadStksChunk(file.Chunks[stackIndex]);
            bool directResume = ReadHostResumeMarker(file);
            int programCounter;
            Z3Branch? resumeBranch = null;
            if (directResume)
            {
                programCounter = checked((int)identity.ProgramCounter);
            }
            else
            {
                resumeBranch = ReadSavedBranch(state.Memory, restoredMemory, identity.ProgramCounter);
                programCounter = resumeBranch.Value.AddressAfterBranchData;
                ValidateSuccessfulSaveBranch(state.Memory.Length, stacks, resumeBranch.Value);
            }

            state.ValidateStackSnapshot(stacks, programCounter);
            state.Memory.RestoreDynamicMemory(restoredMemory);
            state.RestoreStackSnapshot(stacks, programCounter);
            resumeBranch?.Apply(state, condition: true);
            return new Z3RestoreResult(directResume ? Z3RestoreStatus.ResumedAtProgramCounter : Z3RestoreStatus.ResumedSaveOpcode);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or OverflowException)
        {
            return Failure(exception.Message);
        }
    }

    private static byte[] CreateSaveFile(Z3ExecutionState state, int programCounter, bool includeHostResumeMarker)
    {
        byte[] originalDynamicMemory = state.Memory.GetOriginalDynamicMemory();
        byte[] currentDynamicMemory = state.Memory.GetCurrentDynamicMemory();
        ushort checksum = state.Header.HasChecksum
            ? state.Header.Checksum
            : state.Memory.CalculateOriginalStoryChecksum();
        QuetzalIfhd identity = new(
            state.Header.ReleaseNumber,
            state.Memory.GetOriginalSerialNumber(),
            checksum,
            checked((uint)programCounter));

        List<QuetzalChunk> chunks =
        [
            identity.ToChunk(),
            QuetzalMemoryCodec.CreateCMemChunk(originalDynamicMemory, currentDynamicMemory),
            QuetzalStackCodec.CreateStksChunk(state.CreateStackSnapshot()),
        ];
        if (includeHostResumeMarker)
        {
            chunks.Add(new QuetzalChunk(HostResumeChunkId, [HostResumeChunkVersion]));
        }

        return QuetzalFileCodec.Write(new QuetzalFile(chunks));
    }

    private static bool MatchesStory(QuetzalIfhd identity, Z3ExecutionState state)
    {
        byte[] serialNumber = state.Memory.GetOriginalSerialNumber();
        ushort checksum = state.Header.HasChecksum
            ? state.Header.Checksum
            : state.Memory.CalculateOriginalStoryChecksum();
        return identity.ReleaseNumber == state.Header.ReleaseNumber
            && identity.SerialNumber.Span.SequenceEqual(serialNumber)
            && identity.Checksum == checksum;
    }

    private static int FindFirst(QuetzalFile file, string chunkId)
    {
        for (int index = 0; index < file.Chunks.Count; index++)
        {
            if (string.Equals(file.Chunks[index].Id, chunkId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindFirstMemoryChunk(QuetzalFile file)
    {
        for (int index = 0; index < file.Chunks.Count; index++)
        {
            if (file.Chunks[index].Id is "CMem" or "UMem")
            {
                return index;
            }
        }

        return -1;
    }

    private static bool ReadHostResumeMarker(QuetzalFile file)
    {
        int markerIndex = FindFirst(file, HostResumeChunkId);
        if (markerIndex < 0)
        {
            return false;
        }

        ReadOnlySpan<byte> data = file.Chunks[markerIndex].Data.Span;
        if (data.Length != 1 || data[0] != HostResumeChunkVersion)
        {
            throw new InvalidDataException("The host resume marker is invalid or unsupported.");
        }

        return true;
    }

    private static Z3Branch ReadSavedBranch(Z3Memory memory, ReadOnlySpan<byte> restoredDynamicMemory, uint programCounter)
    {
        int address = checked((int)programCounter);
        byte first = ReadRestoredByte(memory, restoredDynamicMemory, address);
        bool branchOnTrue = (first & 0x80) != 0;
        int offset;
        int addressAfterBranchData;
        if ((first & 0x40) != 0)
        {
            offset = first & 0x3F;
            addressAfterBranchData = address + 1;
        }
        else
        {
            byte second = ReadRestoredByte(memory, restoredDynamicMemory, address + 1);
            int encodedOffset = ((first & 0x3F) << 8) | second;
            offset = (encodedOffset & 0x2000) == 0 ? encodedOffset : encodedOffset - 0x4000;
            addressAfterBranchData = address + 2;
        }

        if (addressAfterBranchData >= memory.Length)
        {
            throw new InvalidDataException("The saved SAVE branch extends beyond the story image.");
        }

        return new Z3Branch(branchOnTrue, offset, addressAfterBranchData);
    }

    private static byte ReadRestoredByte(Z3Memory memory, ReadOnlySpan<byte> restoredDynamicMemory, int address)
    {
        if (address < 0 || address >= memory.Length)
        {
            throw new InvalidDataException("The saved SAVE branch address is outside the story image.");
        }

        return address < restoredDynamicMemory.Length
            ? restoredDynamicMemory[address]
            : memory.ReadOriginalByte(address);
    }

    private static void ValidateSuccessfulSaveBranch(int storyLength, QuetzalStackState stacks, Z3Branch branch)
    {
        if (!branch.BranchOnTrue)
        {
            return;
        }

        if (branch.Offset is 0 or 1)
        {
            if (stacks.Frames.Count == 0)
            {
                throw new InvalidDataException("The saved SAVE branch returns from a missing call frame.");
            }

            return;
        }

        int target = branch.AddressAfterBranchData + branch.Offset - 2;
        if (target < 0 || target >= storyLength)
        {
            throw new InvalidDataException("The saved SAVE branch target is outside the story image.");
        }
    }

    private static Z3RestoreResult Failure(string message) => new(Z3RestoreStatus.Failed, message);
}
