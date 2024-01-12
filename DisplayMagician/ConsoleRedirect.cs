﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace DisplayMagician
{

    class ConsoleRedirect
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(StandardHandle nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetStdHandle(StandardHandle nStdHandle, IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern FileType GetFileType(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        private enum StandardHandle : uint
        {
            Input = unchecked((uint)-10),
            Output = unchecked((uint)-11),
            Error = unchecked((uint)-12)
        }

        private enum FileType : uint
        {
            Unknown = 0x0000,
            Disk = 0x0001,
            Char = 0x0002,
            Pipe = 0x0003
        }

        private static bool IsRedirected(IntPtr handle)
        {
            FileType fileType = GetFileType(handle);

            return (fileType == FileType.Disk) || (fileType == FileType.Pipe);
        }

        public static void Redirect()
        {
            if (IsRedirected(GetStdHandle(StandardHandle.Output)))
            {
                var initialiseOut = Console.Out;
            }

            bool errorRedirected = IsRedirected(GetStdHandle(StandardHandle.Error));
            if (errorRedirected)
            {
                var initialiseError = Console.Error;
            }

            bool freeconsole = false;
            AttachConsole(-1);
            /*if (!AttachConsole(-1))
            {
                AllocConsole();
                freeconsole = true;
            }*/

            if (!errorRedirected)
                SetStdHandle(StandardHandle.Error, GetStdHandle(StandardHandle.Output));
        }
    }
}
