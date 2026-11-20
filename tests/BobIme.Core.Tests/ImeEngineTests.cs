namespace BobIme.Core.Tests;

public class ImeEngineTests
{
    [Fact]
    public void Press_BuildsBufferAndCandidates()
    {
        var engine = new ImeEngine(TestDict.Table);

        var result = engine.Press('k');

        Assert.Equal(KeyStatus.Updated, result.Status);
        Assert.Equal("k", result.Buffer);
        Assert.Equal("中", result.Candidates[0].Text);
        Assert.False(result.AutoCommit);
    }

    [Fact]
    public void Press_AcceptsUpperCase()
    {
        var engine = new ImeEngine(TestDict.Table);

        engine.Press('K');
        engine.Press('H');
        engine.Press('L');

        Assert.Equal("khl", engine.Buffer);

        var result = engine.Press('G');
        Assert.Equal("khlg", result.Buffer);
        Assert.Equal("中国", result.Candidates[0].Text);
    }

    [Fact]
    public void Press_UnknownFirstKeyReportsNoMatch()
    {
        var engine = new ImeEngine(TestDict.Synthetic);

        Assert.Equal(KeyStatus.NoMatch, engine.Press('z').Status);
        Assert.False(engine.IsComposing);
    }

    [Fact]
    public void Press_KeepsBufferWhenKeyWouldBreakTheCode()
    {
        var engine = new ImeEngine(TestDict.Synthetic);
        engine.Press('b');
        engine.Press('l');

        Assert.Equal(KeyStatus.NoMatch, engine.Press('z').Status);
        Assert.Equal("bl", engine.Buffer);
        Assert.Equal(["阵", "除", "队"], engine.Candidates.Select(c => c.Text));
    }

    [Fact]
    public void Press_SetsAutoCommitForUniqueShortFullCode()
    {
        var engine = new ImeEngine(TestDict.Table);
        engine.Press('p');
        engine.Press('s');

        var result = engine.Press('u');

        Assert.True(result.AutoCommit);
        Assert.Equal("宋", engine.SelectFirst().Text);
    }

    [Fact]
    public void Press_FourCodeUniqueFullCodeDoesNotAutoCommit()
    {
        var engine = new ImeEngine(TestDict.Table);
        engine.Press('k');
        engine.Press('l');
        engine.Press('g');

        var result = engine.Press('h');

        // 四码唯一命中也不抢跑，留着等空格。
        Assert.False(result.AutoCommit);
        Assert.Equal("klgh", engine.Buffer);
        Assert.Equal("咽下", engine.SelectFirst().Text);
    }

    [Fact]
    public void Press_StartsOverAfterBufferFillsUp()
    {
        var engine = new ImeEngine(TestDict.Synthetic);
        foreach (var key in "aaaa")
        {
            engine.Press(key);
        }

        Assert.True(engine.IsBufferFull);
        Assert.Equal("b", engine.Press('b').Buffer);
    }

    [Fact]
    public void PressBackspace_ShrinksBufferThenEndsComposition()
    {
        var engine = new ImeEngine(TestDict.Table);
        foreach (var key in "khl")
        {
            engine.Press(key);
        }

        Assert.Equal("kh", engine.PressBackspace().Buffer);
        engine.PressBackspace();

        Assert.Equal("k", engine.Buffer);
        Assert.Equal("中", engine.Candidates[0].Text);

        engine.PressBackspace();
        Assert.False(engine.IsComposing);
        Assert.Equal(KeyStatus.Ignored, engine.PressBackspace().Status);
    }

    [Fact]
    public void SelectFirst_CommitsAndClearsBuffer()
    {
        var engine = new ImeEngine(TestDict.Table);
        foreach (var key in "khlg")
        {
            engine.Press(key);
        }

        var result = engine.SelectFirst();

        Assert.Equal(KeyStatus.Committed, result.Status);
        Assert.Equal("中国", result.Text);
        Assert.False(engine.IsComposing);
        Assert.Empty(engine.Candidates);
    }

    [Fact]
    public void SelectAt_PicksRequestedCandidate()
    {
        var engine = new ImeEngine(TestDict.Table);
        engine.Press('k');

        Assert.Equal("口", engine.SelectAt(1).Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void SelectAt_OutOfRangeIsIgnored(int index)
    {
        var engine = new ImeEngine(TestDict.Table);
        engine.Press('k');

        Assert.Equal(KeyStatus.Ignored, engine.SelectAt(index).Status);
        Assert.True(engine.IsComposing);
    }

    [Fact]
    public void SelectWithoutComposingIsIgnored() =>
        Assert.Equal(KeyStatus.Ignored, new ImeEngine(TestDict.Table).SelectFirst().Status);

    [Fact]
    public void PressEscape_DiscardsComposition()
    {
        var engine = new ImeEngine(TestDict.Table);
        engine.Press('k');

        engine.PressEscape();

        Assert.False(engine.IsComposing);
        Assert.Empty(engine.Candidates);
    }

    [Fact]
    public void NextPageAndPrevPage_WalkTheCachedPages()
    {
        var engine = ManyPerCode();
        engine.Press('a');

        Assert.Equal(1, engine.Page);
        Assert.Equal(3, engine.Pages);

        Assert.True(engine.NextPage());
        Assert.Equal(2, engine.Page);
        Assert.Equal(
            ["一9", "一10", "一11", "二0", "二1", "二2", "二3", "二4", "二5"],
            engine.Candidates.Select(c => c.Text));

        Assert.True(engine.NextPage());
        Assert.Equal(["二6", "二7", "二8", "二9", "二10", "二11"], engine.Candidates.Select(c => c.Text));

        // 最后一页待着不动，页码不许越过边界。
        Assert.False(engine.NextPage());
        Assert.Equal(3, engine.Page);

        Assert.True(engine.PrevPage());
        Assert.True(engine.PrevPage());
        Assert.False(engine.PrevPage());
        Assert.Equal(1, engine.Page);
    }

    [Fact]
    public void Press_ResetsToFirstPage()
    {
        var engine = ManyPerCode();
        engine.Press('a');
        engine.NextPage();

        engine.Press('b');

        Assert.Equal("ab", engine.Buffer);
        Assert.Equal(1, engine.Page);
        Assert.Equal("二0", engine.Candidates[0].Text);
    }

    [Fact]
    public void PressBackspace_ResetsToFirstPage()
    {
        var engine = ManyPerCode();
        engine.Press('a');
        engine.Press('b');
        Assert.True(engine.NextPage());

        engine.PressBackspace();

        Assert.Equal("a", engine.Buffer);
        Assert.Equal(1, engine.Page);
        Assert.Equal("一0", engine.Candidates[0].Text);
    }

    [Fact]
    public void SelectAt_IndexesTheCurrentPage()
    {
        var engine = ManyPerCode();
        engine.Press('a');
        engine.NextPage();

        // 数字键跟着当前页走：第二页第 1 位就是整表的第 10 条。
        Assert.Equal("一9", engine.SelectAt(0).Text);
    }

    [Fact]
    public void Paging_WithoutCompositionDoesNothing()
    {
        var engine = ManyPerCode();

        Assert.False(engine.NextPage());
        Assert.False(engine.PrevPage());
        Assert.Equal(1, engine.Page);
    }

    [Fact]
    public void Query_CachesOnlyFivePages()
    {
        var engine = new ImeEngine(TestDict.Table) { PageSize = 9 };
        engine.Press('k');

        // k 这个前缀在词库里能命中上千条，一次只留五页，翻到第五页就到头。
        Assert.Equal(5, engine.Pages);
        Assert.True(engine.NextPage());
        Assert.True(engine.NextPage());
        Assert.True(engine.NextPage());
        Assert.True(engine.NextPage());
        Assert.Equal(9, engine.Candidates.Count);
        Assert.False(engine.NextPage());
    }

    [Fact]
    public void Press_NonCodeKeyIsIgnored() =>
        Assert.Equal(KeyStatus.Ignored, new ImeEngine(TestDict.Table).Press('1').Status);

    [Fact]
    public void ActiveSource_WithNoTableOfThatKindComposesNothing()
    {
        var engine = new ImeEngine(TestDict.Table) { ActiveSource = CandidateSource.Pinyin };

        // 只挂了五笔表却选了拼音档：一套码表都不剩，一个键也组不起来。
        Assert.Equal(KeyStatus.NoMatch, engine.Press('k').Status);
        Assert.False(engine.IsComposing);
    }

    /// <summary>一码和二码各挂 12 条，PageSize 9 时正好跨三页。</summary>
    private static ImeEngine ManyPerCode() => new(
        new CodeTable(
        [
            .. Enumerable.Range(0, 12).Select(i => new Candidate($"一{i}", "a", i + 1)),
            .. Enumerable.Range(0, 12).Select(i => new Candidate($"二{i}", "ab", i + 13)),
        ]))
    { PageSize = 9 };
}
