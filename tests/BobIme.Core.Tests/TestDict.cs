namespace BobIme.Core.Tests;

internal static class TestDict
{
    private static readonly Lazy<CodeTable> Instance = new(() => CodeTableLoader.LoadFromFile(Resolve()));

    /// <summary>仓库自带的极爽词库，供需要真实编码的断言使用。</summary>
    public static CodeTable Table => Instance.Value;

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

    private static string Resolve()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "data", "dict", "jisuan43.tsv");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("找不到 data/dict/jisuan43.tsv，测试需要先准备码表。");
    }
}
