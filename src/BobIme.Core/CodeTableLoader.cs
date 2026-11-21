using System.Text;

namespace BobIme.Core;

/// <summary>读取纯文本码表，自动识别「词在前」和「码在前」两种列序。</summary>
public static class CodeTableLoader
{
    private static readonly char[] Whitespace = [' ', '\t'];

    public static CodeTable LoadFromFile(string path, CodeTableFormat? format = null)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        return Load(reader, format);
    }

    public static CodeTable Load(TextReader reader, CodeTableFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        format ??= CodeTableFormat.Wubi;

        var entries = new List<Candidate>();
        var lineNo = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNo++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or '[' or ';' or '/' or '-')
            {
                continue;
            }

            if (ParseLine(trimmed, lineNo, format) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return new CodeTable(entries, format);
    }

    private static Candidate? ParseLine(string line, int index, CodeTableFormat format)
    {
        var maxCodeLength = format.MaxCodeLength;
        var fields = line.Split(['\t'], StringSplitOptions.TrimEntries);
        if (fields.Length < 2)
        {
            fields = line.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        if (fields.Length < 2 || fields[0].Length == 0)
        {
            return null;
        }

        // 词在前是主流格式，先按它试；不成立再当码在前处理。
        string word, code;
        if (IsCode(fields[1], maxCodeLength))
        {
            (word, code) = (fields[0], fields[1].ToLowerInvariant());
        }
        else if (IsCode(fields[0], maxCodeLength))
        {
            (word, code) = (fields[1], fields[0].ToLowerInvariant());
        }
        else
        {
            return null;
        }

        // 本产品全程使用英文标点：码表里挂在编码上的中文标点一律不加载。
        if (StartsWithPunctuation(word))
        {
            return null;
        }

        // 极点这类码表把 ā á ǎ à a 整块挂在 copa、copu 这些编码上，混输有了真拼音表之后就是纯噪声。
        if (IsBareLatinLetter(word))
        {
            return null;
        }

        // 平假名整块挂在 cojp、片假名整块挂在 copj，本产品不打日文。
        if (StartsWithKana(word))
        {
            return null;
        }

        // 第三列可选，是拼音码表的词频；五笔码表没有这一列，权重恒为 0。
        var weight = 0;
        if (fields.Length >= 3 && int.TryParse(fields[2], out var parsed))
        {
            weight = parsed;
        }

        return new Candidate(word, code, index, weight, format.Source);
    }

    private static bool IsBareLatinLetter(string word) =>
        word.Length == 1 && IsLatinLetter(word[0]);

    /// <summary>平假名 ぁ-ゟ 加片假名 ゠-㋿。</summary>
    private static bool StartsWithKana(string word)
    {
        var first = word[0];
        return first >= 0x3041 && first <= 0x30FF;
    }

    /// <summary>拉丁字母既算 ASCII 的 a-zA-Z，也算带声调的 À-ÿ、ā-ſ、ǎ-ȏ 这些组合字符。</summary>
    private static bool IsLatinLetter(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' || (c >= 0x00C0 && c <= 0x024F);

    /// <summary>极点这类码表会把中文标点、声调符号按编码挂成词条，统一在加载时挡掉。</summary>
    private static bool StartsWithPunctuation(string word)
    {
        var first = word[0];
        return char.IsPunctuation(first)
            || char.IsSymbol(first)

            // CJK Symbols and Punctuation：、。〃〈〉《》「」【】〇々
            || (first >= 0x3000 && first <= 0x303F)

            // 声调修饰字母：ˇ ˉ ˙ ˜
            || (first >= 0x02B0 && first <= 0x02FF)

            // 私用区码位：显示成空白候选，还白占 nhty、xyxy 这些正常编码位
            || (first >= 0xE000 && first <= 0xF8FF);
    }

    private static bool IsCode(string field, int maxCodeLength) =>
        field.Length > 0 && field.Length <= maxCodeLength
        && field.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
}
