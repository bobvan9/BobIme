namespace BobIme.Core.Tests;

public class WordOrderTests
{
    /// <summary>规则要挂到实例上，所以每个用例各建一份，不去动 TestDict 里大家共用的那两张表。</summary>
    private static CodeTable Wubi(params Candidate[] entries) => new(entries);

    private static CodeTable Pinyin(params Candidate[] entries) => new(entries, CodeTableFormat.Pinyin);

    /// <summary>同码分带：一个码长一条，最后那档里挤着两个同码的词。</summary>
    private static CodeTable BandTable() => Wubi(
        new("一", "a", 1),
        new("二", "aa", 2),
        new("三", "aaa", 3),
        new("四", "aaaa", 4),
        new("五", "aaaa", 5),
        new("六", "aaab", 6));

    private static CodeTable PinyinTable() => Pinyin(
        new("上", "shang", 1, 30),
        new("是", "shi", 2, 90),
        new("说", "shuo", 3, 80),
        new("时", "shi", 4, 70),
        new("少", "shao", 5, 20),
        new("三", "san", 6, 99),
        new("想", "xiang", 7, 50),
        new("手机", "shouji", 8, 5));

    [Fact]
    public void Rule_SwapsCandidatesOfTheSameCode()
    {
        var table = Wubi(new("工", "a", 1), new("戈", "a", 2), new("式", "aa", 3));
        table.WordOrders = [new WordOrder("a", ["戈", "工"])];

        Assert.Equal(["戈", "工", "式"], table.Query("a").Select(c => c.Text));
    }

    [Fact]
    public void Rule_KeepsWordsItDoesNotListInTheOldOrder()
    {
        var table = Wubi(new("甲", "qqqq", 1), new("乙", "qqqq", 2), new("丙", "qqqq", 3));
        table.WordOrders = [new WordOrder("qqqq", ["丙"])];

        Assert.Equal(["丙", "甲", "乙"], table.Query("qqqq").Select(c => c.Text));
    }

    [Fact]
    public void Rule_LeavesCodeLengthOrderAlone()
    {
        var table = BandTable();
        table.WordOrders = [new WordOrder("aaaa", ["五", "四"])];

        Assert.Equal(["一", "二", "三", "五", "四", "六"], table.Query("a").Select(c => c.Text));
    }

    [Fact]
    public void Rule_MovesOnlyTheSeatsItsCodeAlreadyHolds()
    {
        // shi 占第 1、3 席：规则把「时」提到「是」前面，别的编码占的席位一个不动。
        var table = PinyinTable();
        table.WordOrders = [new WordOrder("shi", ["时", "是"])];

        Assert.Equal(["时", "说", "是", "上", "少", "手机"], table.Query("sh").Select(c => c.Text));
    }

    [Fact]
    public void Rule_WithoutAnyCandidatesForThatCodeChangesNothing()
    {
        var table = BandTable();
        table.WordOrders = [new WordOrder("zzzz", ["某", "些"])];

        Assert.Equal(["一", "二", "三", "四", "五", "六"], table.Query("a").Select(c => c.Text));
    }

    [Fact]
    public void NoRule_LeavesQueryUntouched()
    {
        var table = BandTable();
        Assert.Equal(["一", "二", "三", "四", "五", "六"], table.Query("a").Select(c => c.Text));
    }

    [Fact]
    public void RealDict_PromotesTheWordTheBaseTableRanksSecond()
    {
        // qqqq 在极点原表里是「狗狗」在前、「金」在后，正是用户要反过来那一例。
        var table = CodeTableLoader.LoadFromFile(TestDict.DictPath("jidian86.tsv"));
        Assert.Equal(["狗狗", "金"], table.Query("qqqq").Select(c => c.Text).Take(2));

        table.WordOrders = [new WordOrder("qqqq", ["金", "狗狗"])];
        Assert.Equal("金", table.Query("qqqq")[0].Text);
    }

    [Fact]
    public void Apply_ReturnsTheSameListWhenThereIsNothingToReorder()
    {
        var hits = BandTable().Query("a");
        Assert.Same(hits, WordOrder.Apply(hits, []));
        Assert.Same(hits, WordOrder.Apply(hits, null));
    }

    [Theory]
    [InlineData("qqqq:金,狗狗", "qqqq", new[] { "金", "狗狗" })]
    [InlineData(" QQQQ : 金 , 狗狗 ", "qqqq", new[] { "金", "狗狗" })]
    [InlineData("kan:呓", "kan", new[] { "呓" })]
    public void Parse_ReadsTheShortToken(string token, string code, string[] words)
    {
        var rule = WordOrder.Parse(token);
        Assert.NotNull(rule);
        Assert.Equal(code, rule!.Code);
        Assert.Equal(words, rule.Words);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("qqqq")]
    [InlineData(":金,狗狗")]
    [InlineData("qq1:金")]
    [InlineData("qqqq:")]
    [InlineData("  ")]
    public void Parse_RejectsTokensWithoutCodeOrWords(string? token) =>
        Assert.Null(WordOrder.Parse(token));

    [Fact]
    public void Token_RoundTripsThroughParse()
    {
        var rule = new WordOrder("qqqq", ["金", "狗狗"]);
        var parsed = WordOrder.Parse(rule.Token);

        Assert.Equal(rule.Code, parsed!.Code);
        Assert.Equal(rule.Words, parsed.Words);
    }

    [Fact]
    public void Normalize_DropsBlankRulesAndKeepsTheFirstOfACode()
    {
        var kept = WordOrder.Normalize(
        [
            new WordOrder("qqqq", ["金", "狗狗"]),
            new WordOrder("", ["无"]),
            new WordOrder("ab1", ["无"]),
            new WordOrder("aaaa", ["一"]),
            new WordOrder("qqqq", ["狗", "金"]),
            new WordOrder("kan", ["", "  ", "呓"]),
        ]);

        Assert.Equal(["qqqq", "aaaa", "kan"], kept.Select(rule => rule.Code));
        Assert.Equal(["金", "狗狗"], kept[0].Words);
        Assert.Equal(["呓"], kept[2].Words);
    }

    [Fact]
    public void Normalize_LowercasesTheCode() =>
        Assert.Equal("qqqq", WordOrder.Normalize([new WordOrder("QQQQ", ["金", "狗狗"])])[0].Code);

    [Fact]
    public void Normalize_CapsWordsAndRules()
    {
        var many = Enumerable.Range(0, 40).Select(i => $"词{i}").ToArray();
        Assert.Equal(WordOrder.MaxWords, WordOrder.Normalize([new WordOrder("aaaa", many)])[0].Words.Length);

        var rules = WordOrder.Normalize(Enumerable.Range(0, 40).Select(i => new WordOrder(CodeOf(i), ["甲", "乙"])));
        Assert.Equal(WordOrder.Max, rules.Count);
    }

    /// <summary>两个字母拼出的互不相同的编码，用来造超出条数上限的规则表。</summary>
    private static string CodeOf(int index) => $"{(char)('a' + index / 26)}{(char)('a' + index % 26)}";

    [Fact]
    public void Normalize_DropsRepeatedWordsInsideARule() =>
        Assert.Equal(["甲", "乙"], WordOrder.Normalize([new WordOrder("aa", ["甲", "乙", "甲"])])[0].Words);

    [Fact]
    public void RankOf_PutsUnlistedWordsLast()
    {
        var rule = new WordOrder("qqqq", ["金", "狗狗"]);
        Assert.Equal(0, rule.RankOf("金"));
        Assert.Equal(1, rule.RankOf("狗狗"));
        Assert.Equal(int.MaxValue, rule.RankOf("他"));
    }

    [Theory]
    [InlineData("QQQQ", "qqqq")]
    [InlineData(" qqqq ", "qqqq")]
    public void CodeKey_LowercasesAndTrims(string code, string expected) =>
        Assert.Equal(expected, WordOrder.CodeKey(code));

    [Theory]
    [InlineData("")]
    [InlineData("qq1")]
    [InlineData("qq-qq")]
    [InlineData("aaaaaaaaaaaaaaaab")]
    public void CodeKey_RejectsAnythingThatIsNotAPlainCode(string code) =>
        Assert.Equal("", WordOrder.CodeKey(code));

    [Fact]
    public void Engine_HandsTheRulesToEveryTable()
    {
        var engine = new ImeEngine([WubiTable(), PinyinTable()])
        {
            WordOrders = [new WordOrder("qqqq", ["金", "狗狗"]), new WordOrder("shi", ["时", "是"])],
        };

        Assert.Equal("金", engine.Peek("qqqq")[0].Text);
        Assert.Equal("时", engine.Peek("sh")[0].Text);
    }

    [Fact]
    public void Engine_ClearsTheRulesWhenGivenNone()
    {
        var engine = new ImeEngine(WubiTable()) { WordOrders = [new WordOrder("qqqq", ["金", "狗狗"])] };
        engine.WordOrders = [];

        Assert.Equal("狗狗", engine.Peek("qqqq")[0].Text);
    }

    [Fact]
    public void Peek_LeavesTheCompositionAlone()
    {
        var engine = new ImeEngine(WubiTable());
        engine.Press('q');
        engine.Press('q');
        var hits = engine.Peek("qqqq");

        Assert.NotEmpty(hits);
        Assert.Equal("qq", engine.Buffer);
        Assert.True(engine.IsComposing);
    }

    [Fact]
    public void Peek_HonoursTheActiveSource()
    {
        var engine = new ImeEngine([WubiTable(), PinyinTable()]) { ActiveSource = CandidateSource.Wubi };

        Assert.Empty(engine.Peek("shouji"));
        Assert.NotEmpty(engine.Peek("qqqq"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("qq1")]
    [InlineData("qqqqq")]
    public void Peek_ReturnsEmptyForCodeOutsideTheTable(string code) =>
        Assert.Empty(new ImeEngine(WubiTable()).Peek(code));

    /// <summary>两张合成码表：五笔那一张把 qqqq 的同码次序照真词库摆。</summary>
    private static CodeTable WubiTable() => Wubi(
        new("狗狗", "qqqq", 1),
        new("金", "qqqq", 2),
        new("全", "qqqk", 3));
}
