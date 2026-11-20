namespace BobIme.Core;

/// <summary>
/// 码表的读法：五笔按码长分带查（简码先出），拼音按词频降序线性扫（高频词先出）。
/// </summary>
public sealed record CodeTableFormat(int MaxCodeLength, CandidateSource Source, bool RankByWeight)
{
    public static readonly CodeTableFormat Wubi = new(4, CandidateSource.Wubi, false);

    public static readonly CodeTableFormat Pinyin = new(16, CandidateSource.Pinyin, true);
}

/// <summary>
/// 码表。五笔这类没有权重列的按码长分带存储，查询前缀时短码（简码）先出；
/// 拼音这类带词频的按词频排一次，查前缀时高频词先出。
/// </summary>
public sealed class CodeTable
{
    /// <summary>按词频排序查询时最多扫多少条，防止生僻前缀把一次按键拖慢。</summary>
    private const int WeightScanBudget = 60_000;

    private readonly Candidate[][] _bands;
    private readonly Candidate[] _byWeight;

    public CodeTable(IReadOnlyList<Candidate> entries, CodeTableFormat? format = null)
    {
        format ??= CodeTableFormat.Wubi;
        ArgumentOutOfRangeException.ThrowIfLessThan(format.MaxCodeLength, 1);
        Format = format;
        MaxCodeLength = format.MaxCodeLength;

        if (format.RankByWeight)
        {
            // 词频降序；同权重退回码表原始次序，保证每轮加载结果一致。
            _byWeight = [.. entries.OrderByDescending(e => e.Weight).ThenBy(e => e.Index)];
            _bands = [];
        }
        else
        {
            _byWeight = [];
            _bands = new Candidate[MaxCodeLength + 1][];
            for (var len = 1; len <= MaxCodeLength; len++)
            {
                var band = new List<Candidate>();
                foreach (var e in entries)
                {
                    if (e.Code.Length == len)
                    {
                        band.Add(e);
                    }
                }

                // 同码内保留码表原始次序：极点系码表的行号本身就是按词频排的。
                _bands[len] = [.. band.OrderBy(e => e.Code, StringComparer.Ordinal).ThenBy(e => e.Index)];
            }
        }

        Count = entries.Count;
    }

    public CodeTableFormat Format { get; }

    public CandidateSource Source => Format.Source;

    public int MaxCodeLength { get; }

    public int Count { get; }

    /// <summary>返回所有以 <paramref name="prefix"/> 开头的词条；同词条只出现一次。</summary>
    public IReadOnlyList<Candidate> Query(string prefix, int limit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (prefix.Length is 0 || prefix.Length > MaxCodeLength || limit <= 0)
        {
            return [];
        }

        return Format.RankByWeight ? QueryByWeight(prefix, limit) : QueryByBand(prefix, limit);
    }

    /// <summary>分带查：先本码长，再逐级放长，靠二分定位起点。</summary>
    private IReadOnlyList<Candidate> QueryByBand(string prefix, int limit)
    {
        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var len = prefix.Length; len <= MaxCodeLength; len++)
        {
            var band = _bands[len];
            for (var i = LowerBound(band, prefix); i < band.Length; i++)
            {
                var candidate = band[i];
                if (!candidate.Code.StartsWith(prefix, StringComparison.Ordinal))
                {
                    break;
                }

                if (seen.Add(candidate.Text))
                {
                    result.Add(candidate);
                    if (result.Count >= limit)
                    {
                        return result;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>按词频查：整张表从高频往低扫，够一页就停。</summary>
    private IReadOnlyList<Candidate> QueryByWeight(string prefix, int limit)
    {
        var result = new List<Candidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var budget = Math.Min(_byWeight.Length, WeightScanBudget);

        for (var i = 0; i < budget && result.Count < limit; i++)
        {
            var candidate = _byWeight[i];
            if (candidate.Code.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(candidate.Text))
            {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static int LowerBound(Candidate[] band, string prefix)
    {
        var lo = 0;
        var hi = band.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (string.CompareOrdinal(band[mid].Code, prefix) < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
