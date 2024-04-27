/*
 * This file contains mainly logic and functional code
*/

using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Leayal.SnowBreakLauncher.Classes;
using Leayal.SnowBreakLauncher.Snowbreak;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Leayal.SnowBreakLauncher.Windows
{
    public partial class MainWindow
    {
        private readonly CancellationTokenSource cancelSrc_Root;
        private readonly OfficialJsonConfiguration _launcherConfig;

        private void OnGameManagerChanged(GameManager? oldOne, GameManager newOne)
        {
            // Since we're changing directory, closing the old File handles in the old directory. This is only meaningful on non-Windows.
            if (!OperatingSystem.IsWindows()) GameClientManifestData.CloseAllHandles();

            if (oldOne != null)
            {
                oldOne.Process.ProcessStarted -= GameManager_Process_Started;
                oldOne.Process.ProcessExited -= GameManager_Process_Exited;
            }
            newOne.Process.ProcessStarted += GameManager_Process_Started;
            newOne.Process.ProcessExited += GameManager_Process_Exited;
        }

        private void GameManager_Process_Started(in uint processId)
        {
            if (this.CheckAccess())
            {
                this.GameStartButtonState = GameStartButtonState.WaitingForGameExit;
            }
            else
            {
                var wrapper = new Wrapper_GameManager_ProcessStarted(in processId, this);
                Dispatcher.UIThread.InvokeAsync(wrapper.GameManager_Process_Started);
            }
        }

        private readonly struct Wrapper_GameManager_ProcessStarted
        {
            public readonly uint processId;
            private readonly MainWindow window;

            public Wrapper_GameManager_ProcessStarted(in uint processId, MainWindow window)
            {
                this.processId = processId;
                this.window = window;
            }

            public void GameManager_Process_Started()
            {
                this.window.GameManager_Process_Started(in this.processId);
            }
        }

        private void GameManager_Process_Exited(in uint processId)
        {
            if (this.CheckAccess())
            {
                this.GameStartButtonState = GameStartButtonState.CanStartGame;
            }
            else
            {
                var wrapper = new Wrapper_GameManager_ProcessExited(in processId, this);
                Dispatcher.UIThread.InvokeAsync(wrapper.GameManager_Process_Exited);
            }
        }

        private readonly struct Wrapper_GameManager_ProcessExited
        {
            public readonly uint processId;
            private readonly MainWindow window;

            public Wrapper_GameManager_ProcessExited(in uint processId, MainWindow window)
            {
                this.processId = processId;
                this.window = window;
            }

            public void GameManager_Process_Exited()
            {
                this.window.GameManager_Process_Exited(in this.processId);
            }
        }

        private async Task StuckInLoop_BrowseGameFolder()
        {
            var openFileOpts = new FilePickerOpenOptions()
            {
                AllowMultiple = false,
                Title = "选择已存在的游戏文件",
                FileTypeFilter = new List<FilePickerFileType>(2)
                {
                    new FilePickerFileType("Game Client File") { Patterns = new string[] { "manifest.json", "game.exe" } },
                    FilePickerFileTypes.All
                }
            };

            while (true) // You're stuck here!!!! Actually not, this is for re-entering the dialog when user doesn't click cancel.
            {
                var results = await StorageProvider.OpenFilePickerAsync(openFileOpts);
                if (results == null || results.Count == 0) break;

                var path = results[0].TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    await this.ShowInfoMsgBox("你选择的文件不在你本地系统中.", "Invalid item selected");
                    continue;
                }

                static string FolderGoBackFromGameExecutable(string path) => path.Remove(path.Length - GameManager.RelativePathToExecutablePath.Length - 1);

                string? selectedInstallationDirectory = IsClientOrManifest(path) switch
                {
                    true => Path.GetDirectoryName(path),
                    false => FolderGoBackFromGameExecutable(path),
                    _ => null
                };

                if (string.IsNullOrEmpty(selectedInstallationDirectory))
                {
                    await this.ShowInfoMsgBox("你选择的文件看起来并不是《尘白禁区》的游戏文件.", "Invalid item selected");
                    continue;
                }

                if ((await this.ShowYesNoMsgBox("检测到你的游戏在:" + Environment.NewLine
                    + selectedInstallationDirectory + Environment.NewLine + Environment.NewLine
                    + "你确定要选择这个路径吗?" + Environment.NewLine
                    + "(这个路径应该是 'manifest.json' 文件本身应在的位置，并且没有缺失任何其他文件)" + Environment.NewLine
                    + "(如果选择的路径不是你想要的路径，请把文件夹移动到其他地方再选择该文件夹)", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                {
                    this._launcherConfig.GameClientInstallationPath = selectedInstallationDirectory;
                    await this.AfterLoaded_Btn_GameStart();
                    break;
                }
            }
        }

        private async Task PerformGameClientUpdate(GameManager gameMgr, bool skipCrcTableCache = false)
        {
            var updater = gameMgr.Updater;
            this.GameStartButtonState = GameStartButtonState.UpdatingGameClient;
            var newCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.cancelSrc_Root.Token);
            var oldCancellation = Interlocked.Exchange(ref this.cancelSrc_UpdateGameClient, newCancellation);
            try
            {
                if (oldCancellation != null)
                {
                    oldCancellation.Cancel();
                    oldCancellation.Dispose();
                }
            }
            catch { }
            Action StopIndetermined = () =>
            {
                this.ProgressBar_Total.ProgressTextFormat = "文件自检和下载 ({1}%)";
                this.ProgressBar_Total.IsIndeterminate = false;
                this.ProgressBar_Download1.IsIndeterminate = false;
                this.ProgressBar_Download2.IsIndeterminate = false;
                this.ProgressBar_Download1.ShowProgressText = true;
                this.ProgressBar_Download2.ShowProgressText = true;
            };
            var progressCallback = new GameUpdaterProgressCallback(() =>
            {
                if (this.ProgressBar_Total.CheckAccess())
                {
                    StopIndetermined.Invoke();
                }
                else
                {
                    // Should be here, since we're under different thread that invoked this callback.
                    Dispatcher.UIThread.InvokeAsync(StopIndetermined);
                }
            });
            this.ProgressBar_Total.IsIndeterminate = true;
            this.ProgressBar_Download1.IsIndeterminate = true;
            this.ProgressBar_Download2.IsIndeterminate = true;
            this.ProgressBar_Total.ProgressTextFormat = "正在从远端下载文件";
            this.ProgressBar_Download1.ShowProgressText = false;
            this.ProgressBar_Download2.ShowProgressText = false;
            var uiUpdaterCancellation = DispatcherTimer.Run(() =>
            {
                if (!this.ProgressBar_Total.IsIndeterminate)
                {
                    // Yes, max is 50%, we split the "Total Progress" bar into 2, half for file checking, the other half for file downloading.
                    if (progressCallback.FileCheckProgress.IsDone && progressCallback.TotalDownloadProgress.IsDone)
                    {
                        this.ProgressBar_Total.Value = 100;
                    }
                    else
                    {
                        if (progressCallback.FileCheckProgress.IsDone)
                        {
                            this.ProgressBar_Total.Value = progressCallback.TotalDownloadProgress.GetPercentile();
                        }
                        else if (progressCallback.TotalDownloadProgress.IsDone)
                        {
                            this.ProgressBar_Total.Value = progressCallback.FileCheckProgress.GetPercentile();
                        }
                        else
                        {
                            long sumCurrent = progressCallback.TotalDownloadProgress.CurrentProgress + progressCallback.FileCheckProgress.CurrentProgress,
                                sumTotal = progressCallback.TotalDownloadProgress.TotalProgress + progressCallback.FileCheckProgress.TotalProgress;

                            var tmp = (sumCurrent * 100d) / sumTotal;
                            this.ProgressBar_Total.Value = tmp;
                        }
                    }
                }
                static void UpdateProgressBar(GameUpdaterDownloadProgressValue progress, ProgressBar attachedprogressbar)
                {
                    /* String.Format of the Avalonia ProgressBarText
                    0 = Value
                    1 = Value as a Percentage from 0 to 100 (e.g. Minimum = 0, Maximum = 50, Value = 25, then Percentage = 50)
                    2 = Minimum
                    3 = Maximum
                    */
                    var oldFilename = attachedprogressbar.Tag as string;
                    if (!string.Equals(oldFilename, progress.Filename, StringComparison.Ordinal))
                    {
                        attachedprogressbar.Tag = progress.Filename;
                        attachedprogressbar.ProgressTextFormat = string.Concat(Path.GetFileName(progress.Filename.AsSpan()), " ({1}%)");
                    }
                    if (progress.IsDone)
                    {
                        attachedprogressbar.Value = 100;
                    }
                    else if (progress.TotalProgress == 0 || attachedprogressbar.IsIndeterminate)
                    {
                        attachedprogressbar.Value = 0;
                    }
                    else
                    {
                        attachedprogressbar.Value = progress.GetPercentile();
                    }
                }

                UpdateProgressBar(progressCallback.Download1Progress, this.ProgressBar_Download1);
                UpdateProgressBar(progressCallback.Download2Progress, this.ProgressBar_Download2);

                return true;
            }, TimeSpan.FromMilliseconds(50), DispatcherPriority.Render);
            try
            {
                var cancelToken = newCancellation.Token;

                await updater.UpdateGameClientAsync(fixMode: skipCrcTableCache, progressCallback: progressCallback, cancellationToken: cancelToken);

                if (await updater.CheckForUpdatesAsync(cancelToken))
                {
                    this.GameStartButtonState = GameStartButtonState.RequiresUpdate;
                }
                else
                {
                    this.GameStartButtonState = GameStartButtonState.CanStartGame;
                }
            }
            catch (Exception ex)
            {
                this.GameStartButtonState = GameStartButtonState.RequiresUpdate;
                if (ex is not OperationCanceledException)
                    await this.ShowErrorMsgBox(ex);
            }
            finally
            {
                uiUpdaterCancellation.Dispose();
                Interlocked.Exchange(ref this.cancelSrc_UpdateGameClient, null); // Set it back to null
                newCancellation.Dispose();
                if (this.cancelSrc_Root.IsCancellationRequested)
                {
                    this.Close();
                }
            }
        }
    }
}
