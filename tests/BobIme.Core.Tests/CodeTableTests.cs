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

    [Fact]
    public void PinyinQuery_OrdersAcrossCodeLengthsByWeight() =>
        Assert.Equal(["是", "说", "时", "上", "少", "手机"], TestDict.SyntheticPinyin.Query("sh").Select(c => c.Text));

    [Fact]
    public void PinyinQuery_ReadsOnlyThePrefixBucket() =>
        Assert.DoesNotContain("咱", TestDict.SyntheticPinyin.Query("s").Select(c => c.Text));

    [Fact]
    public void PinyinQuery_FindsLowWeightEntryDeepInABigBucket()
    {
        // 六万条以上噪声压在前头，曾经扫到封顶就停的写法就会把末尾那条丢掉。
        var noisy = Enumerable.Range(0, 60_001).Select(i => new Candidate($"词{i}", "shi", i, 1_000_000)).ToList();
        noisy.Add(new("手机", "shouji", 60_002, 1));
        var table = new CodeTable(noisy, CodeTableFormat.Pinyin);

        Assert.Equal("手机", table.Query("shou").Select(c => c.Text).SingleOrDefault());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    public void PinyinQuery_RejectsPrefixThatCannotStartACode(string prefix) =>
        Assert.Empty(TestDict.SyntheticPinyin.Query(prefix));

    [Theory]
    [InlineData("pofang", "破防")]
    [InlineData("neijuan", "内卷")]
    [InlineData("tangping", "躺平")]
    [InlineData("dazi", "搭子")]
    public void RealPinyin_HoldsWordsRankedBelowTheOldScanCap(string code, string expected) =>
        // 这几个词一直在表里，但全局词频排在八万到二十万位，扫到六万条封顶的旧写法永远查不到。
        Assert.Contains(expected, TestDict.Pinyin.Query(code).Select(c => c.Text));

    [Fact]
    public void RealPinyin_HighestWeightComesFirst() => Assert.Equal("的", TestDict.Pinyin.Query("de")[0].Text);

    [Theory]
    [InlineData("k", "中")]
    [InlineData("r", "的")]
    [InlineData("w", "人")]
    [InlineData("khlg", "中国")]
    [InlineData("ggtt", "五笔")]
    [InlineData("ltif", "输入法")]
    public void RealDict_FirstCandidateMatchesKnownCode(string code, string expected) =>
        Assert.Equal(expected, TestDict.Table.Query(code)[0].Text);

    [Theory]
    [InlineData("ruuj", "抖音")]
    [InlineData("hktf", "点赞")]
    [InlineData("kfsg", "吐槽")]
    [InlineData("tmwy", "微信")]
    public void RealDict_HoldsTheRecentWords(string code, string expected) =>
        // 换极点 86 就是为了这批新词，老词库这些码位里根本没有这些字面。
        Assert.Contains(expected, TestDict.Table.Query(code).Select(c => c.Text));

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
