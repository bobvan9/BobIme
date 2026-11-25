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
    public void Query_PutsSingleCharsAheadOfPhrasesOfTheSameCode()
    {
        // 极点原表 qqqq 把「狗狗」写在「金」前面，同码那一趟要反过来。
        var table = WubiTable([new("狗狗", "qqqq", 1), new("金", "qqqq", 2)]);

        Assert.Equal(["金", "狗狗"], table.Query("qqqq").Select(c => c.Text));
    }

    [Fact]
    public void Query_KeepsShortCodeFirstWithinTheSingleCharPass()
    {
        // 单字这一遍仍旧按码长由短到长，简码先出没有被词组挤掉。
        var table = WubiTable([new("语", "yyyy", 1), new("言", "y", 2), new("语言", "yy", 3)]);

        Assert.Equal(["言", "语", "语言"], table.Query("y").Select(c => c.Text));
    }

    [Fact]
    public void Query_CountsAnAstralCharAsASingleChar()
    {
        // 增补平面的字一个写成一对代理，长度是 2 却仍是单字，不能归到词组那一遍。
        var astral = char.ConvertFromUtf32(0x20000);
        var table = WubiTable([new("中国", "aaaa", 1), new(astral, "aaab", 2)]);

        Assert.Equal([astral, "中国"], table.Query("a").Select(c => c.Text));
    }

    [Fact]
    public void Query_LimitCutsInsideTheSingleCharPass()
    {
        // 取一页就只从单字里取，词组不会因为原本排在前面而占掉席位。
        var table = WubiTable([new("狗狗", "qqqq", 1), new("金", "qqqq", 2), new("淾", "qqqq", 3)]);

        Assert.Equal(["金", "淾"], table.Query("qqqq", 2).Select(c => c.Text));
    }

    [Fact]
    public void Query_PreservesTheOrderInsideEachPass() =>
        // 两遍各自维持原本那一套次序：单字仍按码长再按编码，词组接在后面照同样的次序。
        Assert.Equal(["全", "金", "狗狗", "珠联璧合"], WubiTable([
            new("狗狗", "qqqq", 1), new("金", "qqqq", 2), new("全", "qqqk", 3), new("珠联璧合", "qqqq", 4),
        ]).Query("qqq").Select(c => c.Text));

    private static CodeTable WubiTable(params Candidate[] entries) => new(entries);

    private static CodeTable PinyinTable(params Candidate[] entries) => new(entries, CodeTableFormat.Pinyin);

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
    public void PinyinQuery_PutsSingleCharsAheadOfHigherWeightPhrases()
    {
        // 规范 词频最高，可它是词组：单字那一遍走完才轮到它。
        var table = PinyinTable(
            new("规范", "guifan", 1, 500),
            new("鬼", "gui", 2, 300),
            new("贵", "gui", 3, 100));

        Assert.Equal(["鬼", "贵", "规范"], table.Query("gui").Select(c => c.Text));
    }

    [Fact]
    public void PinyinQuery_KeepsWeightOrderWithinThePhrasePass() =>
        Assert.Equal(["鬼", "规范", "规定"], PinyinTable([
            new("规定", "guiding", 1, 200), new("规范", "guifan", 2, 500), new("鬼", "gui", 3, 300),
        ]).Query("gui").Select(c => c.Text));

    [Fact]
    public void PinyinQuery_LimitCutsInsideTheSingleCharPass()
    {
        var table = PinyinTable(
            new("规范", "guifan", 1, 500),
            new("鬼", "gui", 2, 300),
            new("贵", "gui", 3, 100));

        Assert.Equal(["鬼"], table.Query("gui", 1).Select(c => c.Text));
    }

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

    [Fact]
    public void RealPinyin_SingleCharsLeadEveryPhraseOfAPrefix()
    {
        // 真表里 gui 的头席原本是词频最高的 规范，现在要让单字先走完一整遍。
        var hits = TestDict.Pinyin.Query("gui");

        Assert.NotEmpty(hits);
        Assert.False(IsPhrase(hits[0]));
        Assert.All(hits.SkipWhile(c => !IsPhrase(c)), c => Assert.True(IsPhrase(c)));
    }

    [Fact]
    public void RealDict_SingleCharsLeadEveryPhraseOfAPrefix()
    {
        var hits = TestDict.Table.Query("qqq");

        Assert.NotEmpty(hits);
        Assert.False(IsPhrase(hits[0]));
        Assert.All(hits.SkipWhile(c => !IsPhrase(c)), c => Assert.True(IsPhrase(c)));
    }

    /// <summary>码表意义上的词组：不止一个字符。增补平面的字写成一代理对，这里算一个字符。</summary>
    private static bool IsPhrase(Candidate candidate) => candidate.Text.EnumerateRunes().Count() > 1;

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
