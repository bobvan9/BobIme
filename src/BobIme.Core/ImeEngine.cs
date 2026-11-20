namespace BobIme.Core;

/// <summary>
/// 与按键捕获、候选窗绘制都无关的组码状态机：喂进字母，吐出缓冲和候选。
/// 可以挂多套码表，候选按码表顺序合并，所以五笔候选永远排在拼音候选前面。
/// </summary>
public sealed class ImeEngine
{
    /// <summary>自动上屏只到三码为止，四码要等空格。</summary>
    private const int AutoCommitCodeLimit = 4;

    private readonly CodeTable[] _tables;
    private readonly int _maxLength;
    private string _buffer = "";
    private IReadOnlyList<Candidate> _candidates = [];

    public ImeEngine(CodeTable table) : this([table])
    {
    }

    public ImeEngine(IReadOnlyList<CodeTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentOutOfRangeException.ThrowIfZero(tables.Count);
        _tables = [.. tables];
        _maxLength = _tables.Max(t => t.MaxCodeLength);
    }

    /// <summary>一次交给候选窗显示的词条数。</summary>
    public int PageSize { get; init; } = 9;

    public string Buffer => _buffer;

    public IReadOnlyList<Candidate> Candidates => _candidates;

    public bool IsComposing => _buffer.Length > 0;

    public bool IsBufferFull => _buffer.Length == _maxLength;

    public KeyResult Press(char key)
    {
        var letter = char.ToLowerInvariant(key);
        if (letter is < 'a' or > 'z')
        {
            return Result(KeyStatus.Ignored);
        }

        // 码长已满还继续敲，就当作重新起组，不再往上追加。
        var next = IsBufferFull ? letter.ToString() : _buffer + letter;
        var candidates = Query(next);
        if (candidates.Count == 0)
        {
            return Result(KeyStatus.NoMatch);
        }

        _buffer = next;
        _candidates = candidates;

        // 唯一且正好是全码命中才不必等空格，但四码不算：四码一律要按空格确认。
        var autoCommit = candidates.Count == 1 && candidates[0].Code == next
            && next.Length < AutoCommitCodeLimit;
        return Result(KeyStatus.Updated, autoCommit: autoCommit);
    }

    public KeyResult PressBackspace()
    {
        if (_buffer.Length == 0)
        {
            return Result(KeyStatus.Ignored);
        }

        _buffer = _buffer[..^1];
        _candidates = _buffer.Length == 0 ? [] : Query(_buffer);
        return Result(KeyStatus.Updated);
    }

    public KeyResult SelectAt(int index)
    {
        if (!IsComposing || (uint)index >= (uint)_candidates.Count)
        {
            return Result(KeyStatus.Ignored);
        }

        return Commit(_candidates[index]);
    }

    public KeyResult SelectFirst() => SelectAt(0);

    public KeyResult PressEscape()
    {
        Clear();
        return Result(KeyStatus.Updated);
    }

    public void Clear()
    {
        _buffer = "";
        _candidates = [];
    }

    /// <summary>
    /// 按码表顺序取候选，同一词条只留第一次出现。每套码表先保底分到半页名额，
    /// 免得五笔的上千条同前缀词条把拼音候选全挤掉；没用完的名额顺延给后面的表。
    /// </summary>
    private List<Candidate> Query(string prefix)
    {
        var result = new List<Candidate>(PageSize);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var quota = Math.Max(1, (PageSize + _tables.Length - 1) / _tables.Length);

        for (var t = 0; t < _tables.Length; t++)
        {
            // 最后一套码表可以把剩下的整页名额都拿走。
            var limit = t == _tables.Length - 1 ? PageSize - result.Count : Math.Min(quota, PageSize - result.Count);
            foreach (var candidate in _tables[t].Query(prefix, limit))
            {
                if (seen.Add(candidate.Text))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private KeyResult Commit(Candidate candidate)
    {
        Clear();
        return new KeyResult(KeyStatus.Committed, candidate.Text, "", [], false);
    }

    private KeyResult Result(KeyStatus status, bool autoCommit = false) =>
        new(status, "", _buffer, _candidates, autoCommit);
}
