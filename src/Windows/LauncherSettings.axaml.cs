using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Leayal.SnowBreakLauncher.I18n;
using Leayal.SnowBreakLauncher.Snowbreak;
using Leayal.SnowBreakLauncher.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Resources;

namespace Leayal.SnowBreakLauncher;

public partial class LauncherSettings : Window
{
    private bool isInDialog;
    //private string currentLanguage;
    private string currentLanguageCode;
    public LauncherSettings()
    {
        this.isInDialog = false;
        InitializeComponent();


        if (App.Current is App app)
        {
            var conf = app.LeaLauncherConfig;
            this.CheckBox_Networking_UseDoH.IsChecked = conf.Networking_UseDoH;
            this.CheckBox_AllowFetchingManifestFromOfficial.IsChecked = conf.AllowFetchingOfficialLauncherManifestData;
            this.CheckBox_AllowFetchingManifestFromOfficialInMemory.IsChecked = conf.AllowFetchingOfficialLauncherManifestDataInMemory;
            this.CheckBox_EnableStreamerMode.IsChecked = conf.StreamerMode;
            LoadLanguages(conf.LauncherLanguage);
        }
    }
    private void LoadLanguages(string configLanguageCode)
    {
        var languages = new List<LanguageItem>
           {
               new LanguageItem { LanguageName = "English", LanguageCode = "en-US" },
               new LanguageItem { LanguageName = "简体中文", LanguageCode = "zh-CN" }
           };
        currentLanguageCode = configLanguageCode;
        // 将语言项添加到 ComboBox
        foreach (var language in languages)
        {
            var comboBoxItem = new ComboBoxItem
            {
                Content = language.LanguageName,
                Tag = language // 将 LanguageItem 作为 Tag 存储
            };
            this.ComboBox_LanguageSelector.Items.Add(comboBoxItem);
        }
        foreach (ComboBoxItem item in this.ComboBox_LanguageSelector.Items)
        {
            var languageItem = item.Tag as LanguageItem;
            if (languageItem != null && languageItem.LanguageCode.Equals(configLanguageCode))
            {
                this.ComboBox_LanguageSelector.SelectedItem = item;
                break; // 找到后退出循环
            }
        }
    }
    private bool HasLanguageChanged(string newLanguageCode)
    {
        return !currentLanguageCode.Equals(newLanguageCode);
    }

    private void CloseBtn_Click(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }

    private async void SaveBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (App.Current is App app)
            {
                var conf = app.LeaLauncherConfig;
                var val_CheckBox_Networking_UseDoH = (this.CheckBox_Networking_UseDoH.IsChecked == true);
                var val_CheckBox_StreamerMode = (this.CheckBox_EnableStreamerMode.IsChecked == true);
                var selectedItem = this.ComboBox_LanguageSelector.SelectedItem as ComboBoxItem;
                var selectedLanguageItem = selectedItem?.Tag as LanguageItem;
                string newLanguageValue = selectedLanguageItem.LanguageCode;
                if (HasLanguageChanged(newLanguageValue))
                {
                    conf.LauncherLanguage = newLanguageValue;
                }
                conf.Networking_UseDoH = val_CheckBox_Networking_UseDoH;

                conf.AllowFetchingOfficialLauncherManifestData = (this.CheckBox_AllowFetchingManifestFromOfficial.IsChecked == true);
                conf.AllowFetchingOfficialLauncherManifestDataInMemory = (this.CheckBox_AllowFetchingManifestFromOfficialInMemory.IsChecked == true);
                conf.StreamerMode = val_CheckBox_StreamerMode;
                SnowBreakHttpClient.Instance.EnableDnsOverHttps = val_CheckBox_Networking_UseDoH;
                var installedDirectory = (conf.ServerSelect.Equals("Global")) ? app.GB_LauncherConfig.GameClientInstallationPath : app.CN_LauncherConfig.GameClientInstallationPath;
                if (!string.IsNullOrEmpty(installedDirectory))
                {
                    using (CensorshipSetting fs = new CensorshipSetting(installedDirectory))
                    {
                        fs.VerifyCensorship();
                    }
                }

                conf.Save();
                if (HasLanguageChanged(newLanguageValue))
                {
                    currentLanguageCode = newLanguageValue;
                    await MainWindow.ShowInfoMsgBox(this, LanguageHelpers.GetLanguageString("LanguageChanged"), LanguageHelpers.GetLanguageString("Information"));
                    Environment.Exit(0);
                }
            }
            this.Close(true);
        }
        catch (Exception ex)
        {
            await MainWindow.ShowErrorMsgBox(this, ex);
        }
    }
}