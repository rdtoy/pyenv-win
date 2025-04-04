using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace Scoop {

    public class Program {
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
        static extern bool CreateProcess(string lpApplicationName,
            string lpCommandLine, IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes, bool bInheritHandles,
            uint dwCreationFlags, IntPtr lpEnvironment, string lpCurrentDirectory,
            [In] ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);
        const int ERROR_ELEVATION_REQUIRED = 740;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetConsoleWindow();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct STARTUPINFO {
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
        internal struct PROCESS_INFORMATION {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError=true)]
        static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);
        const UInt32 INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32.dll", SetLastError=true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

        static int Main(string[] args) {
            var exe = Assembly.GetExecutingAssembly().Location;
            var name = Path.GetFileNameWithoutExtension(exe);

            var si = new STARTUPINFO();
            var pi = new PROCESS_INFORMATION();

            // create command line
            var path = "cmd";
            var cmd_args = "/c pyenv exec " +  name;
            var pass_args = GetArgs(Environment.CommandLine);
            if(!string.IsNullOrEmpty(pass_args)) {
                if(!string.IsNullOrEmpty(cmd_args)) cmd_args += " ";
                cmd_args += pass_args;
            }
            if(!string.IsNullOrEmpty(cmd_args)) cmd_args = " " + cmd_args;
            var cmd = path + cmd_args;
            // Console.WriteLine("cmd: " + cmd + ", pass_args: " + pass_args);

            // Fix when GUI applications want to write to a console
            if (GetConsoleWindow() == IntPtr.Zero) {
                AttachConsole(-1);
            }

            if(!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: 0,
                lpEnvironment: IntPtr.Zero, // inherit parent
                lpCurrentDirectory: null, // inherit parent
                lpStartupInfo: ref si,
                lpProcessInformation: out pi)) {

                var error = Marshal.GetLastWin32Error();
                if(error == ERROR_ELEVATION_REQUIRED) {
                    // Unfortunately, ShellExecute() does not allow us to run program without
                    // CREATE_NEW_CONSOLE, so we can not replace CreateProcess() completely.
                    // The good news is we are okay with CREATE_NEW_CONSOLE when we run program with elevation.
                    Process process = new Process();
                    process.StartInfo = new ProcessStartInfo(path, cmd_args);
                    process.StartInfo.UseShellExecute = true;
                    try {
                        process.Start();
                    }
                    catch(Win32Exception exception) {
                        return exception.ErrorCode;
                    }
                    process.WaitForExit();
                    return process.ExitCode;
                }
                return error;
            }

            WaitForSingleObject(pi.hProcess, INFINITE);

            uint exit_code = 0;
            GetExitCodeProcess(pi.hProcess, out exit_code);

            // Close process and thread handles.
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);

            return (int)exit_code;
        }

        // now uses GetArgs instead
        static string Serialize(string[] args) {
            return string.Join(" ", args.Select(a => a.Contains(' ') ? '"' + a + '"' : a));
        }

        // strips the program name from the command line, returns just the arguments
        static string GetArgs(string cmdLine) {
            if(cmdLine.StartsWith("\"")) {
                var endQuote = cmdLine.IndexOf("\" ", 1);
                if(endQuote < 0) return "";
                return cmdLine.Substring(endQuote + 1);
            }
            var space = cmdLine.IndexOf(' ');
            if(space < 0 || space == cmdLine.Length - 1) return "";
            return cmdLine.Substring(space + 1);
        }
    }
}