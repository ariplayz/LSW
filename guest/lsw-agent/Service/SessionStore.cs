using System.Collections.Concurrent;

namespace LswAgent.Service;

/// <summary>
/// Holds active ConPTY sessions keyed by session_id.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, ConPtySession> _sessions = new();

    public bool TryAdd(string id, ConPtySession session) => _sessions.TryAdd(id, session);
    public bool TryGet(string id, out ConPtySession? session) => _sessions.TryGetValue(id, out session);
    public bool TryRemove(string id, out ConPtySession? session) => _sessions.TryRemove(id, out session);
}

/// <summary>
/// Represents one live ConPTY-backed shell session.
/// On Windows 10 1809+ the ConPTY API is available via PInvoke.
/// </summary>
public sealed class ConPtySession : IDisposable
{
    public string SessionId { get; }
    public string Shell { get; }

    // ConPTY handles (IntPtr.Zero = not yet created / not supported)
    private IntPtr _hPseudoConsole = IntPtr.Zero;
    private IntPtr _hProcess       = IntPtr.Zero;
    private IntPtr _hThread        = IntPtr.Zero;
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _inputWrite;
    private Microsoft.Win32.SafeHandles.SafeFileHandle? _outputRead;

    private readonly CancellationTokenSource _cts = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _outQueue = new();
    private bool _eof;
    private int _exitCode = -1;

    public bool IsEof => _eof;
    public int ExitCode => _exitCode;

    public ConPtySession(string sessionId, string shell)
    {
        SessionId = sessionId;
        Shell     = shell;
    }

    /// <summary>
    /// Starts the ConPTY session using Win32 PInvoke.
    /// Falls back gracefully if ConPTY is unavailable (Windows 7/Server 2019-).
    /// Returns true on success.
    /// </summary>
    public bool Start(string commandLine, string? cwd, short cols, short rows)
    {
        return ConPtyNative.TryCreateSession(
            commandLine, cwd, cols, rows,
            out _hPseudoConsole, out _hProcess, out _hThread,
            out _inputWrite, out _outputRead);
    }

    public void Write(byte[] data)
    {
        if (_inputWrite == null || _inputWrite.IsInvalid) return;
        using var fs = new System.IO.FileStream(_inputWrite, System.IO.FileAccess.Write, leaveOpen: true);
        fs.Write(data);
        fs.Flush();
    }

    /// <summary>Drains available output bytes (non-blocking).</summary>
    public byte[] ReadAvailable()
    {
        if (_outputRead == null || _outputRead.IsInvalid) return Array.Empty<byte>();
        using var fs = new System.IO.FileStream(_outputRead, System.IO.FileAccess.Read, leaveOpen: true);
        var buf = new byte[4096];
        // Non-blocking peek via available bytes
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            return Array.Empty<byte>();
        var ms = new System.IO.MemoryStream();
        try
        {
            int n;
            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            {
                ms.Write(buf, 0, n);
                if (n < buf.Length) break;
            }
        }
        catch { _eof = true; }
        return ms.ToArray();
    }

    public void Resize(short cols, short rows)
    {
        if (_hPseudoConsole != IntPtr.Zero)
            ConPtyNative.ResizePseudoConsole(_hPseudoConsole, cols, rows);
    }

    public int Close()
    {
        _cts.Cancel();
        if (_hProcess != IntPtr.Zero)
        {
            uint code = 0;
            ConPtyNative.GetExitCodeProcess(_hProcess, out code);
            _exitCode = (int)code;
            ConPtyNative.CloseHandle(_hProcess);
            _hProcess = IntPtr.Zero;
        }
        if (_hThread != IntPtr.Zero) { ConPtyNative.CloseHandle(_hThread); _hThread = IntPtr.Zero; }
        if (_hPseudoConsole != IntPtr.Zero) { ConPtyNative.ClosePseudoConsole(_hPseudoConsole); _hPseudoConsole = IntPtr.Zero; }
        _inputWrite?.Dispose();
        _outputRead?.Dispose();
        _eof = true;
        return _exitCode;
    }

    public void Dispose() => Close();
}

/// <summary>
/// P/Invoke wrappers for the Windows ConPTY API (Windows 10 1809+ / Server 2019+).
/// </summary>
internal static class ConPtyNative
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ClosePseudoConsole(IntPtr hPC);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    internal static extern bool CreatePipe(
        out Microsoft.Win32.SafeHandles.SafeFileHandle hReadPipe,
        out Microsoft.Win32.SafeHandles.SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes, uint nSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(
        COORD size,
        Microsoft.Win32.SafeHandles.SafeFileHandle hInput,
        Microsoft.Win32.SafeHandles.SafeFileHandle hOutput,
        uint dwFlags,
        out IntPtr phPC);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct COORD { public short X; public short Y; }

    /// <summary>
    /// Creates a full ConPTY session. Returns false if ConPTY is not available on this OS.
    /// </summary>
    internal static bool TryCreateSession(
        string commandLine, string? cwd, short cols, short rows,
        out IntPtr hPC, out IntPtr hProcess, out IntPtr hThread,
        out Microsoft.Win32.SafeHandles.SafeFileHandle? inputWrite,
        out Microsoft.Win32.SafeHandles.SafeFileHandle? outputRead)
    {
        hPC = IntPtr.Zero; hProcess = IntPtr.Zero; hThread = IntPtr.Zero;
        inputWrite = null; outputRead = null;

        // ConPTY requires Windows 10 1809 (build 17763)
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return false;

        if (!CreatePipe(out var inputReadSide, out var inputWriteSide, IntPtr.Zero, 0))
            return false;
        if (!CreatePipe(out var outputReadSide, out var outputWriteSide, IntPtr.Zero, 0))
            return false;

        var sz = new COORD { X = cols, Y = rows };
        int hr = CreatePseudoConsole(sz, inputReadSide, outputWriteSide, 0, out hPC);
        inputReadSide.Dispose();
        outputWriteSide.Dispose();
        if (hr != 0) return false;

        inputWrite = inputWriteSide;
        outputRead = outputReadSide;

        // Build STARTUPINFOEX with the ConPTY attribute
        // (abbreviated: full STARTUPINFOEX PInvoke omitted for brevity;
        //  real implementation uses InitializeProcThreadAttributeList +
        //  UpdateProcThreadAttribute + CreateProcess)
        // For MVP we start the shell with CreateProcess using the ConPTY handle.
        // This skeleton sets hProcess/hThread to Zero — concrete PInvoke
        // implementation in ConPtyNative.StartProcess() should be added here.
        // The shell will be accessible via the pipes.
        return StartProcess(commandLine, cwd, hPC, out hProcess, out hThread);
    }

    private static bool StartProcess(string commandLine, string? cwd, IntPtr hPC,
        out IntPtr hProcess, out IntPtr hThread)
    {
        hProcess = IntPtr.Zero;
        hThread  = IntPtr.Zero;

        // Full STARTUPINFOEX path requires unsafe code and extensive PInvoke.
        // Abbreviated here; the ConPTY sample at
        // https://github.com/microsoft/terminal/tree/main/samples/ConPTY/MiniTerm
        // provides the complete pattern.
        //
        // For the MVP interactive SSH path, lswd relies on the built-in OpenSSH
        // sshd which already provides a PTY — ConPTY sessions here serve the
        // JSON-RPC conpty_* methods only.
        return false; // placeholder until full PInvoke is wired
    }
}
