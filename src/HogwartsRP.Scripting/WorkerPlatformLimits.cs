using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HogwartsRP.Scripting;

/// <summary>Hard process resource controls supported by the current OS.</summary>
public sealed class WindowsWorkerJob : IDisposable
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const int JobObjectCpuRateControlInformation = 15;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint CpuRateControlEnable = 0x1;
    private const uint CpuRateControlHardCap = 0x4;
    private IntPtr _handle;

    private WindowsWorkerJob(IntPtr handle) => _handle = handle;

    public static WindowsWorkerJob Create(long memoryLimitBytes, uint cpuRatePercent = 50)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to create Lua worker job object.");
        var job = new WindowsWorkerJob(handle);
        try
        {
            var limits = new ExtendedLimitInformation
            {
                BasicLimitInformation = new BasicLimitInformation
                {
                    LimitFlags = JobObjectLimitProcessMemory | JobObjectLimitKillOnJobClose
                },
                ProcessMemoryLimit = (UIntPtr)memoryLimitBytes
            };
            job.SetInformation(JobObjectExtendedLimitInformation, limits);
            job.SetInformation(JobObjectCpuRateControlInformation, new CpuRateControlInformation
            {
                ControlFlags = CpuRateControlEnable | CpuRateControlHardCap,
                CpuRate = Math.Clamp(cpuRatePercent, 1u, 100u) * 100u
            });
            return job;
        }
        catch { job.Dispose(); throw; }
    }

    public void Assign(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to apply hard Lua worker job limits.");
    }

    private void SetInformation<T>(int informationClass, T value) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, memory, false);
            if (!SetInformationJobObject(_handle, informationClass, memory, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to configure hard Lua worker job limits.");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
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
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CpuRateControlInformation { public uint ControlFlags; public uint CpuRate; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint informationLength);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}

public static class LinuxWorkerLimits
{
    private const int ResourceCpu = 0;
    private const int ResourceData = 2;
    private const int ProcessCpuClock = 2;
    private const int CpuSecondsPerRequest = 10;
    private const long DataLimitBytes = 768L * 1024 * 1024;

    /// <summary>Apply hard Linux CPU and data-segment limits before loading resource code.</summary>
    public static void ApplyAtWorkerStartup()
    {
        if (!OperatingSystem.IsLinux()) return;
        SetLimit(ResourceData, DataLimitBytes, DataLimitBytes);
        ResetCpuBudgetForRequest();
    }

    /// <summary>Move the hard CPU time threshold forward for each new bounded worker request.</summary>
    public static void ResetCpuBudgetForRequest()
    {
        if (!OperatingSystem.IsLinux()) return;
        if (clock_gettime(ProcessCpuClock, out var time) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read Lua worker CPU time.");
        if (getrlimit(ResourceCpu, out var current) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read Lua worker CPU limit.");
        var usedSeconds = (ulong)Math.Ceiling(time.Seconds + time.Nanoseconds / 1_000_000_000d);
        var hard = current.Maximum;
        var requested = usedSeconds + CpuSecondsPerRequest;
        var soft = hard == ulong.MaxValue ? requested : Math.Min(requested, hard);
        if (soft <= usedSeconds) throw new InvalidOperationException("Inherited Linux CPU limit is exhausted for the Lua worker.");
        if (setrlimit(ResourceCpu, new ResourceLimit { Current = soft, Maximum = hard }) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to apply Lua worker CPU limit.");
    }

    private static void SetLimit(int resource, long current, long maximum)
    {
        if (getrlimit(resource, out var inherited) != 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to read Linux Lua worker resource limit.");
        var max = inherited.Maximum == ulong.MaxValue ? (ulong)maximum : Math.Min((ulong)maximum, inherited.Maximum);
        var cur = Math.Min((ulong)current, max);
        if (setrlimit(resource, new ResourceLimit { Current = cur, Maximum = max }) != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to apply hard Linux Lua worker memory limit.");
    }

    [StructLayout(LayoutKind.Sequential)] private struct ResourceLimit { public ulong Current; public ulong Maximum; }
    [StructLayout(LayoutKind.Sequential)] private struct Timespec { public long Seconds; public long Nanoseconds; }
    [DllImport("libc", SetLastError = true)] private static extern int getrlimit(int resource, out ResourceLimit limit);
    [DllImport("libc", SetLastError = true)] private static extern int setrlimit(int resource, in ResourceLimit limit);
    [DllImport("libc", SetLastError = true)] private static extern int clock_gettime(int clockId, out Timespec time);
}
