using System;
using System.IO;

namespace AutoDraw.Core
{
    /// <summary>Appends diagnostics to %LOCALAPPDATA%\AutoDraw\log.txt so failures can be inspected afterwards.</summary>
    internal static class Log
    {
        public static string Path => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoDraw", "log.txt");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch { /* logging must never break the panel */ }
        }

        /// <summary>
        /// Times a step: <c>using (Log.Timed("Mass")) { ... }</c> writes "Mass: 1234 ms" when it ends. Steps under
        /// <paramref name="minMs"/> are left out so the log shows where the time goes.
        /// </summary>
        public static IDisposable Timed(string step, long minMs = 20) => new StepTimer(step, minMs);

        private sealed class StepTimer : IDisposable
        {
            private readonly string _step;
            private readonly long _minMs;
            private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

            public StepTimer(string step, long minMs) { _step = step; _minMs = minMs; }

            public void Dispose()
            {
                long ms = _watch.ElapsedMilliseconds;
                if (ms >= _minMs) Write(_step + ": " + ms + " ms");
            }
        }
    }
}
