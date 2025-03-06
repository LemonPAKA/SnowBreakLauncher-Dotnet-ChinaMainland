using Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Leayal.SnowBreakLauncher.I18n
{
    public class LanguageItem
    {
        public string LanguageName { get; set; }
        public string LanguageCode { get; set; }
    }
    class LanguageHelpers
    {
        private CultureInfo culture;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetCurrentLanguageCode()
        {
            if (Application.Current is App app)
            {
                var languageCode = app.LeaLauncherConfig.LauncherLanguage;
                return languageCode;
            }
            return "en-US";
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetLanguageString(string languageKey)
        {
            return Resources.ResourceManager.GetString(languageKey,Resources.Culture);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ChangeGUICulture(string languageCode)
        {
            var culture = new CultureInfo(languageCode);
            Resources.Culture = culture;
        }
    }
}
