using System.Globalization;
using GrueGPT.Core.Resources;
using GrueGPT.Core.Runtime;

namespace GrueGPT.Core.Stories;

/// <summary>Executes Version 3 instructions using the shared story state and host I/O.</summary>
public sealed class Z3OpcodeExecutor
{
    private readonly Z3ExecutionState _state;
    private readonly Z3InstructionDecoder _decoder;
    private readonly Z3ObjectTable _objects;
    private readonly Z3TextDecoder _textDecoder;
    private readonly IInterpreterIO _io;
    private readonly IZ3StreamIO? _streamIO;
    private readonly IZ3ScreenIO? _screenIO;
    private readonly IZ3SaveRestoreIO? _saveRestoreIO;
    private readonly IZ3SoundIO? _soundIO;
    private readonly BlorbFile? _blorb;
    private readonly Dictionary<uint, byte[]> _preparedSounds = [];
    private readonly Z3ScreenModel _screen;
    private readonly Z3RandomNumberGenerator _random;
    private readonly Stack<Z3MemoryOutputFrame> _memoryOutputStreams = new();
    private bool _screenOutputSelected = true;
    private bool _recordCommands;
    private byte _inputStream;
    private bool _halted;
    private int? _activeReadInstructionAddress;

    public Z3OpcodeExecutor(
        Z3ExecutionState state,
        IInterpreterIO io,
        Z3RandomNumberGenerator? randomNumberGenerator = null,
        Z3ScreenModel? screenModel = null,
        BlorbFile? blorb = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(io);
        _state = state;
        _io = io;
        _streamIO = io as IZ3StreamIO;
        _screenIO = io as IZ3ScreenIO;
        _saveRestoreIO = io as IZ3SaveRestoreIO;
        _soundIO = io as IZ3SoundIO;
        _blorb = blorb;
        _screen = screenModel ?? new Z3ScreenModel();
        _decoder = new Z3InstructionDecoder(state);
        _objects = new Z3ObjectTable(state.Memory, state.Header.ObjectTableAddress);
        _textDecoder = new Z3TextDecoder(state.Memory, state.Header.AbbreviationsAddress);
        _random = randomNumberGenerator ?? new Z3RandomNumberGenerator();

        if (_streamIO is null)
        {
            byte flags2LowByte = _state.Memory.ReadByte(Z3Memory.Flags2Address + 1);
            _state.Memory.WriteByte(Z3Memory.Flags2Address + 1, (byte)(flags2LowByte & ~0x01));
        }

        _state.Memory.SetInterpreterFlags1(0x30, 0x20);
    }

    public bool IsHalted => _halted;

    /// <summary>The current Version 3 output window selected by set_window.</summary>
    public byte CurrentWindow => _screen.CurrentWindow;

    /// <summary>The current upper-window height selected by split_window.</summary>
    public ushort UpperWindowHeight => checked((ushort)_screen.UpperWindowHeight);

    /// <summary>The character-cell screen state exposed for hosts and tests.</summary>
    public Z3ScreenSnapshot Screen => _screen.CreateSnapshot();

    /// <summary>Saves the current state for a host menu action. A save requested at a command prompt repeats its pending read.</summary>
    public async ValueTask<bool> SaveFromHostAsync(CancellationToken cancellationToken = default)
    {
        if (_saveRestoreIO is null)
        {
            return false;
        }

        byte[] saveFile = Z3SaveRestoreService.CreateHostSaveFile(_state, _activeReadInstructionAddress);
        return await _saveRestoreIO.SaveGameAsync(saveFile, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Restores a state selected by a host menu action. Call between instruction executions.</summary>
    public async ValueTask<bool> RestoreFromHostAsync(CancellationToken cancellationToken = default)
    {
        if (_saveRestoreIO is null)
        {
            return false;
        }

        byte[]? saveFile = await _saveRestoreIO.LoadGameAsync(cancellationToken).ConfigureAwait(false);
        if (saveFile is null)
        {
            return false;
        }

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(_state, saveFile);
        if (!result.Succeeded)
        {
            await _io.ReportErrorAsync(result.Error!, cancellationToken).ConfigureAwait(false);
            return false;
        }

        _halted = false;
        await UpdateStatusAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Decodes and executes one instruction; returns false after quit.</summary>
    public async ValueTask<bool> ExecuteNextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_halted)
        {
            return false;
        }

        Z3DecodedInstruction instruction = _decoder.DecodeNext(evaluateVariableOperands: false);
        await ExecuteInstructionAsync(instruction, cancellationToken).ConfigureAwait(false);
        return !_halted;
    }

    /// <summary>Executes instructions until quit or an opcode owned by a later task is reached.</summary>
    public async ValueTask RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (await ExecuteNextAsync(cancellationToken).ConfigureAwait(false))
            {
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _io.ReportErrorAsync(
                $"Story execution stopped at 0x{_state.ProgramCounter:X4}: {exception.Message}",
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask ExecuteInstructionAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        switch (instruction.OperandCount)
        {
            case Z3OperandCount.Zero:
                await ExecuteZeroOperandAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            case Z3OperandCount.One:
                await ExecuteOneOperandAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            case Z3OperandCount.Two:
                await ExecuteTwoOperandAsync(instruction).ConfigureAwait(false);
                break;
            case Z3OperandCount.Variable:
                await ExecuteVariableOperandAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidDataException($"Unsupported operand count {instruction.OperandCount}.");
        }
    }

    private async ValueTask ExecuteZeroOperandAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        switch (instruction.OpcodeNumber)
        {
            case 0:
                _state.ReturnFromRoutine(1);
                break;
            case 1:
                _state.ReturnFromRoutine(0);
                break;
            case 2:
                await WriteAsync(DecodeInlineText(instruction), cancellationToken).ConfigureAwait(false);
                break;
            case 3:
                await WriteAsync(DecodeInlineText(instruction), cancellationToken).ConfigureAwait(false);
                await WriteAsync("\n", cancellationToken).ConfigureAwait(false);
                _state.ReturnFromRoutine(1);
                break;
            case 4:
                break;
            case 5:
                await ExecuteSaveInstructionAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            case 6:
                await ExecuteRestoreInstructionAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            case 7:
                await RestartAsync(cancellationToken).ConfigureAwait(false);
                break;
            case 8:
                _state.ReturnFromRoutine(_state.PopStack());
                break;
            case 9:
                _ = _state.PopStack();
                break;
            case 10:
                _halted = true;
                break;
            case 11:
                await WriteAsync("\n", cancellationToken).ConfigureAwait(false);
                break;
            case 12:
                await UpdateStatusAsync(cancellationToken).ConfigureAwait(false);
                break;
            case 13:
                instruction.Branch?.Apply(_state, VerifyChecksum());
                break;
            default:
                throw UnknownOpcode(instruction);
        }
    }

    private async ValueTask ExecuteOneOperandAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        switch (instruction.OpcodeNumber)
        {
            case 0:
                instruction.Branch?.Apply(_state, GetValue(instruction, 0) == 0);
                break;
            case 1:
            case 2:
                {
                    byte value = instruction.OpcodeNumber == 1
                        ? _objects.GetSibling(GetValue(instruction, 0).ToObjectNumber())
                        : _objects.GetChild(GetValue(instruction, 0).ToObjectNumber());
                    StoreResult(instruction, value);
                    instruction.Branch?.Apply(_state, value != 0);
                    break;
                }
            case 3:
                StoreResult(instruction, _objects.GetParent(GetValue(instruction, 0).ToObjectNumber()));
                break;
            case 4:
                StoreResult(instruction, _objects.GetPropertyLength(GetValue(instruction, 0)));
                break;
            case 5:
            case 6:
                {
                    byte variableNumber = GetVariableNumber(instruction, 0);
                    ushort current = _state.ReadVariableIndirect(variableNumber);
                    ushort next = unchecked((ushort)(current + (instruction.OpcodeNumber == 5 ? 1 : -1)));
                    _state.WriteVariableIndirect(variableNumber, next);
                    break;
                }
            case 7:
                await WriteAsync(_textDecoder.DecodeAt(GetValue(instruction, 0)), cancellationToken).ConfigureAwait(false);
                break;
            case 9:
                _objects.RemoveObject(GetValue(instruction, 0).ToObjectNumber());
                break;
            case 10:
                await WriteAsync(_objects.GetShortName(GetValue(instruction, 0).ToObjectNumber(), _textDecoder), cancellationToken).ConfigureAwait(false);
                break;
            case 11:
                _state.ReturnFromRoutine(GetValue(instruction, 0));
                break;
            case 12:
                _state.Jump(GetValue(instruction, 0));
                break;
            case 13:
                await WriteAsync(_textDecoder.DecodePackedAddress(GetValue(instruction, 0)), cancellationToken).ConfigureAwait(false);
                break;
            case 14:
                StoreResult(instruction, _state.ReadVariableIndirect(GetVariableNumber(instruction, 0)));
                break;
            case 15:
                StoreResult(instruction, (ushort)~GetValue(instruction, 0));
                break;
            default:
                throw UnknownOpcode(instruction);
        }
    }

    private ValueTask ExecuteTwoOperandAsync(Z3DecodedInstruction instruction)
    {
        switch (instruction.OpcodeNumber)
        {
            case 1:
                {
                    ushort first = GetValue(instruction, 0);
                    bool equal = false;
                    for (int index = 1; index < instruction.Operands.Count; index++)
                    {
                        equal |= GetValue(instruction, index) == first;
                    }

                    instruction.Branch?.Apply(_state, equal);
                    break;
                }
            case 2:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    instruction.Branch?.Apply(_state, AsSigned(first) < AsSigned(second));
                    break;
                }
            case 3:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    instruction.Branch?.Apply(_state, AsSigned(first) > AsSigned(second));
                    break;
                }
            case 4:
            case 5:
                {
                    byte variableNumber = GetVariableNumber(instruction, 0);
                    ushort comparison = GetValue(instruction, 1);
                    ushort current = _state.ReadVariableIndirect(variableNumber);
                    ushort changed = unchecked((ushort)(current + (instruction.OpcodeNumber == 4 ? -1 : 1)));
                    _state.WriteVariableIndirect(variableNumber, changed);
                    bool branch = instruction.OpcodeNumber == 4
                        ? AsSigned(changed) < AsSigned(comparison)
                        : AsSigned(changed) > AsSigned(comparison);
                    instruction.Branch?.Apply(_state, branch);
                    break;
                }
            case 6:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    bool isDirectChild = first != 0 && second != 0
                        && _objects.GetParent(first.ToObjectNumber()) == second.ToObjectNumber();
                    instruction.Branch?.Apply(_state, isDirectChild);
                    break;
                }
            case 7:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    instruction.Branch?.Apply(_state, (first & second) == second);
                    break;
                }
            case 8:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, (ushort)(first | second));
                    break;
                }
            case 9:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, (ushort)(first & second));
                    break;
                }
            case 10:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    bool hasAttribute = _objects.TestAttribute(first.ToObjectNumber(), second.ToAttributeNumber());
                    instruction.Branch?.Apply(_state, hasAttribute);
                    break;
                }
            case 11:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    _objects.SetAttribute(first.ToObjectNumber(), second.ToAttributeNumber());
                    break;
                }
            case 12:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    _objects.ClearAttribute(first.ToObjectNumber(), second.ToAttributeNumber());
                    break;
                }
            case 13:
                _state.WriteVariable(GetVariableNumber(instruction, 0), GetValue(instruction, 1));
                break;
            case 14:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    _objects.InsertObject(first.ToObjectNumber(), second.ToObjectNumber());
                    break;
                }
            case 15:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, _state.Memory.ReadWord(checked(first + (2 * (int)second))));
                    break;
                }
            case 16:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, _state.Memory.ReadByte(checked(first + (int)second)));
                    break;
                }
            case 17:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, _objects.GetProperty(first.ToObjectNumber(), second.ToPropertyNumber()));
                    break;
                }
            case 18:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, _objects.GetPropertyAddress(first.ToObjectNumber(), second.ToPropertyNumber()));
                    break;
                }
            case 19:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, _objects.GetNextProperty(first.ToObjectNumber(), second.ToPropertyNumber(allowZero: true)));
                    break;
                }
            case 20:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, unchecked((ushort)(first + second)));
                    break;
                }
            case 21:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, unchecked((ushort)(first - second)));
                    break;
                }
            case 22:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, unchecked((ushort)(AsSigned(first) * AsSigned(second))));
                    break;
                }
            case 23:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, Divide(first, second));
                    break;
                }
            case 24:
                {
                    ushort first = GetValue(instruction, 0);
                    ushort second = GetValue(instruction, 1);
                    StoreResult(instruction, Modulo(first, second));
                    break;
                }
            default:
                throw UnknownOpcode(instruction);
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask ExecuteVariableOperandAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        switch (instruction.OpcodeNumber)
        {
            case 0:
                {
                    ushort packedRoutine = GetValue(instruction, 0);
                    ushort[] arguments = instruction.Operands.Skip(1).Select(GetValue).ToArray();
                    _state.CallRoutine(packedRoutine, arguments, RequireStoreVariable(instruction));
                    break;
                }
            case 1:
                {
                    int address = checked(GetValue(instruction, 0) + (2 * (int)GetValue(instruction, 1)));
                    _state.Memory.WriteWord(address, GetValue(instruction, 2));
                    break;
                }
            case 2:
                {
                    int address = checked(GetValue(instruction, 0) + (int)GetValue(instruction, 1));
                    _state.Memory.WriteByte(address, (byte)GetValue(instruction, 2));
                    break;
                }
            case 3:
                _objects.PutProperty(
                    GetValue(instruction, 0).ToObjectNumber(),
                    GetValue(instruction, 1).ToPropertyNumber(),
                    GetValue(instruction, 2));
                break;
            case 4:
                await ReadLineAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            case 5:
                await WriteAsync(DecodeOutputCharacter(GetValue(instruction, 0)), cancellationToken).ConfigureAwait(false);
                break;
            case 6:
                await WriteAsync(AsSigned(GetValue(instruction, 0)).ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
                break;
            case 7:
                StoreResult(instruction, _random.Next(GetValue(instruction, 0)));
                break;
            case 8:
                _state.PushStack(GetValue(instruction, 0));
                break;
            case 9:
                _state.WriteVariable(GetVariableNumber(instruction, 0), _state.PopStack());
                break;
            case 10:
                _screen.SplitWindow(GetValue(instruction, 0));
                await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
                break;
            case 11:
                {
                    ushort window = GetValue(instruction, 0);
                    if (window > 1)
                    {
                        throw new InvalidOperationException("Version 3 supports output windows 0 and 1 only.");
                    }

                    _screen.SelectWindow((byte)window);
                    await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
            case 19:
                SelectOutputStream(instruction);
                break;
            case 20:
                SelectInputStream(GetValue(instruction, 0));
                break;
            case 21:
                await ExecuteSoundEffectAsync(instruction, cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw UnknownOpcode(instruction);
        }

    }

    private async ValueTask ExecuteSoundEffectAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        if (_soundIO is null || _blorb is null || instruction.Operands.Count < 2)
        {
            return;
        }

        uint number = GetValue(instruction, 0);
        ushort action = GetValue(instruction, 1);
        switch (action)
        {
            case 1: // Prepare.
                if (number != 0 && _blorb.FindResource("Snd ", number) is BlorbResource prepareResource
                    && prepareResource.Chunk.Id == "FORM")
                {
                    _preparedSounds[number] = BlorbAiffCodec.DecodeToWave(prepareResource);
                }

                break;
            case 2: // Start.
                if (number == 0)
                {
                    return;
                }

                if (!_preparedSounds.TryGetValue(number, out byte[]? waveData))
                {
                    BlorbResource? resource = _blorb.FindResource("Snd ", number);
                    if (resource is null || resource.Chunk.Id != "FORM")
                    {
                        return;
                    }

                    waveData = BlorbAiffCodec.DecodeToWave(resource);
                }

                bool repeats = _blorb.Metadata.SoundLoops.FirstOrDefault(loop => loop.SoundNumber == number)?.Repeats == true;
                await _soundIO.PlaySoundAsync(number, waveData, repeats, cancellationToken).ConfigureAwait(false);
                break;
            case 3: // Stop.
                await _soundIO.StopSoundAsync(number, cancellationToken).ConfigureAwait(false);
                break;
            case 4: // Finish.
                if (number == 0)
                {
                    _preparedSounds.Clear();
                }
                else
                {
                    _preparedSounds.Remove(number);
                }

                break;
        }
    }

    private ushort GetValue(Z3DecodedInstruction instruction, int index) => GetValue(instruction.Operands[index]);

    private ushort GetValue(Z3Operand operand)
    {
        if (operand.Type != Z3OperandType.Variable)
        {
            return operand.Value;
        }

        byte variableNumber = operand.VariableNumber
            ?? throw new InvalidDataException("A variable operand has no variable number.");
        return operand.IsValueEvaluated ? operand.Value : _state.ReadVariableValue(variableNumber);
    }

    private static byte GetVariableNumber(Z3DecodedInstruction instruction, int index)
    {
        Z3Operand operand = instruction.Operands[index];
        int variableNumber = operand.Type == Z3OperandType.Variable
            ? operand.VariableNumber
                ?? throw new InvalidDataException("A variable operand has no variable number.")
            : operand.Value;
        if (variableNumber > byte.MaxValue)
        {
            throw new InvalidDataException("An indirect variable reference must fit in one byte.");
        }

        return (byte)variableNumber;
    }

    private static byte RequireStoreVariable(Z3DecodedInstruction instruction) =>
        instruction.StoreVariable
            ?? throw new InvalidDataException("The instruction is missing its required store variable.");

    private void StoreResult(Z3DecodedInstruction instruction, ushort value) =>
        _state.WriteVariable(RequireStoreVariable(instruction), value);

    private static short AsSigned(ushort value) => unchecked((short)value);

    private static ushort Divide(ushort dividend, ushort divisor)
    {
        short signedDivisor = AsSigned(divisor);
        if (signedDivisor == 0)
        {
            throw new InvalidOperationException("The Z-machine divide opcode cannot divide by zero.");
        }

        int result = AsSigned(dividend) / signedDivisor;
        return unchecked((ushort)result);
    }

    private static ushort Modulo(ushort dividend, ushort divisor)
    {
        short signedDivisor = AsSigned(divisor);
        if (signedDivisor == 0)
        {
            throw new InvalidOperationException("The Z-machine modulo opcode cannot divide by zero.");
        }

        int result = AsSigned(dividend) % signedDivisor;
        return unchecked((ushort)result);
    }

    private string DecodeInlineText(Z3DecodedInstruction instruction)
    {
        int address = instruction.InlineTextAddress
            ?? throw new InvalidDataException("The print instruction is missing its inline text.");
        return _textDecoder.DecodeAt(address);
    }

    private string DecodeOutputCharacter(ushort value)
    {
        if (value == 0)
        {
            return string.Empty;
        }

        if (value == 13 || value is >= 32 and <= 126 || value is >= 155 and <= 223)
        {
            return Z3TextDecoder.ToUnicodeCharacter(value) is char character
                ? character.ToString()
                : string.Empty;
        }

        throw new InvalidDataException($"ZSCII code {value} is not defined for Version 3 output.");
    }

    private async ValueTask ReadLineAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        ushort textBufferAddress = GetValue(instruction, 0);
        ushort parseBufferAddress = GetValue(instruction, 1);
        int maximumCharacters = _state.Memory.ReadByte(textBufferAddress);
        if (maximumCharacters < 1)
        {
            throw new InvalidDataException("A Version 3 text buffer must have room for at least one character and its terminator.");
        }

        ValidateInputBuffer(textBufferAddress, maximumCharacters + 2, "text");
        int maximumWords = _state.Memory.ReadByte(parseBufferAddress);
        if (maximumWords < 1)
        {
            throw new InvalidDataException("A Version 3 parse buffer must have room for at least one token.");
        }

        ValidateInputBuffer(parseBufferAddress, 2 + (maximumWords * 4), "parse");
        _activeReadInstructionAddress = instruction.Address;
        string? inputLine;
        try
        {
            await UpdateStatusAsync(cancellationToken).ConfigureAwait(false);
            _screen.BeginInput();
            await PresentScreenAsync(cancellationToken).ConfigureAwait(false);

            inputLine = _inputStream == 0
                ? await _io.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                : await GetStreamIO().ReadPlaybackCommandAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _activeReadInstructionAddress = null;
        }

        if (inputLine is null)
        {
            _screen.EndInput();
            await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
            _halted = true;
            return;
        }

        string line = inputLine.TrimEnd('\r', '\n');
        if (line.Length > maximumCharacters)
        {
            line = line[..maximumCharacters];
        }

        await EchoInputLineAsync(line, cancellationToken).ConfigureAwait(false);
        if (_recordCommands)
        {
            await GetStreamIO().WriteRecordedCommandAsync(line, cancellationToken).ConfigureAwait(false);
        }

        for (int index = 0; index < line.Length; index++)
        {
            char lowerCaseCharacter = char.ToLowerInvariant(line[index]);
            _state.Memory.WriteByte(
                textBufferAddress + 1 + index,
                Z3TextDecoder.ToZsciiCharacter(lowerCaseCharacter));
        }

        _state.Memory.WriteByte(textBufferAddress + 1 + line.Length, 0);
        _ = new Z3Tokenizer(_state.Memory, _state.Header.DictionaryAddress)
            .TokenizeTextBuffer(textBufferAddress, parseBufferAddress);
    }

    private void ValidateInputBuffer(ushort address, int length, string bufferName)
    {
        if (length < (bufferName == "text" ? 3 : 6)
            || address < Z3StoryLoader.HeaderSize
            || address > _state.Memory.StaticMemoryBase - length
            || address + length > _state.Memory.Length)
        {
            throw new InvalidDataException($"The Version 3 {bufferName} buffer is too small or is outside dynamic memory.");
        }
    }

    private async ValueTask EchoInputLineAsync(string line, CancellationToken cancellationToken)
    {
        string text = $"{line}\n";
        _screen.EndInput();
        if (_screenOutputSelected)
        {
            _screen.WriteText(text);
            await _io.WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
        }

        await PresentScreenAsync(cancellationToken).ConfigureAwait(false);

        if (IsTranscriptSelected)
        {
            await GetStreamIO().WriteTranscriptAsync(text, cancellationToken).ConfigureAwait(false);
        }
    }

    private void SelectInputStream(ushort encodedStream)
    {
        short stream = AsSigned(encodedStream);
        if (stream is not (0 or 1))
        {
            throw new InvalidOperationException($"Version 3 input stream {stream} is not supported.");
        }

        if (stream == 1)
        {
            _ = GetStreamIO();
        }

        _inputStream = (byte)stream;
    }

    private void SelectOutputStream(Z3DecodedInstruction instruction)
    {
        short selectedStream = AsSigned(GetValue(instruction, 0));
        if (selectedStream == 0)
        {
            return;
        }

        bool enable = selectedStream > 0;
        int streamNumber = Math.Abs((int)selectedStream);
        switch (streamNumber)
        {
            case 1:
                _screenOutputSelected = enable;
                break;
            case 2:
                if (enable)
                {
                    _ = GetStreamIO();
                }

                byte flags2LowByte = _state.Memory.ReadByte(Z3Memory.Flags2Address + 1);
                _state.Memory.WriteByte(
                    Z3Memory.Flags2Address + 1,
                    enable ? (byte)(flags2LowByte | 0x01) : (byte)(flags2LowByte & ~0x01));
                break;
            case 3:
                if (enable)
                {
                    if (instruction.Operands.Count < 2)
                    {
                        throw new InvalidDataException("Selecting Version 3 output stream 3 requires a table address.");
                    }

                    if (_memoryOutputStreams.Count == 16)
                    {
                        throw new InvalidOperationException("Version 3 output stream 3 supports at most 16 nested tables.");
                    }

                    int tableAddress = GetValue(instruction, 1);
                    if (tableAddress < Z3StoryLoader.HeaderSize
                        || tableAddress >= _state.Memory.StaticMemoryBase - sizeof(ushort))
                    {
                        throw new InvalidDataException("A Version 3 output stream 3 table must begin where its count word fits in dynamic memory.");
                    }

                    _memoryOutputStreams.Push(new Z3MemoryOutputFrame(tableAddress));
                }
                else if (_memoryOutputStreams.TryPop(out Z3MemoryOutputFrame? frame))
                {
                    _state.Memory.WriteWord(frame.TableAddress, unchecked((ushort)frame.CharacterCount));
                }

                break;
            case 4:
                if (enable)
                {
                    _ = GetStreamIO();
                }

                _recordCommands = enable;
                break;
            default:
                throw new InvalidOperationException($"Version 3 output stream {selectedStream} is not supported.");
        }
    }

    private bool IsTranscriptSelected => (_state.Memory.ReadByte(Z3Memory.Flags2Address + 1) & 0x01) != 0;

    private IZ3StreamIO GetStreamIO() => _streamIO
        ?? throw new NotSupportedException("This host does not provide transcript or command-file streams.");

    private async ValueTask RestartAsync(CancellationToken cancellationToken)
    {
        _state.Restart();
        _random.Reset();
        _screenOutputSelected = true;
        _recordCommands = false;
        _inputStream = 0;
        _memoryOutputStreams.Clear();
        if (_streamIO is null)
        {
            byte flags2LowByte = _state.Memory.ReadByte(Z3Memory.Flags2Address + 1);
            _state.Memory.WriteByte(Z3Memory.Flags2Address + 1, (byte)(flags2LowByte & ~0x01));
        }

        _screen.Reset();
        _state.Memory.SetInterpreterFlags1(0x30, 0x20);
        _halted = false;
        await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ExecuteSaveInstructionAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        bool succeeded = false;
        if (_saveRestoreIO is not null)
        {
            byte[] saveFile = Z3SaveRestoreService.CreateStorySaveFile(_state, instruction.Address + 1);
            succeeded = await _saveRestoreIO.SaveGameAsync(saveFile, cancellationToken).ConfigureAwait(false);
        }

        instruction.Branch?.Apply(_state, succeeded);
    }

    private async ValueTask ExecuteRestoreInstructionAsync(Z3DecodedInstruction instruction, CancellationToken cancellationToken)
    {
        byte[]? saveFile = _saveRestoreIO is null
            ? null
            : await _saveRestoreIO.LoadGameAsync(cancellationToken).ConfigureAwait(false);
        if (saveFile is null)
        {
            instruction.Branch?.Apply(_state, condition: false);
            return;
        }

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(_state, saveFile);
        if (!result.Succeeded)
        {
            instruction.Branch?.Apply(_state, condition: false);
            await _io.ReportErrorAsync(result.Error!, cancellationToken).ConfigureAwait(false);
            return;
        }

        _halted = false;
        await UpdateStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask UpdateStatusAsync(CancellationToken cancellationToken)
    {
        ushort objectNumber = _state.Memory.ReadWord(_state.Header.GlobalVariablesAddress);
        string location = objectNumber == 0
            ? string.Empty
            : _objects.GetShortName(objectNumber.ToObjectNumber(), _textDecoder);
        ushort scoreOrHour = _state.Memory.ReadWord(_state.Header.GlobalVariablesAddress + sizeof(ushort));
        ushort movesOrMinute = _state.Memory.ReadWord(_state.Header.GlobalVariablesAddress + (2 * sizeof(ushort)));
        bool isTimeGame = (_state.Memory.ReadByte(Z3Memory.Flags1Address) & 0x02) != 0;
        StoryStatus status = new(location, unchecked((short)scoreOrHour), movesOrMinute, isTimeGame);
        _screen.SetStatus(status);
        await _io.UpdateStatusAsync(status, cancellationToken).ConfigureAwait(false);
        await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
    }

    private bool VerifyChecksum()
    {
        if (!_state.Header.HasChecksum)
        {
            return false;
        }

        uint checksum = 0;
        for (int address = Z3StoryLoader.HeaderSize; address < _state.Header.FileLength; address++)
        {
            checksum += _state.Memory.ReadOriginalByte(address);
        }

        return unchecked((ushort)checksum) == _state.Header.Checksum;
    }

    private async ValueTask WriteAsync(string text, CancellationToken cancellationToken)
    {
        if (_memoryOutputStreams.TryPeek(out Z3MemoryOutputFrame? memoryOutput))
        {
            foreach (char character in text)
            {
                int address = checked(memoryOutput.TableAddress + sizeof(ushort) + memoryOutput.CharacterCount);
                _state.Memory.WriteByte(address, Z3TextDecoder.ToZsciiCharacter(character));
                memoryOutput.CharacterCount++;
            }

            return;
        }

        if (_screenOutputSelected)
        {
            _screen.WriteText(text);
            await PresentScreenAsync(cancellationToken).ConfigureAwait(false);
            await _io.WriteTextAsync(text, cancellationToken).ConfigureAwait(false);
        }

        if (IsTranscriptSelected)
        {
            await GetStreamIO().WriteTranscriptAsync(text, cancellationToken).ConfigureAwait(false);
        }

    }

    private ValueTask PresentScreenAsync(CancellationToken cancellationToken) => _screenIO is null
        ? ValueTask.CompletedTask
        : _screenIO.PresentScreenAsync(_screen.CreateSnapshot(), cancellationToken);

    private static InvalidDataException UnknownOpcode(Z3DecodedInstruction instruction) =>
        new($"Opcode {instruction.Form}:{instruction.OpcodeNumber} is not a legal Version 3 instruction.");

    private sealed class Z3MemoryOutputFrame(int tableAddress)
    {
        public int TableAddress { get; } = tableAddress;

        public int CharacterCount { get; set; }
    }
}

internal static class Z3OperandConversions
{
    public static byte ToObjectNumber(this ushort value)
    {
        if (value is 0 or > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A Version 3 object number must be in the range 1 through 255.");
        }

        return (byte)value;
    }

    public static byte ToPropertyNumber(this ushort value, bool allowZero = false)
    {
        if ((allowZero && value == 0) || value is >= 1 and <= Z3ObjectTable.MaximumPropertyNumber)
        {
            return (byte)value;
        }

        throw new ArgumentOutOfRangeException(nameof(value), "A Version 3 property number must be in the range 1 through 31.");
    }

    public static byte ToAttributeNumber(this ushort value)
    {
        if (value >= Z3ObjectTable.AttributeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A Version 3 attribute number must be in the range 0 through 31.");
        }

        return (byte)value;
    }
}
