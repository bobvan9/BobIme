namespace BobIme.Core.Tests;

public class CodeTableTests
{
    private static readonly CodeTable Table = TestDict.Synthetic;

    [Fact]
    public void Query_PutsShortCodeHitsFirst() =>
        Assert.Equal(["a", "a", "aa"], Table.Query("a").Select(c => c.Code));

    [Fact]
    public void Query_DedupesSameTextAcrossCodeLengths() =>
        Assert.Equal(["工", "戈", "式"], Table.Query("a").Select(c => c.Text));

    [Fact]
    public void Query_MatchesLongerCodesAsPrefix() =>
        Assert.Equal(["阵", "除", "队"], Table.Query("bl").Select(c => c.Text));

    [Fact]
    public void Query_RespectsLimit() => Assert.Equal(2, Table.Query("a", 2).Count);

    [Theory]
    [InlineData("zz")]
    [InlineData("blgf")]
    public void Query_ReturnsEmptyForUnknownPrefix(string prefix) => Assert.Empty(Table.Query(prefix));

    [Theory]
    [InlineData("")]
    [InlineData("blggh")]
    public void Query_RejectsPrefixOutsideCodeLengthRange(string prefix) => Assert.Empty(Table.Query(prefix));

    [Fact]
    public void Query_LimitNonPositiveReturnsEmpty() => Assert.Empty(Table.Query("a", 0));

    [Theory]
    [InlineData("k", "中")]
    [InlineData("r", "的")]
    [InlineData("w", "人")]
    [InlineData("khlg", "中国")]
    [InlineData("ggtt", "五笔")]
    [InlineData("ltif", "输入法")]
    public void RealDict_FirstCandidateMatchesKnownCode(string code, string expected) =>
        Assert.Equal(expected, TestDict.Table.Query(code)[0].Text);

    [Fact]
    public void RealDict_ExcludesCodeTablePunctuationEntries()
    {
        Assert.Empty(TestDict.Table.Query("cobd"));
        Assert.Empty(TestDict.Table.Query("cohb"));

        var llll = TestDict.Table.Query("llll").Select(c => c.Text);
        Assert.DoesNotContain("〇", llll);
        Assert.Contains("田", llll);

        // 这些编码原本被私用区的空白词条占着，过滤后必须还原成正常词。
        Assert.All(TestDict.Table.Query("nhty"), c => Assert.Equal('收', c.Text[0]));

        // 声调字母块清空，但 cosx 上的 π 不属于这一类，要留着。
        Assert.Empty(TestDict.Table.Query("copa"));
        Assert.Empty(TestDict.Table.Query("copv"));
        Assert.Equal(["π"], TestDict.Table.Query("cosx").Select(c => c.Text));

        // 日语假名两块同样清空。
        Assert.Empty(TestDict.Table.Query("cojp"));
        Assert.Empty(TestDict.Table.Query("copj"));
    }

    [Fact]
    public void RealDict_ThreeLetterPrefixOnlyYieldsLongerCodes()
    {
        var candidates = TestDict.Table.Query("ltl");

        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(4, c.Code.Length));
    }
}
