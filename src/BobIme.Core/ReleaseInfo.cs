using System.Text.Json;

namespace BobIme.Core;

/// <summary>
/// GitHub Releases 那条「最新一版」记录里要用到的两项。解析和版本比法都是纯函数，
/// 网络调用留在界外层，这样这一段不用真连 GitHub 也能单测。
/// </summary>
public sealed record ReleaseInfo(string Tag, string Url)
{
    /// <summary>去掉开头的 <c>v</c> 和两边空白；后面紧跟数字才算版本号前缀。</summary>
    public static string NormalizeTag(string? tag)
    {
        var text = (tag ?? "").Trim();
        return text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsAsciiDigit(text[1])
            ? text[1..]
            : text;
    }

    /// <summary>点分段、每段都是纯数字才算一个能比的版本号；认不出来的交给调用方判，别拿它当「有新版」。</summary>
    public static bool IsVersion(string? tag)
    {
        var text = NormalizeTag(tag);
        return text.Length > 0 && text.Split('.').All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }

    /// <summary>
    /// 按点分段逐段比数字，段数不足当 0。任何一边不是成形的版本号就整串按字符串比，
    /// 所以调用方要先用 <see cref="IsVersion"/> 把认不出来的挡掉。
    /// </summary>
    public static int Compare(string? left, string? right)
    {
        var a = NormalizeTag(left);
        var b = NormalizeTag(right);
        if (!IsVersion(a) || !IsVersion(b))
        {
            return string.CompareOrdinal(a, b);
        }

        var parts = (a.Split('.'), b.Split('.'));
        for (var i = 0; i < Math.Max(parts.Item1.Length, parts.Item2.Length); i++)
        {
            var result = ComparePart(
                i < parts.Item1.Length ? parts.Item1[i] : "0",
                i < parts.Item2.Length ? parts.Item2[i] : "0");
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    /// <summary>段内全是数字，先去掉前导零再比长度、最后按字典比：等价于比数值，还不用担心几位数溢出。</summary>
    private static int ComparePart(string left, string right)
    {
        var x = left.TrimStart('0');
        var y = right.TrimStart('0');
        return x.Length != y.Length ? (x.Length < y.Length ? -1 : 1) : string.CompareOrdinal(x, y);
    }

    /// <summary>读 <c>tag_name</c> 和 <c>html_url</c>；缺字段、类型不对、整份不是对象都算读不出来。</summary>
    public static ReleaseInfo? FromJson(string? json)
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var tag = ReadString(document.RootElement, "tag_name");
            var url = ReadString(document.RootElement, "html_url");
            return tag.Length > 0 && url.Length > 0 ? new ReleaseInfo(tag, url) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
