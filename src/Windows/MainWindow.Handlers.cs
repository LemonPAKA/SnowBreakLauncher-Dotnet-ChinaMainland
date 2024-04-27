/*
 * This file contains mainly UI interaction and UI event handlers
*/

using Avalonia.Interactivity;
using Leayal.SnowBreakLauncher.Snowbreak;
using System.Collections.Generic;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Leayal.SnowBreakLauncher.Controls;
using Avalonia.Controls;
using System.Threading;
using Avalonia.Platform.Storage;
using System.IO;
using Leayal.Shared.Windows;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Reflection;
using Avalonia.Threading;

namespace Leayal.SnowBreakLauncher.Windows
{
    public partial class MainWindow
    {
        private CancellationTokenSource? cancelSrc_UpdateGameClient;
        protected override async void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            await Task.WhenAll(this.AfterLoaded_Btn_GameStart(), this.AfterLoaded_LauncherNews());
            _ = Task.Factory.StartNew(this.PeriodicallyRefreshNews, TaskCreationOptions.LongRunning);
        }

        private async Task PeriodicallyRefreshNews()
        {  
            try
            {
                using (var timer = new PeriodicTimer(TimeSpan.FromHours(8)))
                {
                    await timer.WaitForNextTickAsync();
                    var dispatcher = Dispatcher.UIThread;
                    if (dispatcher.CheckAccess())
                    {
                        // Can't be here anyway.
                        await this.AfterLoaded_LauncherNews();
                    }
                    else
                    {
                        await dispatcher.InvokeAsync(this.AfterLoaded_LauncherNews);
                    }
                }
            }
            catch { }
        }

        private void ExtraContextMenu_Initialized(object? sender, EventArgs e)
        {
            if (!OperatingSystem.IsWindows() && sender is ContextMenu ctxMenu)
            {
                var linuxWineSettingsBtn = new MenuItem();
                linuxWineSettingsBtn.Header = new TextBlock() { Text = "Wine 设置" };
                linuxWineSettingsBtn.Click += this.LinuxWineSettingsBtn_Click;
                ctxMenu.Items.Add(linuxWineSettingsBtn);
            }
        }

        [UnsupportedOSPlatform("windows")]
        private void LinuxWineSettingsBtn_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new LinuxWineSettings();
            dialog.ShowDialog(this);
        }

        private void LauncherVersionString_Initialized(object? sender, EventArgs e)
        {
            if (sender is TextBlock textBlock)
            {
                var lines = textBlock.Inlines;
                if (lines == null)
                {
                    lines = new Avalonia.Controls.Documents.InlineCollection();
                    textBlock.Inlines = lines;
                }
                lines.AddRange(new Avalonia.Controls.Documents.Run[]
                {
                    new Avalonia.Controls.Documents.Run("启动器版本: "),
                    new Avalonia.Controls.Documents.Run(Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "未知版本") { TextDecorations = Avalonia.Media.TextDecorations.Underline },
                    new Avalonia.Controls.Documents.Run(" (点击打开原版国际版三方启动器发布页，修改By 柠檬味雪碧)"),
                });
                Clickable.OnClick(textBlock, LauncherVersionString_Clicked);
            }
        }

        private static void LauncherVersionString_Clicked(TextBlock sender, RoutedEventArgs e)
        {
            OpenURLWithDefaultBrowser("https://github.com/Leayal/SnowBreakLauncher-Dotnet/releases/latest");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OpenURLWithDefaultBrowser(string url)
        {
            if (OperatingSystem.IsWindows())
            {
                // We really need this on Windows to avoid starting a new web browser process as Admin, in case this launcher is run as Admin.
                WindowsExplorerHelper.OpenUrlWithDefaultBrowser(url);
            }
            else
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                    Verb = "open"
                })?.Dispose();
            }
        }

        private async Task AfterLoaded_LauncherNews()
        {
            var httpClient = SnowBreakHttpClient.Instance;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static List<NewsInlineTextWrapper> ToClassItems(IEnumerable<INewsInlineTextItem> items, int preCount)
            {
                var list = (preCount == -1 ? new List<NewsInlineTextWrapper>() : new List<NewsInlineTextWrapper>(preCount));
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.link)) continue;
                    list.Add(new NewsInlineTextWrapper()
                    {
                        link = item.link,
                        time = item.time,
                        title = item.title
                    });
                }
                return list;
            }

            using (var newsFeed = await httpClient.GetLauncherNewsAsync())
            {
                var events = ToClassItems(newsFeed.Events, newsFeed.EventCount);
                this.LauncherNews_Events.ItemsSource = events.Count == 0 ? null : events;
                var notices = ToClassItems(newsFeed.Notices, newsFeed.NoticeCount);
                this.LauncherNews_Notices.ItemsSource = notices.Count == 0 ? null : notices;

                var listCount_banners = newsFeed.BannerCount;
                var list_banners = (listCount_banners == -1 ? new List<LauncherNewsBanner>() : new List<LauncherNewsBanner>(listCount_banners));
                foreach (var banner in newsFeed.Banners)
                {
                    if (string.IsNullOrWhiteSpace(banner.img) || string.IsNullOrWhiteSpace(banner.link)) continue;

                    list_banners.Add(new LauncherNewsBanner(banner.img, banner.link)
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                    });
                }

                this.LauncherNews_Banners.ItemsSource = list_banners.Count == 0 ? null : list_banners;
            }

            this.carouselAutoplay.StartAutoplay();
        }

        private void NewsItem_PointerPressed(NewsInlineTextWrapper obj)
        {
            if (obj != null)
            {
                try
                {
                    OpenURLWithDefaultBrowser(obj.link);
                }
                catch { }
            }
        }

        public void Btn_BannerGoLeft_Click(object source, RoutedEventArgs args) => this.carouselAutoplay.GoLeft();

        public void Btn_BannerGoRight_Click(object source, RoutedEventArgs args) => this.carouselAutoplay.GoRight();

        private async Task AfterLoaded_Btn_GameStart()
        {
            var installedDirectory = this._launcherConfig.GameClientInstallationPath;
            if (string.IsNullOrEmpty(installedDirectory) || !IsGameExisted(Path.GetFullPath(installedDirectory)))
            {
                // Game isn't installed or not detected
                this.GameStartButtonState = GameStartButtonState.NeedInstall;
            }
            else
            {
                var gameMgr = GameManager.SetGameDirectory(installedDirectory);
                if (gameMgr.Process.IsGameRunning)
                {
                    this.GameStartButtonState = GameStartButtonState.WaitingForGameExit;
                }
                else
                {
                    this.GameStartButtonState = GameStartButtonState.CheckingForUpdates;
                    var hasUpdate = await gameMgr.Updater.CheckForUpdatesAsync();
                    this.GameStartButtonState = hasUpdate switch
                    {
                        true => GameStartButtonState.RequiresUpdate,
                        _ => GameStartButtonState.CanStartGame
                    };
                }
            }
        }

        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            if (e.IsProgrammatic)
            {
                base.OnClosing(e);
            }
            else
            {
                base.OnClosing(e);
                if (!e.Cancel)
                {
                    if (this.GameStartButtonState == GameStartButtonState.UpdatingGameClient)
                    {
                        // Always stop closing down the window as long as we're still in the state above.
                        e.Cancel = true;
                        if (this.cancelSrc_Root.IsCancellationRequested)
                        {
                            // We're already issued exit signal.
                            // When updating is completely cancelled and finalized, it will call Window.Close(), which leads to "IsProgrammatic" above and close the window gracefully.
                            return;
                        }
                        else
                        {
                            if (e.CloseReason == WindowCloseReason.OSShutdown)
                            {
                                // Because the OS is shutting down, there should be no interaction/user input to prevent OS from being stuck at shutting down screen.
                                // We send signal without user confirmation.
                                this.cancelSrc_Root.Cancel();
                            }
                            else if ((await this.ShowYesNoMsgBox("游戏正在更新. 你确定要关闭启动器并且取消更新吗?", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                            {
                                // Send signal that the launcher should exit after complete the updating game client task.
                                this.cancelSrc_Root.Cancel();
                            }
                        }
                    }
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            GameManager.GameLocationChanged -= this.OnGameManagerChanged;
            if (GameManager.Instance is GameManager instance)
            {
                instance.Process.ProcessStarted -= this.GameManager_Process_Started;
                instance.Process.ProcessExited -= this.GameManager_Process_Exited;
            }
            this._launcherConfig.Dispose();
            this.carouselAutoplay.Dispose();
            this.cancelSrc_Root?.Dispose();
            base.OnClosed(e);
        }

        public async void Btn_UpdateCancel_Click(object source, RoutedEventArgs args)
        {
            // Don't need Interlocked, unneccessary to be strict here. It's just to avoid re-show the prompt below.
            // But it's still okay to re-show, too.
            if (this.cancelSrc_UpdateGameClient == null) return;

            if ((await this.ShowYesNoMsgBox("游戏正在更新. 你确定要取消更新吗?", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
            {
                // Steal it so that in case the button is clicked multiple times "at once", only the first click will do cancellation.
                var stolenCancelSrc = Interlocked.Exchange(ref this.cancelSrc_UpdateGameClient, null);
                stolenCancelSrc?.Cancel();
            }
        }

        public void BtnSettings_Click(object source, RoutedEventArgs args)
        {
            var btn = (source as Button) ?? (args.Source as Button);
            if (btn == null) return;
            if (btn.ContextMenu is ContextMenu ctxMenu) ctxMenu.Open(btn);
        }

        public async void MenuItem_OpenGameDataDirectory_Click(object source, RoutedEventArgs args)
        {
            var gameMgr = GameManager.Instance as GameManager;
            if (gameMgr == null)
            {
                await this.ShowDialog_LetUserKnowGameDirectoryIsNotSetForThisFunction();
                return;
            }
            var browsingDir = new string(gameMgr.FullPathOfGameDirectory);

            if (OperatingSystem.IsWindows())
            {
                // We really need this on Windows to avoid opening a new File Explorer process as Admin, in case this launcher is run as Admin.
                WindowsExplorerHelper.SelectPathInExplorer(browsingDir);
            }
            else
            {
                Process.Start(new ProcessStartInfo(browsingDir)
                {
                    UseShellExecute = true,
                    Verb = "open"
                })?.Dispose();
            }
        }

        public void MenuItem_LauncherSettings_Click(object source, RoutedEventArgs args)
        {
            var dialog = new LauncherSettings();
            dialog.ShowDialog(this);
        }

        public async void MenuItem_ChangeGameClientDirectory_Click(object source, RoutedEventArgs args)
        {
            var gameMgr = GameManager.Instance as GameManager;
            if (gameMgr == null)
            {
                await this.ShowDialog_LetUserKnowGameDirectoryIsNotSetForThisFunction();
                return;
            }

            if (this.GameStartButtonState != GameStartButtonState.CanStartGame) return;
            await this.StuckInLoop_BrowseGameFolder();
        }

        public async void MenuItem_GameDataIntegrityCheck_Click(object source, RoutedEventArgs args)
        {
            var gameMgr = GameManager.Instance as GameManager;
            if (gameMgr == null)
            {
                await this.ShowDialog_LetUserKnowGameDirectoryIsNotSetForThisFunction();
                return;
            }

            if (this.GameStartButtonState != GameStartButtonState.CanStartGame) return;

            if ((await ShowYesNoMsgBox("是否开始检查文件完整性并下载缺失和损坏的文件?" + Environment.NewLine
                + "(检查完整性可能会很快或者很慢, 取决于你的硬盘速度)", "Confirmation")) != MsBox.Avalonia.Enums.ButtonResult.Yes)
            {
                return;
            }

            try
            {
                await this.PerformGameClientUpdate(gameMgr, true);
            }
            catch (OperationCanceledException) { } // Silence it, user intentionally cancel it anyway
            catch (Exception ex)
            {
                await this.ShowErrorMsgBox(ex);
            }
            finally
            {
                this.GameStartButtonState = GameStartButtonState.CanStartGame;
            }
        }

        public async void Btn_StartGame_Click(object source, RoutedEventArgs args)
        {
            var localvar_btnGameStartState = this.GameStartButtonState; // can access field "this._gameStartButtonState" directly for performance;
            switch (localvar_btnGameStartState)
            {
                case GameStartButtonState.NeedInstall:
                    // GameManager.Instance should be null here.
                    {
                        var installedDirectory = this._launcherConfig.GameClientInstallationPath;
                        if (!string.IsNullOrEmpty(installedDirectory))
                        {
                            installedDirectory = Path.GetFullPath(installedDirectory);
                            if (IsGameExisted(installedDirectory))
                            {
                                if ((await ShowYesNoMsgBox("配置文件似乎被修改了." + Environment.NewLine
                                    + "是否要使用配置文件中的路径 (详情见下)?" + Environment.NewLine + Environment.NewLine
                                    + installedDirectory, "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                                {
                                    await this.AfterLoaded_Btn_GameStart();
                                    return;
                                }
                            }
                        }

                        var selection = await ShowYesNoCancelMsgBox("启动器检测不到你的游戏. 请选择以下选项:" + Environment.NewLine + Environment.NewLine
                                   + "- Yes: 选择你想要安装游戏的路径." + Environment.NewLine
                                   + "- No: 选择已存在游戏文件的路径." + Environment.NewLine
                                   + "- Cancel: 取消并返回.", "Prompt");
                        
                        if (selection == MsBox.Avalonia.Enums.ButtonResult.Yes)  // Browse for a folder, then install to the selected folder.
                        {
                            if (StorageProvider.CanPickFolder) // Should be true anyway, since this app is only Windows in mind.
                            {
                                var folderPickOpts = new FolderPickerOpenOptions()
                                {
                                    AllowMultiple = false,
                                    Title = "选择你想要安装游戏的路径"
                                };

                                while (true)
                                {
                                    var results = await StorageProvider.OpenFolderPickerAsync(folderPickOpts);
                                    if (results == null || results.Count == 0) break;

                                    var selectedPath = results[0].TryGetLocalPath();
                                    if (string.IsNullOrEmpty(selectedPath))
                                    {
                                        await this.ShowInfoMsgBox("选择的路径不是本地路径.", "Invalid item selected");
                                        continue;
                                    }
                                    
                                    if (!Directory.Exists(selectedPath))
                                    {
                                        if ((await this.ShowYesNoMsgBox("选择的路径不存在，是否创建文件夹?", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.No)
                                            continue;
                                    }

                                    if (IsGameExisted(selectedPath))
                                    {
                                        if ((await this.ShowYesNoMsgBox("检测到你的游戏在:" + Environment.NewLine
                                            + selectedPath + Environment.NewLine + Environment.NewLine
                                            + "你确定要选择这个路径吗?" + Environment.NewLine
                                            + "(这个路径应该是 'manifest.json' 文件本身应在的位置，并且没有缺失任何其他文件)", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                                        {
                                            this._launcherConfig.GameClientInstallationPath = selectedPath;
                                            await this.AfterLoaded_Btn_GameStart();
                                            break;
                                        }
                                    }

                                    if ((await this.ShowYesNoMsgBox("《尘白禁区》游戏安装目的地:" + Environment.NewLine
                                           + selectedPath + Environment.NewLine + Environment.NewLine
                                           + "你确定你想要在这里安装游戏吗?", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                                    {
                                        selectedPath = Directory.CreateDirectory(selectedPath).FullName;
                                        this._launcherConfig.GameClientInstallationPath = selectedPath;
                                        GameManager.SetGameDirectory(selectedPath);
                                        this.GameStartButtonState = GameStartButtonState.RequiresUpdate;
                                        this.Btn_StartGame_Click(source, args);
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                await this.ShowInfoMsgBox("你的系统不支持这个操作. 未知错误!!!", "Error", MsBox.Avalonia.Enums.Icon.Error);
                            }
                        }
                        else if (selection == MsBox.Avalonia.Enums.ButtonResult.No) // Browse for existing game client
                        {
                            if (StorageProvider.CanOpen) // Should be true anyway, since this app is only Windows in mind.
                            {
                                await this.StuckInLoop_BrowseGameFolder();
                            }
                            else
                            {
                                await this.ShowInfoMsgBox("你的系统不支持这个操作. 未知错误!!!", "Error", MsBox.Avalonia.Enums.Icon.Error);
                            }
                        }
                    }
                    break;
                case GameStartButtonState.CanStartGame:
                    {
                        if (GameManager.Instance is GameManager gameMgr)
                        {
                            var processMgr = gameMgr.Process;
                            if (processMgr.IsGameRunning)
                            {
                                if (OperatingSystem.IsWindows())
                                {
                                    this.GameStartButtonState = GameStartButtonState.WaitingForGameExit;
                                }
                                else
                                {
                                    await ShowInfoMsgBox("游戏已经在运行了", "Game already running");
                                }
                            }
                            else
                            {
                                var prevState = this.GameStartButtonState;
                                try
                                {
                                    this.GameStartButtonState = GameStartButtonState.CheckingForUpdates;
                                    if (await gameMgr.Updater.CheckForUpdatesAsync())
                                    {
                                        if ((await ShowYesNoMsgBox("游戏需要更新. 你想要现在更新吗?", "Confirmation")) == MsBox.Avalonia.Enums.ButtonResult.Yes)
                                        {
                                            this.GameStartButtonState = GameStartButtonState.RequiresUpdate;
                                            this.Btn_StartGame_Click(source, args);
                                        }
                                        else
                                        {
                                            this.GameStartButtonState = GameStartButtonState.RequiresUpdate;
                                        }
                                    }
                                    else
                                    {
                                        this.GameStartButtonState = GameStartButtonState.StartingGame;
                                        try
                                        {
                                            await processMgr.StartGame();
                                        }
                                        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                                        {
                                            this.GameStartButtonState = GameStartButtonState.CanStartGame;
                                        }
                                        catch (Exception ex)
                                        {
                                            this.GameStartButtonState = GameStartButtonState.CanStartGame;
                                            await this.ShowErrorMsgBox(ex);
                                            // MessageBox.Avalonia.MessageBoxManager
                                        }
                                    }
                                }
                                catch
                                {
                                    this.GameStartButtonState = prevState;
                                    throw;
                                }
                            }
                        }
                    }
                    break;
                case GameStartButtonState.RequiresUpdate:
                    {
                        if (GameManager.Instance is GameManager gameMgr)
                        {
                            var processMgr = gameMgr.Process;
                            if (processMgr.IsGameRunning)
                            {
                                this.GameStartButtonState = GameStartButtonState.WaitingForGameExit;
                            }
                            else
                            {
                                await this.PerformGameClientUpdate(gameMgr);
                            }
                        }
                    }
                    break;
                default:
                // case GameStartButtonState.LoadingUI:
                // case GameStartButtonState.StartingGame:
                // case GameStartButtonState.WaitingForGameExit:
                    // Do nothing
                    break;
            }
        }
    }
}
