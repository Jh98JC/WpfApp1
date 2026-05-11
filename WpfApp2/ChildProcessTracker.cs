using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WpfApp2
{
    // 부모 프로세스(WpfApp2)가 종료될 때 자식 프로세스(대진포스 쿼리.exe 등)도 자동 종료시키기 위한 Job Object 래퍼.
    // Windows 8+ 에서는 nested job 도 지원되므로 부모가 다른 job 안에서 실행 중이어도 동작.
    public static class ChildProcessTracker
    {
        private static readonly IntPtr _job;
        private static readonly bool _supported;

        static ChildProcessTracker()
        {
            try
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero) { _supported = false; return; }

                var basic = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = (uint)JOBOBJECTLIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                };
                var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
                {
                    BasicLimitInformation = basic
                };

                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(extended, ptr, false);
                    _supported = SetInformationJobObject(_job, JobObjectInfoType.ExtendedLimitInformation, ptr, (uint)length);
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { _supported = false; }
        }

        public static void AddProcess(Process process)
        {
            if (!_supported || process == null) return;
            try { AssignProcessToJobObject(_job, process.Handle); }
            catch (Exception ex) { Debug.WriteLine($"[ChildProcessTracker] {ex.Message}"); }
        }

        // ── P/Invoke ──
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr hJob,
            JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [Flags]
        private enum JOBOBJECTLIMIT : uint
        {
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000
        }

        private enum JobObjectInfoType
        {
            ExtendedLimitInformation = 9
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
