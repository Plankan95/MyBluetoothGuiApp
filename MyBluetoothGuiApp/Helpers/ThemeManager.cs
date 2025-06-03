using System; // Behövs för Uri, ArgumentException
using System.Linq; // Behövs för Linq-metoder som FirstOrDefault
using System.Windows; // Behövs för Application, ResourceDictionary
using System.Diagnostics; // Behövs för Debug.WriteLine

namespace BluetoothManager.Helpers
{
    // Denna statiska klass hanterar växlingen mellan appens teman (Ljust/Mörkt).
    // Den gör detta genom att ladda och byta ut ResourceDictionary-filer.
    public static class ThemeManager
    {
        // En enum för att definiera de olika temana vi har.
        public enum AppTheme
        {
            Light,
            Dark
        }

        // Sökvägar till våra XAML-resursfiler för temana.
        // Dessa sökvägar är relativa till projektets rot.
        private const string LightThemeUri = "Themes/LightTheme.xaml";
        private const string DarkThemeUri = "Themes/DarkTheme.xaml";

        // Publik metod för att sätta (växla) appens tema.
        public static void SetTheme(AppTheme theme)
        {
            Debug.WriteLine($"ThemeManager: Attemping to set theme to {theme}");
            var themeDictionary = new ResourceDictionary();
            string themeUri;

            switch (theme)
            {
                case AppTheme.Light:
                    themeUri = LightThemeUri;
                    break;
                case AppTheme.Dark:
                    themeUri = DarkThemeUri;
                    break;
                default:
                    throw new ArgumentException("Invalid theme specified.");
            }

            try
            {
                // Ladda den nya temadictionaryn.
                themeDictionary.Source = new Uri($"pack://application:,,,/{themeUri}", UriKind.Absolute);
                Debug.WriteLine($"ThemeManager: Successfully loaded theme dictionary from {themeUri}");

                // Hämta den samling av ResourceDictionary-objekt som är sammanslagna (MergedDictionaries)
                // i applikationens resurser. Detta är där våra temadictionaries ligger.
                var existingDictionaries = Application.Current.Resources.MergedDictionaries;

                // Försök hitta den befintliga temadictionaryn i den sammanslagna listan.
                // Vi letar efter en dictionary vars Source URI matchar någon av våra temafilar.
                var oldThemeDictionary = existingDictionaries.FirstOrDefault(d =>
                    d.Source != null &&
                    (d.Source.ToString().EndsWith(LightThemeUri, StringComparison.OrdinalIgnoreCase) ||
                     d.Source.ToString().EndsWith(DarkThemeUri, StringComparison.OrdinalIgnoreCase)));

                // Om vi hittade en gammal temadictionary, ta bort den.
                if (oldThemeDictionary != null)
                {
                    existingDictionaries.Remove(oldThemeDictionary);
                    Debug.WriteLine("ThemeManager: Removed old theme dictionary.");
                }

                // Lägg till den nya temadictionaryn i den sammanslagna listan.
                // Detta gör att de definierade resurserna (som färger) blir tillgängliga
                // för hela applikationen via DynamicResource-bindningar i XAML.
                existingDictionaries.Add(themeDictionary);
                Debug.WriteLine($"ThemeManager: Added new theme dictionary for {theme}.");

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ThemeManager: Error setting theme from {themeUri}: {ex.Message}");
                // I en produktionsapp skulle du kanske visa ett felmeddelande för användaren.
            }
        }

        // Valfri: Metod för att läsa av systemets aktuella tema (mer komplex)
        // public static bool IsSystemThemeDark() { /* Implementation här */ return false; }
    }
}