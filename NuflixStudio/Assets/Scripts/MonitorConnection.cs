using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class MonitorConnection
{
    private Thread _thread;
    private TcpClient _client;
    private NetworkStream _stream;
    private ConcurrentQueue<(ViceMonitorCommand, Action<ViceMonitorResponse>, Action)> _commandQueue = new();
    private ManualResetEventSlim _newCommandEvent = new ManualResetEventSlim(false);

    private int _requestId;
    private bool _connecting;

    public void SendCommand(ViceMonitorCommand command, Action<ViceMonitorResponse> responseHandler = null, Action errorHandler = null)
    {
        EnsureConnection();
        _commandQueue.Enqueue((command, responseHandler, errorHandler));
        _newCommandEvent.Set();
    }

    private void EnsureConnection()
    {
        if (_connecting)
        {
            return;
        }
        if (_thread != null && _thread.IsAlive && _client != null && _client.Connected && _stream != null && _stream.CanWrite)
        {
            return;
        }
        if (_thread != null && _thread.IsAlive)
        {
            _thread.Abort();
        }
        _thread = new Thread(ConnectionThread);
        _thread.Start();
    }

    private void ConnectionThread()
    {
        _connecting = true;
        try
        {
            _client = new TcpClient("localhost", 6502);
            _stream = _client.GetStream();
            _requestId = 1;
        }
        catch
        {
            DiscardCommandQueue();
            return;
        }
        finally
        {
            _connecting = false;
        }

        while (true)
        {
            _newCommandEvent.Wait();

            while (_commandQueue.TryDequeue(out var cmd))
            {
                var (command, responseHandler, errorHandler) = cmd;

                try
                {
                    var bytes = command.Bytes;
                    var length = bytes.Count - 1;
                    var frame = new List<byte>
                    {
                        0x02, // Start byte
                        0x02, // Version
                        (byte)length,
                        (byte)(length >> 8),
                        (byte)(length >> 16),
                        (byte)(length >> 24),
                        (byte)_requestId,
                        (byte)(_requestId >> 8),
                        (byte)(_requestId >> 16),
                        (byte)(_requestId >> 24)
                    };
                    frame.AddRange(bytes);

                    _stream.Write(frame.ToArray());
                    _stream.Flush();

                    while (true)
                    {
                        var responseHeader = new byte[12];
                        _stream.Read(responseHeader);
                        var responseLength = responseHeader[2] | (responseHeader[3] << 8) | (responseHeader[4] << 16) | (responseHeader[5] << 24);
                        bytes = new List<byte>(responseHeader);
                        if (responseLength > 0)
                        {
                            var responseBody = new byte[responseLength];
                            _stream.Read(responseBody);
                            bytes.AddRange(responseBody);
                        }
                        var response = new ViceMonitorResponse(bytes);
                        if (response.RequestId == _requestId)
                        {
                            responseHandler?.Invoke(response);
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    errorHandler?.Invoke();
                    DiscardCommandQueue();
                    return;
                }

                _requestId++;
            }

            _newCommandEvent.Reset();
        }
    }

    private void DiscardCommandQueue()
    {
        while (_commandQueue.TryDequeue(out var cmd))
        {
            var (command, responseHandler, errorHandler) = cmd;
            errorHandler?.Invoke();
        }
    }
}

public class ViceMonitorCommand
{
    public readonly List<byte> Bytes = new();

    public ViceMonitorCommandType Type => (ViceMonitorCommandType)Bytes[0];

    private void AddBool(bool val)
    {
        Bytes.Add((byte)(val ? 1 : 0));
    }

    private void AddByte(int val)
    {
        Bytes.Add((byte)val);
    }

    private void AddWord(int val)
    {
        Bytes.Add((byte)val);
        Bytes.Add((byte)(val >> 8));
    }

    private void AddBytes(IList<byte> data)
    {
        Bytes.AddRange(data);
    }

    private void AddType(ViceMonitorCommandType type)
    {
        Bytes.Add((byte)type);
    }

    public static ViceMonitorCommand MemoryGet(int startAddress, int endAddress, bool causeSideEffects = false, MemSpace memSpace = MemSpace.MainMemory, int bankId = 1)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.MemoryGet);
        result.AddBool(causeSideEffects);
        result.AddWord(startAddress);
        result.AddWord(endAddress);
        result.AddByte((byte)memSpace);
        result.AddWord(bankId);
        return result;
    }

    public static ViceMonitorCommand MemorySet(int startAddress, IList<byte> data, bool causeSideEffects = false, MemSpace memSpace = MemSpace.MainMemory, int bankId = 1)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.MemorySet);
        result.AddBool(causeSideEffects);
        result.AddWord(startAddress);
        result.AddWord(startAddress + data.Count - 1);
        result.AddByte((byte)memSpace);
        result.AddWord(bankId);
        result.AddBytes(data);
        return result;
    }

    public static ViceMonitorCommand CheckpointSet(int startAddress, int endAddress, CheckPointCpuOperation cpuOperation, bool stopWhenHit = true, bool enabled = true, bool temporary = true, MemSpace memSpace = MemSpace.MainMemory)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.CheckpointSet);
        result.AddWord(startAddress);
        result.AddWord(endAddress);
        result.AddBool(stopWhenHit);
        result.AddBool(enabled);
        result.AddByte((byte)cpuOperation);
        result.AddBool(temporary);
        result.AddByte((byte)memSpace);
        return result;
    }

    public static ViceMonitorCommand RegistersGet(MemSpace memSpace)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.RegistersGet);
        result.AddByte((byte)memSpace);
        return result;
    }

    public static ViceMonitorCommand RegistersSet(MemSpace memSpace, Dictionary<RegisterId, int> values)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.RegistersSet);
        result.AddByte((byte)memSpace);
        result.AddWord(values.Count);
        foreach (var entry in values)
        {
            result.AddByte(3); // The size of the entry
            result.AddByte((byte)entry.Key);
            result.AddWord(entry.Value);
        }
        return result;
    }

    public static ViceMonitorCommand ResourceGet(string name)
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.ResourceGet);
        result.AddByte((byte)name.Length);
        foreach (char c in name)
        {
            result.AddByte(c);
        }
        return result;
    }

    public static ViceMonitorCommand Exit()
    {
        var result = new ViceMonitorCommand();
        result.AddType(ViceMonitorCommandType.Exit);
        return result;
    }
}

public class ViceMonitorResponse
{
    public readonly List<byte> Bytes;

    public ViceMonitorResponseType Type => (ViceMonitorResponseType)Bytes[6];

    public byte ErrorCode => Bytes[7];

    public int RequestId => Bytes[8] | (Bytes[9] << 8) | (Bytes[10] << 16) | (Bytes[11] << 24);

    public ViceMonitorResponse(IList<byte> bytes)
    {
        Bytes = new List<byte>(bytes);
    }

    public byte[] MemoryGet()
    {
        if (Type != ViceMonitorResponseType.MemoryGet)
        {
            return null;
        }
        var length = Bytes[12] | (Bytes[13] << 8);
        var result = new byte[length];
        Bytes.CopyTo(14, result, 0, length);
        return result;
    }

    public int RegisterGet(RegisterId id)
    {
        if (Type != ViceMonitorResponseType.Register)
        {
            return -1;
        }
        var count = Bytes[12] | (Bytes[13] << 8);
        var ofs = 14;
        for (var i = 0; i < count; i++)
        {
            var entryLen = Bytes[ofs];
            if ((byte)id != Bytes[ofs + 1])
            {
                ofs += entryLen;
                continue;
            }
            return entryLen > 2 ? Bytes[ofs + 2] | (Bytes[ofs + 3] << 8) : Bytes[ofs + 2];
        }
        return -1;
    }

    public int ResourceGetInt()
    {
        if (Type != ViceMonitorResponseType.ResourceGet)
        {
            return -1;
        }
        var isInt = Bytes[12] == 0x01;
        if (!isInt)
        {
            return -1;
        }
        var length = Bytes[13];
        var result = 0;
        for (var i = 0; i < length; i++)
        {
            result |= Bytes[14 + i] << (i << 3);
        }
        return result;
    }

    public string ResourceGetString()
    {
        if (Type != ViceMonitorResponseType.ResourceGet)
        {
            return null;
        }
        var isString = Bytes[12] == 0x00;
        if (!isString)
        {
            return null;
        }
        var length = Bytes[13];
        var result = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            result.Append((char)Bytes[14 + i]);
        }
        return result.ToString();
    }
}

[Flags]
public enum CheckPointCpuOperation : byte
{
    None = 0, Load = 1, Store = 2, Exec = 4
};

public enum MemSpace : byte
{
    MainMemory = 0x00,
    Drive8 = 0x01,
    Drive9 = 0x02,
    Drive10 = 0x03,
    Drive11 = 0x04
};

public enum RegisterId : byte
{
    A = 0x00,
    X = 0x01,
    Y = 0x02,
    PC = 0x03,
    SP = 0x04,
    Status = 0x05,
    Cpu00 = 0x37,
    Cpu01 = 0x38
}

public enum ViceMonitorCommandType : byte
{
    MemoryGet = 0x01,
    MemorySet = 0x02,
    CheckpointGet = 0x11,
    CheckpointSet = 0x12,
    CheckpointDelete = 0x13,
    CheckpointList = 0x14,
    CheckpointToggle = 0x15,
    ConditionSet = 0x22,
    RegistersGet = 0x31,
    RegistersSet = 0x32,
    Dump = 0x41,
    Undump = 0x42,
    ResourceGet = 0x51,
    ResourceSet = 0x52,
    AdvanceInstructions = 0x71,
    KeyboardFeed = 0x72,
    ExecuteUntilReturn = 0x73,
    Ping = 0x81,
    BanksAvailable = 0x82,
    RegistersAvailable = 0x83,
    DisplayGet = 0x84,
    ViceInfo = 0x85,
    PaletteGet = 0x91,
    JoyportSet = 0xa2,
    UserportSet = 0xb2,
    Exit = 0xaa,
    Quit = 0xbb,
    Reset = 0xcc,
    Autostart = 0xdd
}

public enum ViceMonitorResponseType : byte
{
    Invalid = 0x00,
    MemoryGet = 0x01,
    MemorySet = 0x02,
    Checkpoint = 0x11,
    CheckpointDelete = 0x13,
    CheckpointList = 0x14,
    CheckpointToggle = 0x15,
    ConditionSet = 0x22,
    Register = 0x31,
    Dump = 0x41,
    Undump = 0x42,
    ResourceGet = 0x51,
    ResourceSet = 0x52,
    Jam = 0x61,
    Stopped = 0x62,
    Resumed = 0x63,
    AdvanceInstructions = 0x71,
    KeyboardFeed = 0x72,
    ExecuteUntilReturn = 0x73,
    Ping = 0x81,
    BanksAvailable = 0x82,
    RegistersAvailable = 0x83,
    DisplayGet = 0x84,
    ViceInfo = 0x85,
    PaletteGet = 0x91,
    JoyportSet = 0xa2,
    UserportSet = 0xb2,
    Exit = 0xaa,
    Quit = 0xbb,
    Reset = 0xcc,
    Autostart = 0xdd
}