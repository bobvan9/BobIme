namespace BobIme.Core;

/// <summary>候选来自哪套码表，混输时用来区分五笔和拼音。</summary>
public enum CandidateSource
{
    Wubi,
    Pinyin,
}

/// <summary>
/// 码表中的一个词条。<see cref="Index"/> 是行号，<see cref="Weight"/> 是词频：
/// 五笔码表没有权重列，靠行号排；拼音码表两者都有，权重优先。
/// </summary>
public readonly record struct Candidate(string Text, string Code, int Index, int Weight = 0, CandidateSource Source = CandidateSource.Wubi);
