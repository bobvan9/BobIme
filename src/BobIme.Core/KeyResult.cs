namespace BobIme.Core;

public enum KeyStatus
{
    /// <summary>不是输入码按键，调用方应原样放行。</summary>
    Ignored,

    /// <summary>编码缓冲已更新，候选词可用。</summary>
    Updated,

    /// <summary>已产出上屏文本，缓冲清空。</summary>
    Committed,

    /// <summary>该按键会使当前编码无解，缓冲维持不变。</summary>
    NoMatch,
}

public sealed record KeyResult(
    KeyStatus Status,
    string Text,
    string Buffer,
    IReadOnlyList<Candidate> Candidates,
    bool AutoCommit);
