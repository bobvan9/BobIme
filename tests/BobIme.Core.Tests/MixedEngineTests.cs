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

    [Fact]
    public void EachPageKeepsTheWubiAndPinyinSplit()
    {
        var wubi = new CodeTable([.. Enumerable.Range(0, 12).Select(i => new Candidate($"五{i}", "n", i + 1))]);
        var pinyin = new CodeTable(
            [.. Enumerable.Range(0, 12).Select(i => new Candidate($"拼{i}", "n", i + 1, 1000 - i, CandidateSource.Pinyin))],
            CodeTableFormat.Pinyin);
        var engine = new ImeEngine([wubi, pinyin]) { PageSize = 9 };
        engine.Press('n');

        // 翻页不能把配比翻没：每页还是五笔 5 条、拼音 4 条，各表接着上次的次序往下取。
        Assert.Equal(3, engine.Pages);
        Assert.Equal(["五0", "五1", "五2", "五3", "五4", "拼0", "拼1", "拼2", "拼3"], Texts(engine));

        Assert.True(engine.NextPage());
        Assert.Equal(["五5", "五6", "五7", "五8", "五9", "拼4", "拼5", "拼6", "拼7"], Texts(engine));

        Assert.True(engine.NextPage());
        Assert.Equal(["五10", "五11", "拼8", "拼9", "拼10", "拼11"], Texts(engine));
    }

    [Fact]
    public void ActiveSource_WubiOnlyDropsThePinyinRun()
    {
        var engine = Engine();
        engine.ActiveSource = CandidateSource.Wubi;
        engine.Press('k');

        Assert.Equal(["中", "口", "咽下"], engine.Candidates.Select(c => c.Text));
        Assert.All(engine.Candidates, c => Assert.Equal(CandidateSource.Wubi, c.Source));
    }

    [Fact]
    public void ActiveSource_PinyinOnlyDropsTheWubiRun()
    {
        var engine = Engine();
        engine.ActiveSource = CandidateSource.Pinyin;
        engine.Press('k');

        Assert.Equal(["可"], engine.Candidates.Select(c => c.Text));
    }

    [Fact]
    public void ActiveSource_PinyinOnlyKeepsTheLongCodes()
    {
        var engine = Engine();
        engine.ActiveSource = CandidateSource.Pinyin;
        foreach (var letter in "zhongguo")
        {
            engine.Press(letter);
        }

        // 码长上限跟着挂上的表走：纯拼音时八位连写照样是编码，纯五笔四位就顶满了。
        Assert.Equal("zhongguo", engine.Buffer);
        Assert.Equal(["中国", "种过"], engine.Candidates.Select(c => c.Text));
    }

    [Fact]
    public void ActiveSource_ChangeDropsTheHalfTypedCode()
    {
        var engine = Engine();
        engine.Press('k');

        engine.ActiveSource = CandidateSource.Pinyin;

        Assert.False(engine.IsComposing);
        Assert.Empty(engine.Candidates);
    }

    [Fact]
    public void ActiveSource_OnControllerSwitchesWhatSpacePicks()
    {
        var controller = new ImeController(Engine()) { ActiveSource = CandidateSource.Pinyin };
        controller.OnKeyDown(Letter('k'));

        Assert.Equal("可", controller.OnKeyDown(Key(VirtualKey.Space)).CommitText);
    }

    [Fact]
    public void EnglishMode_KeepsTheMountedTableForTheNextChineseSession()
    {
        var controller = new ImeController(Engine()) { ActiveSource = CandidateSource.Pinyin };

        // 托盘选「英文输入」再切回来，挂的还是刚才那本码表，不该掉回混输。
        controller.SetChineseMode(false);
        controller.SetChineseMode(true);
        controller.OnKeyDown(Letter('k'));

        Assert.Equal("可", controller.OnKeyDown(Key(VirtualKey.Space)).CommitText);
    }

    [Fact]
    public void EnglishMode_DropsTheHalfTypedCode()
    {
        var controller = new ImeController(Engine());
        controller.OnKeyDown(Letter('k'));

        controller.SetChineseMode(false);

        Assert.False(controller.IsComposing);
        Assert.Equal("", controller.Buffer);
        Assert.False(controller.OnKeyDown(Letter('k')).Consume);
    }

    private static IEnumerable<string> Texts(ImeEngine engine) => engine.Candidates.Select(c => c.Text);

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
