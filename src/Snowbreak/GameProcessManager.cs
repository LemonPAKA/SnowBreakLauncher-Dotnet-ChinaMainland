﻿using System;
using System.IO;
using System.Threading.Tasks;
#if !NO_WMI
using System.Management;
#endif
using Leayal.Shared.Windows;
using System.Diagnostics;
using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using Leayal.SnowBreakLauncher.Classes;

namespace Leayal.SnowBreakLauncher.Snowbreak;

sealed class GameProcessManager : IDisposable
{
    private readonly GameManager manager;

#if NO_WMI
    private readonly PollingTick pollingProcessWatcher;
    private Process? _gameProcess;
    private readonly CancellationTokenSource waitFinalCancel;
#else
    private readonly ManagementEventWatcher processWatcher;
    private static readonly ManagementScope cachedWMI_Scope = new ManagementScope(@"\\.\root\CIMV2");
    private uint _processId;
#endif

    internal GameProcessManager(GameManager manager)
    {
        this.manager = manager;

        this.waitFinalCancel = new CancellationTokenSource();

#if NO_WMI
        this._gameProcess = this.FindProcessFromExistingProcesses();
        this.pollingProcessWatcher = new PollingTick(this.OnPollingTick);
        if (this._gameProcess == null)
        {
            this.pollingProcessWatcher.Start();
        }
        else
        {
            try
            {
                if (this._gameProcess.HasExited)
                {
                    this._gameProcess = null;
                    this.pollingProcessWatcher.Start();
                }
                else
                {
                    this._gameProcess.RegisterProcessExitCallback(this.OnPollingGameProcessExited, this.waitFinalCancel.Token);
                }
            }
            catch
            {
                this.pollingProcessWatcher.Start();
            }
        }
#else
            this._processId = this.FindProcessIdFromExistingProcesses();
            var spanOfProcessName = Path.GetFileName(manager.GameExecutablePath.AsSpan());
            ReadOnlySpan<char> WMI_STR_QueryProcessOps = "SELECT TargetInstance FROM __InstanceOperationEvent WITHIN 0.5 WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = '";
            // Create a watcher and listen for events
            this.processWatcher = new ManagementEventWatcher(
                cachedWMI_Scope,
                new EventQuery(string.Concat(WMI_STR_QueryProcessOps, spanOfProcessName, WMI_STR_QueryProcessOps.Slice(WMI_STR_QueryProcessOps.Length - 1, 1)))
                );
            this.processWatcher.EventArrived += this.WMIEventArrived;
            this.processWatcher.Start();
#endif
    }

    public delegate void ProcessOperationHandler(in uint processId);
    public event ProcessOperationHandler? ProcessStarted, ProcessExited;

    private static readonly Func<Process, string, bool> IsTargetedProcess = OperatingSystem.IsWindows() ? IsTargetedProcess_Win : IsTargetedProcess_Linux;

    [SupportedOSPlatform("windows"), MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTargetedProcess_Win(Process proc, string targetProcessPath)
    {
        var procPath = proc.QueryFullProcessImageName();
        return string.Equals(procPath, targetProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    [UnsupportedOSPlatform("windows"), MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsTargetedProcess_Linux(Process proc, string targetProcessPath)
    {
        // Currently, it's FUBAR and I haven't found a way for it to work yet.
        // Tried readlink with both Shell execute "readlink" program (yes, it's a program with the same name) and libc's "readlink" function export,
        // both fail and return null regardless.
        return false;

        /*
        var exe = ProcessInfoHelper.QueryFullProcessImageName(proc);
        if (string.IsNullOrEmpty(exe)) return false;
        if (!MemoryExtensions.Equals(exe, "wine", StringComparison.Ordinal) && MemoryExtensions.EndsWith(exe, "/wine", StringComparison.Ordinal)) return false;

        var span = ProcessInfoHelper.Unix.GetProcessInfo(proc, "cmdline");
        return MemoryExtensions.Contains(span, targetProcessPath, StringComparison.OrdinalIgnoreCase);
        */
    }

    private Process? FindProcessFromExistingProcesses()
    {
        Process? result = null;
        var targetProcessPath = this.manager.GameExecutablePath;

        foreach (var proc in Process.GetProcessesByName(OperatingSystem.IsWindows() ? Path.GetFileNameWithoutExtension(targetProcessPath) : "wine"))
        {
            if (IsTargetedProcess.Invoke(proc, targetProcessPath))
            {
                result = proc;
            }
            else
            {
                proc.Dispose();
            }
        }
        return result;
    }

    private uint FindProcessIdFromExistingProcesses()
    {
        var proc = FindProcessFromExistingProcesses();
        if (proc == null) return 0;
        return unchecked((uint)proc.Id);
    }

#if NO_WMI
    public bool IsGameRunning => (this._gameProcess != null);
    public uint GameProcessId => (this._gameProcess == null ? 0 : unchecked((uint)this._gameProcess.Id));

    private void OnPollingGameProcessExited(Process proc, in uint processId)
    {
        this.OnGameProcessExit(proc, processId);
        this.pollingProcessWatcher?.Start();
    }

    private void OnPollingTick()
    {
        if (this.FindProcessFromExistingProcesses() is Process proc)
        {
            this.OnGameProcessStart(proc);
            this.pollingProcessWatcher?.Stop();
        }
    }

    sealed class PollingTick : IDisposable
    {
        private readonly Action callback;
        private readonly PeriodicTimer timer;
        private CancellationTokenSource? cancelSrc;

        public PollingTick(Action tickingCallback)
        {
            ArgumentNullException.ThrowIfNull(tickingCallback);
            this.callback = tickingCallback;
            this.timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        }

        public void Start()
        {
            var newCancelSrc = new CancellationTokenSource();
            var oldCancelSrc = Interlocked.CompareExchange(ref this.cancelSrc, newCancelSrc, null);
            if (oldCancelSrc != null)
            {
                newCancelSrc.Dispose();
                return;
            }

            Task.Factory.StartNew(this.Ticking, newCancelSrc, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            var oldCancelSrc = Interlocked.Exchange(ref this.cancelSrc, null);
            if (oldCancelSrc != null)
            {
                oldCancelSrc.Token.Register(oldCancelSrc.Dispose);
                oldCancelSrc.Cancel();
            }
        }

        private async Task Ticking(object? obj)
        {
            if (obj is CancellationTokenSource cancelSrc)
            {
                try
                {
                    while (await this.timer.WaitForNextTickAsync(cancelSrc.Token))
                    {
                        if (cancelSrc.IsCancellationRequested) break; // We're getting OperationCanceledException anyway, but just to besure if .NET changes that behavior

                        this.callback?.Invoke();
                    }
                }
                catch (OperationCanceledException)
                {

                }
            }
        }

        public void Dispose() => this.timer.Dispose();
    }

    private void OnGameProcessStart(Process process)
    {
        if (process.HasExited)
        {
            Interlocked.Exchange(ref this._gameProcess, null)?.Dispose(); // Dispose the old one if it exists.
            process.Dispose(); // Dispose this 'miss', too.
        }
        else
        {
            Interlocked.Exchange(ref this._gameProcess, process)?.Dispose(); // The old one should be null anyway, but just dispose in case it's not.
            uint processId = unchecked((uint)process.Id);
            process.RegisterProcessExitCallback(this.OnPollingGameProcessExited, this.waitFinalCancel?.Token ?? CancellationToken.None);
            this.ProcessStarted?.Invoke(in processId);
        }
    }

    private void OnGameProcessExit(Process? process, in uint processId)
    {
        var proc = Interlocked.Exchange(ref this._gameProcess, null) ?? process;
        process?.Dispose();
        this.ProcessExited?.Invoke(in processId);
    }
#else
    public bool IsGameRunning => (this._processId != 0);
    public uint GameProcessId => this._processId;

    [SupportedOSPlatform("windows")]
    private void WMIEventArrived(object sender, EventArrivedEventArgs e)
    {
        if (e.NewEvent.Properties["TargetInstance"].Value is ManagementBaseObject targetInstance)
        {
            var eventType = e.NewEvent.ClassPath.ClassName;
       
            if (string.Equals(eventType, "__InstanceCreationEvent", StringComparison.OrdinalIgnoreCase))
            {
                var procId = TryGetProcessId(targetInstance);
                var processPath = TryGetProcessPath(targetInstance, in procId);
                if (string.Equals(processPath, this.manager.GameExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    this.OnGameProcessStart(in procId);
                }
            }
            else if (string.Equals(eventType, "__InstanceDeletionEvent", StringComparison.OrdinalIgnoreCase))
            {
                var procId = TryGetProcessId(targetInstance);
                var processPath = TryGetProcessPath(targetInstance, in procId);
                if ((processPath != null && string.Equals(processPath, this.manager.GameExecutablePath, StringComparison.OrdinalIgnoreCase))
                    || procId == this._processId /* Usually we can't fetch Process Information here because the process has already cleaned up by OS. Comparing process ID should be the safe fall-back */)
                {
                    this.OnGameProcessExit(in procId);
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetProcessPath(ManagementBaseObject targetInstance, in uint processId)
    {
        // We can't just use targetInstance.Properties["ExecutablePath"] because it needs same-privilege to query the information.
        // Weird that WMI doesn't use PROCESS_QUERY_LIMITED_INFORMATION access right.
        if (UacHelper.IsCurrentProcessElevated)
        {
            return targetInstance.Properties["ExecutablePath"].Value.ToString();
        }
        else
        {
            try
            {
                using (var handle = ProcessInfoHelper.OpenProcessForQueryLimitedInfo(processId))
                {
                    return ProcessInfoHelper.QueryFullProcessImageName(handle);
                }
            }
            catch
            {
                return null;
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static uint TryGetProcessId(ManagementBaseObject targetInstance)
    {
        var prop = targetInstance.Properties["ProcessId"];
        static uint TryFromString(string? str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0;
            return uint.TryParse(str, out var id) ? id : 0;
        }
        return prop.Type switch
        {
            CimType.SInt16 => (uint)(Unsafe.Unbox<short>(prop.Value)),
            CimType.SInt32 => unchecked((uint)(Unsafe.Unbox<int>(prop.Value))),
            CimType.SInt64 => Convert.ToUInt32(Unsafe.Unbox<long>(prop.Value)),
            CimType.UInt16 => (uint)(Unsafe.Unbox<ushort>(prop.Value)),
            CimType.UInt32 => Unsafe.Unbox<uint>(prop.Value),
            CimType.UInt64 => Convert.ToUInt32(Unsafe.Unbox<ulong>(prop.Value)),
            _ => TryFromString(prop.Value.ToString())
        };
    }

    [SupportedOSPlatform("windows")]
    private void OnGameProcessStart(in uint processId)
    {
        Interlocked.Exchange(ref this._processId, processId);
        this.ProcessStarted?.Invoke(in processId);
    }

    [SupportedOSPlatform("windows")]
    private void OnGameProcessExit(in uint processId)
    {
        Interlocked.Exchange(ref this._processId, 0);
        this.ProcessExited?.Invoke(in processId);
    }
#endif

    /// <summary>Attempts to start the game.</summary>
    public Task StartGame() => Task.Run(this.Internal_StartGame);

    /// <summary>Tries get Wine version, if it is installed on the machine.</summary>
    /// <param name="wineVersionString">The version string which was returned by "wine --version" command line.</param>
    /// <returns><see langword="true"/> if Wine is installed and the command has been invoked successfully from this launcher. Otherwise, <see langword="false"/>.</returns>
    [UnsupportedOSPlatform("windows")]
    public bool TryGetWineVersion(string? winePath, [NotNullWhen(true)] out string? wineVersionString)
    {
        using (var proc = new Process())
        {
            proc.StartInfo.FileName = string.IsNullOrWhiteSpace(winePath) ? "wine" : winePath;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.ArgumentList.Add("--version");
            proc.StartInfo.RedirectStandardOutput = true;
            try
            {
                proc.Start();
                proc.WaitForExit();
                wineVersionString = proc.StandardOutput.ReadLine() ?? string.Empty;
                return wineVersionString.StartsWith("wine", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                wineVersionString = null;
                return false;
            }
        }
    }

    private void Internal_StartGame()
    {
        var mgr = this.manager;
        var executablePath = mgr.GameExecutablePath;

        if (!File.Exists(executablePath)) throw new FileNotFoundException(null, executablePath);


#if NO_WMI
        this.pollingProcessWatcher?.Stop();
#endif

        Process? proc = null;
        try
        {
            proc = new Process();
            var argList = proc.StartInfo.ArgumentList;
            var serverSelected = SnowBreakHttpClient.Instance.ClientServer;
            if (OperatingSystem.IsWindows())
            {
                proc.StartInfo.FileName = executablePath;
                // Launch the game as Admin, otherwise its anti-cheat will not work and the game will be either flagged as malicious client or not starting at all.
                proc.StartInfo.UseShellExecute = true;
                // "runas" verb on Windows tells OS's UAC to ask for Admin when starting the process. Or simply grant it Admin access if UAC is turned off.
                proc.StartInfo.Verb = "runas";

                proc.StartInfo.WorkingDirectory = Path.GetDirectoryName(proc.StartInfo.FileName) ?? string.Empty;
            }
            else if (OperatingSystem.IsLinux())
            {
                if (App.Current is App app)
                {
                    var winePath = app.LeaLauncherConfig.WinePath;
                    if (string.IsNullOrWhiteSpace(winePath) || !TryGetWineVersion(winePath, out _)) winePath = "wine"; // Fall-back to system-installed Wine if specified path doesn't exist or not a wine.
                    if (TryGetWineVersion(winePath, out _))
                    {
                        proc.StartInfo.FileName = winePath;
                        // I heard that Wine run Windows processes as Admin by default?
                        // Unverified, need help.
                        proc.StartInfo.UseShellExecute = true;

                        argList.Add("start");
                        argList.Add("/wait");
                        if (app.LeaLauncherConfig.WineUseUnixFileSystem) argList.Add("/unix");
                        argList.Add(executablePath);
                    }
                    else
                    {
                        // I don't know how to support Proton of Steam, nor any alternatives yet.
                        // So let's throw this Wine-aware-only error for now.
                        throw new WineNotFoundException();
                    }
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            else throw new NotSupportedException();

            argList.Add("-FeatureLevelES31");
            if (serverSelected.Equals("Global"))
            {
                argList.Add("-ChannelID=seasun");
            }
            else if(serverSelected.Equals("Bilibili"))
            {
                argList.Add("-ChannelID=bilibili");
            }
            else
            {
                argList.Add("-ChannelID=jinshan");
             }
            ReadOnlySpan<char> buffer = "-userdir=\"";
            string saved = "\\" + serverSelected;
            argList.Add(string.Concat(buffer, mgr.FullPathOfGameDirectory, saved, buffer.Slice(buffer.Length - 1)));

            // Argument "-gclid" is optional, it seems.
            bool alreadyHasGclid = false;
            var supposedPath_gclidFile = Path.Join(mgr.FullPathOfOfficialLauncherDirectory, "gclid");
            if (!File.Exists(supposedPath_gclidFile))
            {
                // If the file doesn't exist in the "supposed launcher folder".
                // Probe for the gclid file in the same folder of this launcher.
                supposedPath_gclidFile = Path.GetFullPath("gclid", AppContext.BaseDirectory);
            }
            if (File.Exists(supposedPath_gclidFile))
            {
                try
                {
                    using (var sr = File.OpenText(supposedPath_gclidFile))
                    {
                        var firstLine = sr.ReadLine();
                        if (!string.IsNullOrWhiteSpace(firstLine))
                        {
                            var pairValue = Path.GetFileNameWithoutExtension(firstLine.AsSpan().TrimEnd());
                            if (!pairValue.IsEmpty && !pairValue.IsWhiteSpace())
                            {
                                alreadyHasGclid = true;
                                if (pairValue.Contains(' '))
                                {
                                    ReadOnlySpan<char> buffer_gclid = "-gclid=\"";
                                    argList.Add(string.Concat(buffer_gclid, Path.GetFileNameWithoutExtension(firstLine.AsSpan()), buffer_gclid.Slice(buffer_gclid.Length - 1)));
                                }
                                else
                                {
                                    argList.Add(string.Concat("-gclid=", pairValue));
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // We will simply using assumed gclid value.
            // The official launcher's behavior is that if the "gclid" file doesn't exist, it will not populate this parameter at all.
            if (!alreadyHasGclid) argList.Add("-gclid=Snow_Setup");

            proc.Start();
        }
        catch
        {
            Interlocked.Exchange(ref proc, null)?.Dispose();
            throw;
        }
        finally
        {
#if NO_WMI
            if (proc == null)
            {
                this.pollingProcessWatcher?.Start();
            }
            else
            {
                this.OnGameProcessStart(proc);
            }
#else
                proc?.Dispose();
#endif
        }
    }

    public void Dispose()
    {
#if NO_WMI
        // this.pollingProcessWatcher.Stop();
        this.pollingProcessWatcher?.Dispose();
        if (this.waitFinalCancel != null)
        {
            this.waitFinalCancel.Cancel();
            this.waitFinalCancel.Dispose();
        }
#else
            this.processWatcher.Stop();
            this.processWatcher.EventArrived -= this.WMIEventArrived;
            this.processWatcher.Dispose();
#endif
    }
}
