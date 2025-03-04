using System;
using System.IO;
using System.Runtime.CompilerServices;
using Leayal.SnowBreakLauncher.Windows;

namespace Leayal.SnowBreakLauncher.Snowbreak;

public class CensorshipSetting : IDisposable
{
    private readonly string _filePath;
    private readonly string installedDirectory;
    private bool _disposed = false;

    public CensorshipSetting(string filePath)
    {
        installedDirectory = filePath;
        _filePath = installedDirectory + "\\localization.txt";
        EnsureFileExists();
    }
    private void EnsureFileExists()
    {
        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, "localization = 0"); 
        }
    }
    public string? ReadFirstLine()
    {
        using (var lines = File.ReadLines(_filePath).GetEnumerator())
        {
            if (lines.MoveNext())
            {
                return lines.Current;
            }
            return null;
        }
    }
    public void VerifyCensorship()
    {
        if(IsGameExisted(installedDirectory) && App.Current is App app)
        {
            bool streamerMode = app.LeaLauncherConfig.StreamerMode;
            string? localizationText = ReadFirstLine(); 
            if (streamerMode && localizationText.Equals("localization = 1"))
                File.WriteAllText(_filePath, "localization = 0");
            else if(!streamerMode && localizationText.Equals("localization = 0"))
                File.WriteAllText(_filePath, "localization = 1");
        }
    }
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源（如果有的话）
            }

            // 释放非托管资源（如果有的话）

            _disposed = true;
        }
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsGameExisted(string installDirectory) => System.IO.File.Exists(GameManager.GetGameExecutablePath(installDirectory));

    /// <summary></summary>
    /// <param name="path"></param>
    /// <returns>
    /// <para><see langword="true"/> if the path is a folder containing 'manifest.json' file.</para>
    /// <para><see langword="false"/> if the path is a folder containing 'game.exe' file.</para>
    /// <para><see langword="null"/> if neither cases above matched.</para>
    /// </returns>
    ~CensorshipSetting()
    {
        Dispose(false);
    }
}
