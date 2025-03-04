using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Leayal.SnowBreakLauncher.Classes;
using Leayal.SnowBreakLauncher.Windows;
using Leayal.SnowBreakLauncher.I18n;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.Versioning;
using Avalonia.Controls;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Leayal.SnowBreakLauncher;

public partial class App : Application
{
    private MainWindow mainWindow;
    public readonly OfficialJsonConfiguration CN_LauncherConfig;
    public readonly OfficialJsonConfiguration GB_LauncherConfig;
    public readonly LeaLauncherConfiguration LeaLauncherConfig;
    internal readonly Program.InstanceController? ProcessInstance;
    public readonly string? proxyUrl;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public App() : base()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        var rootDir = AppContext.BaseDirectory;
        // Hardcoded to use the "preference.json" in the same folder.
        // This means after-build:
        // - Either copies the launcher's files to the game's directory (locating its "preference.json" file).
        // - Or copies the file "preference.json" from the official launcher's folder to this launcher's folder, then edit the copied json file to correct the game client's location path.
        this.CN_LauncherConfig = new OfficialJsonConfiguration(Path.GetFullPath("cn_preference.json", rootDir));
        this.GB_LauncherConfig = new OfficialJsonConfiguration(Path.GetFullPath("gb_preference.json", rootDir));
        this.LeaLauncherConfig = new LeaLauncherConfiguration(Path.GetFullPath("lea-sblauncher.json", rootDir));
        LanguageHelpers.ChangeGUICulture(this.LeaLauncherConfig.LauncherLanguage);
    }

    internal App(Program.InstanceController processInstance) : this(processInstance, null) { }

    internal App(Program.InstanceController processInstance, string? proxyUrl) : this()
    {
        this.ProcessInstance = processInstance;
        this.proxyUrl = proxyUrl;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mainWindow = new MainWindow(this.CN_LauncherConfig,this.GB_LauncherConfig);
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}