using System.Globalization;
using System.Resources;
using System.Threading;
using ClubControl.Shared;

namespace ClubControl.Client.Resources;

/// <summary>
/// Lock-screen/timer UI text (SPEC section 10). Strings.resx is the neutral/English base;
/// Strings.ru.resx and Strings.uz.resx are satellite translations picked by <see cref="Apply"/>.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new(typeof(Strings).FullName!, typeof(Strings).Assembly);

    /// <summary>Sets the thread's UI culture so every <see cref="Get"/> call returns the right language.</summary>
    public static void Apply(LockScreenLanguage language)
    {
        var cultureName = language switch
        {
            LockScreenLanguage.Russian => "ru",
            LockScreenLanguage.Uzbek => "uz",
            _ => "en"
        };

        var culture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);

    public static string ClassName(PcClass pcClass) => pcClass switch
    {
        PcClass.Vip => Get("ClassVip"),
        _ => Get("ClassStandard")
    };
}
