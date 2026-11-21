namespace BobIme.Shell;

/// <summary>
/// 「英文窗口」名单的进出处置。判断放在这里、状态放在调用方，是为了能脱离真实窗口单测。
/// 名单只管进出这一刻，不改用户自己选的档：进窗口时把当前档位记下来，离开时还回去，
/// 中间用户手动切过的话就以他那次为准。
/// </summary>
public static class EnglishOnFocus
{
    /// <summary>进名单里的窗口要不要当场切英文：已经在英文档、或者正替用户撑着，就不动。</summary>
    public static bool ShouldForceEnglish(InputMode current, InputMode? held) =>
        held is null && current != InputMode.English;

    /// <summary>进不在名单里的窗口该还回哪一档；没记过就是不用还。</summary>
    public static InputMode? ModeToRestore(InputMode? held) => held;
}
