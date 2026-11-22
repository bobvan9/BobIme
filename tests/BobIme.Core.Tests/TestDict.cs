namespace BobIme.Core.Tests;

internal static class TestDict
{
    private static readonly Lazy<CodeTable> Instance = new(() => CodeTableLoader.LoadFromFile(DictPath("jidian86.tsv")));

    private static readonly Lazy<CodeTable> PinyinInstance = new(() =>
        CodeTableLoader.LoadFromFile(DictPath("pinyin_base.tsv"), CodeTableFormat.Pinyin));

    /// <summary>仓库自带的极点 86 词库，供需要真实编码的断言使用。</summary>
    public static CodeTable Table => Instance.Value;

    /// <summary>仓库自带的拼音表，按词频排。</summary>
    public static CodeTable Pinyin => PinyinInstance.Value;

    public static CodeTable Synthetic { get; } = new(
    [
        new("工", "a", 1),
        new("戈", "a", 2),
        new("式", "aa", 3),
        new("工", "aaa", 4),
        new("工", "aaaa", 5),
        new("陈", "ba", 6),
        new("阵", "bl", 7),
        new("除", "blg", 8),
        new("队", "blge", 9),
    ]);

    /// <summary>拼音格式的合成表：码长不一，权重决定次序，首字母决定分桶。</summary>
    public static CodeTable SyntheticPinyin { get; } = new(
    [
        new("上", "shang", 1, 30),
        new("是", "shi", 2, 90),
        new("说", "shuo", 3, 80),
        new("时", "shi", 4, 70),
        new("少", "shao", 5, 20),
        new("三", "san", 6, 99),
        new("想", "xiang", 7, 50),
        new("手机", "shouji", 8, 5),
        new("咱", "zan", 9, 88),
    ], CodeTableFormat.Pinyin);

    /// <summary>词库文件的绝对路径；要往实例上挂规则的用例自己另加载一份，别共用下面这两个。</summary>
    internal static string DictPath(string file)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", "dict", file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"找不到 data/dict/{file}，测试需要先准备码表。");
    }
}
