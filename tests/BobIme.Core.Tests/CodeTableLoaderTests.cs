namespace BobIme.Core.Tests;

public class CodeTableLoaderTests
{
    [Fact]
    public void Load_ParsesWordFirstTabFormat()
    {
        var table = Load("工\ta\n中\tk");

        Assert.Equal(["中"], table.Query("k").Select(c => c.Text));
        Assert.Equal("a", table.Query("a")[0].Code);
    }

    [Fact]
    public void Load_ParsesCodeFirstTabFormat()
    {
        var table = Load("a\t工\nkhlg\t中国");

        Assert.Equal("中国", table.Query("khlg")[0].Text);
    }

    [Fact]
    public void Load_SkipsBlankCommentAndIniSectionLines()
    {
        var table = Load("""
            [Description]
            # 注释
            ; 另一种注释
            编码截止键=`

            工	a
            """);

        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Load_LowerCasesCodes() => Assert.Equal("khlg", Load("中国\tKHLG").Query("khlg")[0].Code);

    [Fact]
    public void Load_KeepsTableOrderAsIndex()
    {
        var entries = Load("工\ta\n戈\ta").Query("a");

        Assert.Equal(["工", "戈"], entries.Select(c => c.Text));
        Assert.Equal([1, 2], entries.Select(c => c.Index));
    }

    [Theory]
    [InlineData("中\tkhlg5")]
    [InlineData("中\tkh5")]
    [InlineData("中\tkhlgk")]
    [InlineData("中\t-")]
    public void Load_DropsEntriesWithUnusableCode(string line) => Assert.Equal(0, Load(line).Count);

    [Fact]
    public void Load_DropsPunctuationAndSymbolEntries()
    {
        var table = Load("，\tcobd\n、\tcobd\n〇\tllll\n中\tk");

        Assert.Equal(1, table.Count);
        Assert.Empty(table.Query("cobd"));
        Assert.Equal("中", table.Query("k")[0].Text);
    }

    [Fact]
    public void Load_DropsPrivateUseAreaEntries()
    {
        // 码表里的私用区字符在字体中显示成空白，直接写码位常量比写不可见字符可靠。
        var blank = (char)0xE76C;
        var table = Load($"{blank}\tcohb\n中\tk");

        Assert.Equal(1, table.Count);
        Assert.Empty(table.Query("cohb"));
    }

    [Fact]
    public void Load_PinyinFormatReadsWeightAndLongCodes()
    {
        // 拼音码表的音节列在入库前已经拼成连续字母，这里读的就是那一列。
        var table = Load("""
            # 注释
            中	zhong	200000
            中国	zhongguo	400000
            """, CodeTableFormat.Pinyin);

        Assert.Equal(2, table.Count);
        Assert.Equal(CandidateSource.Pinyin, table.Source);
        Assert.Equal(["中国", "中"], table.Query("zhong").Select(c => c.Text));
        Assert.Equal(400_000, table.Query("zhong")[0].Weight);
    }

    [Fact]
    public void Load_DropsBareLatinLetterEntries()
    {
        // 极点这类码表的声调字母块：ā á ǎ à 连 ASCII 的 a 一起挂在 copa 上，全部不加载。
        var table = Load($"{(char)0x0101}\tcopa\n{(char)0xE9}\tcopa\na\tcopa\n中\tk");

        Assert.Equal(1, table.Count);
        Assert.Empty(table.Query("copa"));
        Assert.Equal("中", table.Query("k")[0].Text);
    }

    [Fact]
    public void Load_DropsKanaEntries()
    {
        // ぁ 挂在 cojp、ア 挂在 copj，本产品不打日文。
        var table = Load($"{(char)0x3042}\tcojp\n{(char)0x30A2}\tcopj\n中\tk");

        Assert.Equal(1, table.Count);
        Assert.Empty(table.Query("cojp"));
        Assert.Empty(table.Query("copj"));
    }

    private static CodeTable Load(string text, CodeTableFormat? format = null) =>
        CodeTableLoader.Load(new StringReader(text), format);
}
