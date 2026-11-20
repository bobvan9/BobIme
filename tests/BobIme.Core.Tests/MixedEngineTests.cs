using BobIme.Shell;
using BobIme.Shell.Interop;

namespace BobIme.Core.Tests;

/// <summary>五笔表和拼音表同时挂上引擎时的混合查询行为。</summary>
public class MixedEngineTests
{
    private static readonly CodeTable Wubi = new(
    [
        new("中", "k", 1),
        new("口", "k", 2),
        new("咽下", "klgh", 3),
        new("马", "cg", 4),
        new("码", "cga", 5),
        new("宋", "psu", 6),
    ]);

    private static readonly CodeTable Pinyin = new(
    [
        new("可", "ke", 1, 900_000, CandidateSource.Pinyin),
        new("车", "che", 2, 800_000, CandidateSource.Pinyin),
        new("错", "cuo", 3, 700_000, CandidateSource.Pinyin),
        new("成", "cheng", 4, 600_000, CandidateSource.Pinyin),
        new("常", "chang", 5, 500_000, CandidateSource.Pinyin),
        new("中国", "zhongguo", 6, 400_000, CandidateSource.Pinyin),
        new("种过", "zhongguo", 7, 300_000, CandidateSource.Pinyin),
        new("中", "zhong", 8, 200_000, CandidateSource.Pinyin),
    ], CodeTableFormat.Pinyin);

    private static ImeEngine Engine() => new([Wubi, Pinyin]) { PageSize = 9 };

    [Fact]
    public void WubiCandidatesComeFirstAndPinyinFillsTheRest()
    {
        var candidates = Type("k");

        Assert.Equal(["中", "口", "咽下", "可"], candidates.Select(c => c.Text));
        Assert.Equal(CandidateSource.Pinyin, candidates[^1].Source);
        Assert.All(candidates.Take(3), c => Assert.Equal(CandidateSource.Wubi, c.Source));
    }

    [Fact]
    public void CodesLongerThanWubiStillMatchPinyin()
    {
        var candidates = Type("zhong");

        Assert.Equal(["中国", "种过", "中"], candidates.Select(c => c.Text));
        Assert.All(candidates, c => Assert.Equal(CandidateSource.Pinyin, c.Source));
    }

    [Fact]
    public void PinyinCandidatesAreRankedByWeightNotByCodeOrder()
    {
        // 纯按编码排会先出 chang，按词频排应当是 车、错、成、常。
        var pinyinHits = Type("c").Where(c => c.Source == CandidateSource.Pinyin).Select(c => c.Text);

        Assert.Equal(["车", "错", "成", "常"], pinyinHits);
    }

    [Fact]
    public void SameWordFromBothTablesAppearsOnce()
    {
        var wubi = new CodeTable([new("可", "kk", 1)]);
        var pinyin = new CodeTable(
            [new("可", "ke", 1, 900_000, CandidateSource.Pinyin)], CodeTableFormat.Pinyin);
        var engine = new ImeEngine([wubi, pinyin]) { PageSize = 9 };
        engine.Press('k');

        Assert.Equal(["可"], engine.Candidates.Select(c => c.Text));
    }

    [Fact]
    public void UniqueWubiShortFullCode_AutoCommitsWhenPinyinHasNothing()
    {
        Assert.True(TypeLast("psu").AutoCommit);
    }

    [Fact]
    public void UniqueWubiFourCode_DoesNotAutoCommit()
    {
        // 整页只有一条也不抢，四码要等空格。
        Assert.False(TypeLast("klgh").AutoCommit);
    }

    [Fact]
    public void PinyinFullCode_DoesNotAutoCommitWhileOtherCandidatesRemain()
    {
        // zhongguo 有两条候选，唯一性不成立，不该抢先把词送出去。
        Assert.False(TypeLast("zhongguo").AutoCommit);
    }

    [Fact]
    public void Space_PicksFirstMixedCandidate()
    {
        var controller = new ImeController(Engine());
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.Space));

        Assert.Equal("中", decision.CommitText);
    }

    [Fact]
    public void Digit_SelectsAcrossBothSources()
    {
        var controller = new ImeController(Engine());
        controller.OnKeyDown(Letter('k'));

        // 候选表是五笔段加拼音段拼起来的，第 4 位数字落在拼音段上。
        Assert.Equal("可", controller.OnKeyDown(Key(VirtualKey.D0 + 4)).CommitText);
    }

    private static uint LetterVk(char letter) => (uint)(VirtualKey.A + char.ToUpperInvariant(letter) - 'A');

    private static KeyEvent Letter(char letter) => Key(LetterVk(letter));

    private static KeyEvent Key(uint vk) => new(vk, 0, false, false, false, false);

    private static IReadOnlyList<Candidate> Type(string code) => TypeLast(code).Candidates;

    private static KeyResult TypeLast(string code)
    {
        var engine = Engine();
        var last = engine.Press(code[0]);
        foreach (var letter in code[1..])
        {
            last = engine.Press(letter);
        }

        return last;
    }
}
