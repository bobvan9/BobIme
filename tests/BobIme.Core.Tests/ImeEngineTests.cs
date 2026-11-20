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
    public void Press_NonCodeKeyIsIgnored() =>
        Assert.Equal(KeyStatus.Ignored, new ImeEngine(TestDict.Table).Press('1').Status);
}
