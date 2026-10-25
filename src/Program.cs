using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CawPlus
{
    /// <summary>
    /// CAWPlus.exe sits in the CAW install folder. It loads CAW (WorldBuilderCS.exe / CAW.exe) into this
    /// process, starts it normally, and waits until CAW's main window exists to add the extra toolbar buttons.
    /// CAW's own files are never modified.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            Environment.CurrentDirectory = dir;
            Log.Info("CAWPlus starting in " + dir);

            string target = null;
            foreach (string name in new[] { "WorldBuilderCS.exe", "CAW.exe" })
            {
                string p = Path.Combine(dir, name);
                if (File.Exists(p)) { target = p; break; }
            }
            if (target == null)
            {
                MessageBox.Show("CAWPlus.exe must be placed in the Create a World Tool folder\n(next to CAW.exe / WorldBuilderCS.exe).",
                    "CAW Plus", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            AppDomain.CurrentDomain.AssemblyResolve += delegate (object s, ResolveEventArgs e)
            {
                string shortName = new AssemblyName(e.Name).Name;
                foreach (string ext in new[] { ".dll", ".DLL", ".exe" })
                {
                    string p = Path.Combine(dir, shortName + ext);
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };

            Assembly caw;
            try
            {
                caw = Assembly.LoadFrom(target);
                Caw.Init(caw);
            }
            catch (Exception ex)
            {
                Log.Error("Failed to load CAW", ex);
                MessageBox.Show("Could not load " + Path.GetFileName(target) + ":\n" + ex.Message, "CAW Plus",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // SharedDotNet's PersistenceTools.GetInstance (used by CSHostWrap/GameEngine) finds types by bare name in
            // Assembly.GetEntryAssembly() and its references. The entry assembly is now CAWPlus, so resolve those
            // names against CAW's exe and its references instead, as they would be when CAW is started directly.
            AppDomain.CurrentDomain.TypeResolve += delegate (object s, ResolveEventArgs e)
            {
                return TypeResolver.Find(caw, e.Name);
            };

            // Inject our UI once CAW's main form is up: a background thread waits for it and hands over to CAW's UI
            // thread with BeginInvoke (no Timer or other window may exist before CAW's Main runs; see Injector).
            Injector.Start();

            MethodInfo main = Caw.MainFormType.GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (main == null)
            {
                MessageBox.Show("Could not find CAW's entry point.", "CAW Plus", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            NativeCrashLog.Install();
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("Unhandled exception (terminating=" + e.IsTerminating + ")", e.ExceptionObject as Exception);
            };

            Log.Info("Starting CAW: " + target);
            int exitCode;
            try
            {
                object[] callArgs = main.GetParameters().Length == 1 ? new object[] { args } : new object[0];
                object rc = main.Invoke(null, callArgs);
                exitCode = rc is int ? (int)rc : 0;
            }
            catch (TargetInvocationException tie)
            {
                Log.Error("CAW exited with an exception", tie.InnerException ?? tie);
                throw;
            }

            // CAW's Main has already saved, shut its engine down (App.Shutdown) and disposed its window. Normal .NET
            // teardown after this runs finalizers on CAW's native wrapper objects, which can crash on exit. Nothing is
            // left to do, so end the process here.
            Log.Info("CAW closed normally (exit code " + exitCode + ")");
            TerminateProcess(GetCurrentProcess(), (uint)exitCode);
            return exitCode;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);
    }

    /// <summary>
    /// Logs native crashes (access violations inside CAW's C++ code), which .NET's own handlers never see: the
    /// exception code, the address and the DLL it happened in. Windows then shows its usual crash dialog.
    /// </summary>
    internal static class NativeCrashLog
    {
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate int TopLevelFilter(IntPtr exceptionPointers);

        private static TopLevelFilter keepAlive; // must not be garbage-collected
        private static IntPtr previous;           // the filter we replaced (the CLR's or CAW's): called after logging

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr SetUnhandledExceptionFilter(TopLevelFilter filter);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern uint GetModuleFileNameW(IntPtr module, System.Text.StringBuilder name, uint size);

        public static void Install()
        {
            keepAlive = Filter;
            previous = SetUnhandledExceptionFilter(keepAlive);
        }

        private static int Filter(IntPtr pointers)
        {
            try
            {
                IntPtr record = System.Runtime.InteropServices.Marshal.ReadIntPtr(pointers);
                uint code = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(record);
                IntPtr address = System.Runtime.InteropServices.Marshal.ReadIntPtr(record, 12); // EXCEPTION_RECORD.ExceptionAddress (x86)
                string module = "?";
                IntPtr h;
                if (GetModuleHandleExW(0x4 | 0x2, address, out h)) // FROM_ADDRESS | UNCHANGED_REFCOUNT
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(260);
                    GetModuleFileNameW(h, sb, 260);
                    module = System.IO.Path.GetFileName(sb.ToString()) + "+0x" + (address.ToInt64() - h.ToInt64()).ToString("X");
                }
                Log.Info(string.Format("CRASH (native): code 0x{0:X8} at 0x{1:X8} in {2}", code, address.ToInt64(), module));
            }
            catch { }
            if (previous != IntPtr.Zero)
                try
                {
                    TopLevelFilter prev = (TopLevelFilter)System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer(previous, typeof(TopLevelFilter));
                    return prev(pointers);
                }
                catch { }
            return 0; // EXCEPTION_CONTINUE_SEARCH: let Windows handle it as usual
        }
    }

    internal static class TypeResolver
    {
        [ThreadStatic] private static bool busy;
        private static Assembly[] searchList;

        public static Assembly Find(Assembly caw, string typeName)
        {
            if (busy || string.IsNullOrEmpty(typeName)) return null;
            busy = true;
            try
            {
                int comma = typeName.IndexOf(',');
                string name = comma > 0 ? typeName.Substring(0, comma) : typeName;
                foreach (Assembly a in SearchList(caw))
                {
                    if (a.GetType(name, false) != null) return a;
                }
                return null;
            }
            catch { return null; }
            finally { busy = false; }
        }

        private static Assembly[] SearchList(Assembly caw)
        {
            if (searchList != null) return searchList;
            System.Collections.Generic.List<Assembly> list = new System.Collections.Generic.List<Assembly>();
            list.Add(caw);
            foreach (AssemblyName n in caw.GetReferencedAssemblies())
            {
                try { list.Add(Assembly.Load(n)); } catch { }
            }
            searchList = list.ToArray();
            return searchList;
        }
    }

    internal static class Log
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Personal), "CAWPlus");

        private static string FilePath { get { return Path.Combine(Dir, "CAWPlus.log"); } }

        static Log()
        {
            // keep the log from growing without limit: over 5 MB it becomes CAWPlus.old.log (replacing the last one)
            try
            {
                FileInfo f = new FileInfo(FilePath);
                if (!f.Exists || f.Length < 5 * 1024 * 1024) return;
                string old = Path.Combine(Dir, "CAWPlus.old.log");
                if (File.Exists(old)) File.Delete(old);
                File.Move(FilePath, old);
            }
            catch { }
        }

        public static void Info(string msg) { Write("INFO  " + msg); }
        public static void Error(string msg, Exception ex) { Write("ERROR " + msg + (ex != null ? ": " + ex : "")); }

        private static void Write(string line)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + line + Environment.NewLine);
            }
            catch { }
        }
    }
}
