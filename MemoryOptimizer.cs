using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading.Tasks;

namespace FlugiClipboard
{
    /// <summary>
    /// 内存优化辅助类
    /// </summary>
    public static class MemoryOptimizer
    {
        private static readonly object _lock = new();
        private static DateTime _lastCleanup = DateTime.MinValue;
        private static readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(3);

        /// <summary>
        /// 获取当前进程的内存使用情况（MB）
        /// </summary>
        public static long GetMemoryUsageMB()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.WorkingSet64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取当前进程的私有内存使用情况（MB）
        /// </summary>
        public static long GetPrivateMemoryMB()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.PrivateMemorySize64 / (1024 * 1024);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 执行内存清理
        /// </summary>
        public static void PerformCleanup(bool force = false)
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                if (!force && now - _lastCleanup < _cleanupInterval)
                {
                    return; // 避免过于频繁的清理
                }

                try
                {
                    // 设置垃圾回收为低延迟模式
                    var oldMode = GCSettings.LatencyMode;
                    GCSettings.LatencyMode = GCLatencyMode.LowLatency;

                    // 执行垃圾回收
                    GC.Collect(2, GCCollectionMode.Forced, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, true);

                    // 压缩大对象堆
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();

                    // 恢复垃圾回收模式
                    GCSettings.LatencyMode = oldMode;

                    _lastCleanup = now;
                }
                catch
                {
                    // 忽略内存清理失败
                }
            }
        }

        /// <summary>
        /// 异步执行内存清理
        /// </summary>
        public static Task PerformCleanupAsync(bool force = false)
        {
            return Task.Run(() => PerformCleanup(force));
        }

        /// <summary>
        /// 检查内存使用是否过高
        /// </summary>
        public static bool IsMemoryUsageHigh(long thresholdMB = 100)
        {
            return GetMemoryUsageMB() > thresholdMB;
        }

        /// <summary>
        /// 优化字符串内存使用
        /// </summary>
        public static string OptimizeString(string input, int maxLength = 1000)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (input.Length <= maxLength)
                return input;

            // 截断过长的字符串以节省内存
            return string.Concat(input.AsSpan(0, maxLength), "...");
        }

        /// <summary>
        /// 设置进程优先级为低优先级以减少系统资源占用
        /// </summary>
        public static void SetLowPriority()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch
            {
                // 忽略设置失败
            }
        }

        /// <summary>
        /// 监控内存使用并在必要时自动清理
        /// </summary>
        public static void MonitorAndCleanup(long thresholdMB = 80)
        {
            if (IsMemoryUsageHigh(thresholdMB))
            {
                PerformCleanupAsync(true);
            }
        }
    }
}
