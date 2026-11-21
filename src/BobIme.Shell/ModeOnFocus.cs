namespace BobIme.Shell;

/// <summary>
/// 「按程序定档」规则表的进出处置。判断放在这里、状态放在调用方，是为了能脱离真实窗口单测。
/// 规则表只管进出这一刻，不改用户自己选的码表档：进表里的窗口时把当前档位记下来，离开时还回去，
/// 中间用户手动切过的话就以他那次为准。
/// </summary>
public static class ModeOnFocus
{
    /// <summary>
    /// 进表要不要当场切到目标档：表里没有、已经在那一档、
    /// 或者这个窗口刚才已经顶过一档（用户在里头自己切过的话以他那次为准），都不动。
    /// 不能拿「手上还替用户撑着一档」当挡箭牌：表里两个程序来回切的时候那一条记录是上一个窗口留下的，
    /// 挡掉的正是下一个窗口该顶的那一档。
    /// </summary>
    public static bool ShouldApply(InputMode current, InputMode? target, bool alreadyForcedOn) =>
        target is not null && !alreadyForcedOn && current != target.Value;

    /// <summary>
    /// 离开名单该还回哪一档：只有还是我们顶上来的那一档才还。
    /// 用户在窗口里手动切过档，就以他那次为准，不该被我们搅回去。
    /// </summary>
    public static InputMode? ModeToRestore(InputMode? held, InputMode? forced, InputMode current) =>
        held is not null && current == forced ? held : null;
}
