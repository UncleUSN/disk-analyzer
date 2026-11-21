using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace DiskAnalyzer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("ru-RU")));
        }
        catch (CultureNotFoundException)
        {
            // Окно и так на русском. Формат чисел останется системным.
        }

        base.OnStartup(e);
    }
}
