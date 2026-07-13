param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$WorkingDirectory = "",

    [string]$Arguments = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
    $WorkingDirectory = Split-Path -Parent $ExePath
}

$code = @"
using System;
using System.Runtime.InteropServices;

public static class SystemSessionLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public Int32 cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public Int32 dwX;
        public Int32 dwY;
        public Int32 dwXSize;
        public Int32 dwYSize;
        public Int32 dwXCountChars;
        public Int32 dwYCountChars;
        public Int32 dwFillAttribute;
        public Int32 dwFlags;
        public Int16 wShowWindow;
        public Int16 cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public Int32 dwProcessId;
        public Int32 dwThreadId;
    }

    public enum TOKEN_INFORMATION_CLASS
    {
        TokenSessionId = 12
    }

    [DllImport("kernel32.dll")] public static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool DuplicateTokenEx(IntPtr ExistingTokenHandle, UInt32 dwDesiredAccess, IntPtr lpTokenAttributes, Int32 ImpersonationLevel, Int32 TokenType, out IntPtr DuplicateTokenHandle);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool SetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, ref UInt32 TokenInformation, UInt32 TokenInformationLength);
    [DllImport("userenv.dll", SetLastError = true)] public static extern bool CreateEnvironmentBlock(out IntPtr env, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] public static extern bool DestroyEnvironmentBlock(IntPtr env);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool CreateProcessAsUser(IntPtr token, string app, string cmd, IntPtr procAttr, IntPtr threadAttr, bool inherit, UInt32 flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CloseHandle(IntPtr handle);

    public static int Start(string app, string args, string cwd)
    {
        const UInt32 TOKEN_ALL_ACCESS = 0xF01FF;
        const UInt32 CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        const Int32 SecurityImpersonation = 2;
        const Int32 TokenPrimary = 1;

        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF) throw new InvalidOperationException("No active console session");

        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL_ACCESS, out token)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken");
        }

        IntPtr primary;
        if (!DuplicateTokenEx(token, TOKEN_ALL_ACCESS, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primary)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx");
        }

        if (!SetTokenInformation(primary, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, 4)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetTokenInformation(TokenSessionId)");
        }

        IntPtr env;
        if (!CreateEnvironmentBlock(out env, primary, false)) env = IntPtr.Zero;

        STARTUPINFO si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(si);
        si.lpDesktop = "winsta0\\default";
        si.wShowWindow = 1;

        PROCESS_INFORMATION pi;
        string cmd = "\"" + app + "\"" + (String.IsNullOrWhiteSpace(args) ? "" : " " + args);
        if (!CreateProcessAsUser(primary, app, cmd, IntPtr.Zero, IntPtr.Zero, false, CREATE_UNICODE_ENVIRONMENT, env, cwd, ref si, out pi)) {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser");
        }

        if (env != IntPtr.Zero) DestroyEnvironmentBlock(env);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        CloseHandle(primary);
        CloseHandle(token);
        return pi.dwProcessId;
    }
}
"@

Add-Type $code
$processId = [SystemSessionLauncher]::Start($ExePath, $Arguments, $WorkingDirectory)
Write-Host "Started SYSTEM process in active console session: pid=$processId exe=$ExePath"
