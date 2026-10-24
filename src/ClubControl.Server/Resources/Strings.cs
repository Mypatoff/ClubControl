using System.Globalization;
using System.Resources;
using System.Threading;
using ClubControl.Shared;

namespace ClubControl.Server.Resources;

/// <summary>
/// Admin panel UI text (SPEC section 10: all UI text in .resx files). Strings.resx is the neutral/English
/// base; Strings.ru.resx and Strings.uz.resx are satellite translations picked by <see cref="Apply"/>.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager = new(typeof(Strings).FullName!, typeof(Strings).Assembly);

    /// <summary>Sets the thread's UI culture so every <see cref="Get"/> call returns the right language.</summary>
    public static void Apply(AdminPanelLanguage language)
    {
        var cultureName = language switch
        {
            AdminPanelLanguage.Uzbek => "uz",
            _ => "ru"
        };

        var culture = new CultureInfo(cultureName);
        CultureInfo.CurrentUICulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    public static string Get(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);

    public static string ResourceKindName(ResourceKind kind) => kind switch
    {
        ResourceKind.Billiard => Get("KindBilliard"),
        _ => Get("KindComputer")
    };

    public static string LockScreenLanguageName(LockScreenLanguage language) => language switch
    {
        LockScreenLanguage.Russian => Get("LangRussian"),
        LockScreenLanguage.Uzbek => Get("LangUzbek"),
        _ => Get("LangEnglish")
    };

    public static string AdminPanelLanguageName(AdminPanelLanguage language) => language switch
    {
        AdminPanelLanguage.Uzbek => Get("LangUzbek"),
        _ => Get("LangRussian")
    };

    public static string AfterSessionActionName(AfterSessionAction action) => action switch
    {
        AfterSessionAction.Restart => Get("AfterSessionRestart"),
        _ => Get("AfterSessionClose")
    };

    public static string PcStateName(PcState state, bool isConfigured) => !isConfigured
        ? Get("StateNewPc")
        : state switch
        {
            PcState.Free => Get("StateFree"),
            PcState.Locked => Get("StateLocked"),
            PcState.PrepaySession => Get("StatePrepay"),
            PcState.PostpaySession => Get("StatePostpay"),
            PcState.LimitReached => Get("StateLimitReached"),
            PcState.OfflineUnlocked => Get("StateOfflineUnlocked"),
            _ => state.ToString()
        };

    public static string SessionTypeName(SessionType type, bool isFree) => isFree
        ? Get("SessionFree")
        : type switch
        {
            SessionType.Prepay => Get("StatePrepay"),
            SessionType.Postpay => Get("StatePostpay"),
            _ => Get("SessionFree")
        };
}
