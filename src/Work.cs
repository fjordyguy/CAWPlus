using System;
using System.Threading;

namespace CawPlus
{
    /// <summary>Reports a long job's stage and percent done (also used inside CAWPlusHelper).</summary>
    public delegate void ProgressFn(string stage, int percent);

    /// <summary>
    /// Runs independent pieces of work on several CPU cores (.NET 2 has no Parallel.For). Used for the terrain and road
    /// check candidates, which are pure computation (no CAW calls, no windows).
    /// </summary>
    internal static class Work
    {
        public delegate void Body(int i);

        /// <summary>Calls body(0..n-1), spread over up to (cores - 1) threads; returns when all are done. The first
        /// exception is rethrown. stop (optional) is checked before each item.</summary>
        public static void For(int n, Body body, Stop stop)
        {
            int threads = Math.Max(1, Math.Min(n, Environment.ProcessorCount - 1));
            if (threads <= 1)
            {
                for (int i = 0; i < n; i++) { if (stop != null && stop.Requested) return; body(i); }
                return;
            }
            int next = -1;
            Exception error = null;
            Thread[] ts = new Thread[threads];
            for (int t = 0; t < threads; t++)
            {
                ts[t] = new Thread(delegate ()
                {
                    try
                    {
                        while (true)
                        {
                            int i = Interlocked.Increment(ref next);
                            if (i >= n || (stop != null && stop.Requested) || error != null) break;
                            body(i);
                        }
                    }
                    catch (Exception ex) { if (error == null) error = ex; }
                });
                ts[t].IsBackground = true;
                ts[t].Start();
            }
            foreach (Thread th in ts) th.Join();
            if (error != null) throw new InvalidOperationException("Background work failed: " + error.Message, error);
        }

        /// <summary>Set Requested to abandon work that is no longer wanted (e.g. a preview superseded by a newer one).</summary>
        public sealed class Stop { public volatile bool Requested; }
    }
}
