namespace BobIme.Shell;

/// <summary>
/// 「英文窗口」「中文窗口」两张名单的进出处置。判断放在这里、状态放在调用方，是为了能脱离真实窗口单测。
/// 名单只管进出这一刻，不改用户自己选的码表档：进窗口时把当前档位记下来，离开时还回去，
/// 中间用户手动切过的话就以他那次为准。
/// </summary>
public static class ModeOnFocus
{
    /// <summary>
    /// 进名单里的窗口要不要当场切到目标档：不在两张名单里、已经在那一档、
    /// 或者正替用户撑着一档，都不动。
    /// </summary>
    public static bool ShouldApply(InputMode current, InputMode? held, InputMode? target) =>
        target is not null && held is null && current != target.Value;

    /// <summary>
    /// 离开名单该还回哪一档：只有还是我们顶上来的那一档才还。
    /// 用户在窗口里手动切过档，就以他那次为准，不该被我们搅回去。
    /// </summary>
    public static InputMode? ModeToRestore(InputMode? held, InputMode? forced, InputMode current) =>
        held is not null && current == forced ? held : null;
}
