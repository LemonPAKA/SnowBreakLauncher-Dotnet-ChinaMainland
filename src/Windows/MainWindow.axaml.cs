/*
 * This file contains mainly UI manipulation code
*/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Leayal.SnowBreakLauncher.Snowbreak;
using Leayal.SnowBreakLauncher.Classes;
using Leayal.SnowBreakLauncher.Controls;
using Leayal.SnowBreakLauncher.I18n;
using System;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace Leayal.SnowBreakLauncher.Windows;

public partial class MainWindow : Window
{
    private readonly CarouselAutoplay carouselAutoplay;

    public MainWindow(OfficialJsonConfiguration cn_launcherConfig, OfficialJsonConfiguration gb_launcherConfig)
    {
        this._cnlauncherConfig = cn_launcherConfig;
        this._gblauncherConfig = gb_launcherConfig;
        this.cancelSrc_Root = new System.Threading.CancellationTokenSource();
        GameManager.GameLocationChanged += this.OnGameManagerChanged;
        InitializeComponent();
        if (EmbedResources.WindowIcon.Value is Bitmap bm)
        {
            this.Icon = new WindowIcon(bm);
        }
        this.GameStartButtonState = GameStartButtonState.LoadingUI;
        this.carouselAutoplay = new CarouselAutoplay(this.LauncherNews_Banners);
        this.LauncherNews_Banners.GetObservable(Carousel.SelectedIndexProperty).Subscribe(this.OnLauncherNews_Banners_IndexChanged, this.cancelSrc_Root.Token);
        this.LauncherNews_Banners.GetObservable(Carousel.ItemCountProperty).Subscribe(this.OnLauncherNews_Banners_ItemCountChanged, this.cancelSrc_Root.Token);
        this.ServerSelector.Text = LanguageHelpers.GetLanguageString(GetGameServer());
    }

    #region "| LauncherNews_Banners Paging |"
    private void OnLauncherNews_Banners_IndexChanged(int value)
    {
        this.TB_LauncherNews_Banners_PagingCurrent.Text = (value + 1).ToString();
    }

    private void OnLauncherNews_Banners_ItemCountChanged(int value)
    {
        if (value == 0)
        {
            this.TB_LauncherNews_Banners_Paging.IsVisible = false;
        }
        else
        {
            this.TB_LauncherNews_Banners_Paging.IsVisible = true;
            this.TB_LauncherNews_Banners_PagingCount.Text = value.ToString();
        }
    }
    #endregion

    #region "| GameStartButtonState |"
    private GameStartButtonState _gameStartButtonState;

    public static readonly DirectProperty<MainWindow, GameStartButtonState> GameStartButtonStateProperty = AvaloniaProperty.RegisterDirect<MainWindow, GameStartButtonState>("GameStartButtonState",
        window => window._gameStartButtonState,
        (window, value) =>
        {
            if (window._gameStartButtonState != value)
            {
                window._gameStartButtonState = value;
                switch (value)
                {
                    case GameStartButtonState.CheckingForUpdates:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = true;
                        window.ProgressBar_Main.IsVisible = true;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("CheckingGameClientUpdate");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.RequiresUpdate:
                        window.Btn_StartGame.IsEnabled = true;
                        window.ProgressBar_Main.IsIndeterminate = false;
                        window.ProgressBar_Main.IsVisible = false;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("UpdateGameClient");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.UpdatingGameClient:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = false;
                        window.ProgressBar_Main.IsVisible = false;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("UpdatingGameClient");

                        window.BtnGameStart_Page.SelectedIndex = 1;
                        break;
                    case GameStartButtonState.CanStartGame:
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("StartGame");
                        window.ProgressBar_Main.IsIndeterminate = false;
                        window.ProgressBar_Main.IsVisible = false;
                        window.Btn_StartGame.IsEnabled = true;

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.StartingGame:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = true;
                        window.ProgressBar_Main.IsVisible = true;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("StartingGame");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.WaitingForGameExit:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = false;
                        window.ProgressBar_Main.IsVisible = false;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("RunningGame");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.NeedInstall:
                        window.Btn_StartGame.IsEnabled = true;
                        window.ProgressBar_Main.IsIndeterminate = false;
                        window.ProgressBar_Main.IsVisible = false;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("InstallorSelectGameFile");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    case GameStartButtonState.PreparingGeneric:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = true;
                        window.ProgressBar_Main.IsVisible = true;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("Preparing");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                    default:
                        window.Btn_StartGame.IsEnabled = false;
                        window.ProgressBar_Main.IsIndeterminate = true;
                        window.ProgressBar_Main.IsVisible = true;
                        window.BtnText_StartGame.Text = LanguageHelpers.GetLanguageString("LoadingLauncherUI");

                        window.BtnGameStart_Page.SelectedIndex = 0;
                        break;
                }
            }
        }, GameStartButtonState.LoadingUI);

    public GameStartButtonState GameStartButtonState
    {
        get => this.GetValue(GameStartButtonStateProperty);
        set => this.SetValue(GameStartButtonStateProperty, value);
    }
    #endregion
}