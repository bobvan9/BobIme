namespace BobIme.Core;

/// <summary>
/// 与按键捕获、候选窗绘制都无关的组码状态机：喂进字母，吐出缓冲和候选。
/// 可以挂多套码表，候选按码表顺序合并，所以五笔候选永远排在拼音候选前面。
/// </summary>
public sealed class ImeEngine
{
    /// <summary>自动上屏只到三码为止，四码要等空格。</summary>
    private const int AutoCommitCodeLimit = 4;

    /// <summary>一次缓存几页候选。<c>a</c>、<c>zhong</c> 这类前缀能命中上千条，全存下来不值当。</summary>
    private const int PageLimit = 5;

    private readonly CodeTable[] _tables;
    private CodeTable[] _active;
    private int _maxLength;
    private CandidateSource? _activeSource;
    private string _buffer = "";
    private List<Candidate> _matches = [];
    private IReadOnlyList<Candidate> _candidates = [];
    private int _pageIndex;
    private int _pageSize = 9;
    private IReadOnlyList<WordOrder> _wordOrders = [];

    public ImeEngine(CodeTable table) : this([table])
    {
    }

    public ImeEngine(IReadOnlyList<CodeTable> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentOutOfRangeException.ThrowIfZero(tables.Count);
        _tables = [.. tables];
        _active = _tables;
        _maxLength = _tables.Max(t => t.MaxCodeLength);
    }

    /// <summary>
    /// 只挂某一类码表：<see cref="CandidateSource.Wubi"/> 是纯五笔，
    /// <see cref="CandidateSource.Pinyin"/> 是纯拼音，<c>null</c> 是混输。
    /// 换档等于换了一套编码规则，手上的半截编码要丢掉。
    /// </summary>
    public CandidateSource? ActiveSource
    {
        get => _activeSource;
        set
        {
            if (_activeSource == value)
            {
                return;
            }

            _activeSource = value;
            _active = [.. _tables.Where(t => value is null || t.Source == value)];
            _maxLength = _active.Length == 0 ? 0 : _active.Max(t => t.MaxCodeLength);
            Clear();
        }
    }

    /// <summary>
    /// 一次交给候选窗显示的词条数。候选靠 1–9 数字键选，所以上限就是 9；
    /// 改档位时当前页夹回可用范围，免得停在一页不存在的位置上。
    /// </summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            var size = Math.Clamp(value, 1, 9);
            if (size == _pageSize)
            {
                return;
            }

            _pageSize = size;
            ShowPage(_matches.Count == 0 ? 0 : Math.Min(_pageIndex, Pages - 1));
        }
    }

    /// <summary>
    /// 词序规则，整套给每一张码表。设置界面保存后换一次，下一次查候选就走新次序；
    /// 规则按编码生效，所以五笔和拼音里同码的候选都受它影响。
    /// </summary>
    public IReadOnlyList<WordOrder> WordOrders
    {
        get => _wordOrders;
        set
        {
            _wordOrders = value ?? [];
            foreach (var table in _tables)
            {
                table.WordOrders = _wordOrders;
            }
        }
    }

    /// <summary>
    /// 照现在的码表和规则查一遍候选，供设置界面预览次序；
    /// 编码不合形或超出可用码长就是空表，且不动手上的半截编码。
    /// </summary>
    public IReadOnlyList<Candidate> Peek(string? code)
    {
        var prefix = WordOrder.CodeKey(code);
        return prefix.Length == 0 || prefix.Length > _maxLength ? [] : Query(prefix);
    }

    public string Buffer => _buffer;

    public IReadOnlyList<Candidate> Candidates => _candidates;

    /// <summary>当前页码，从 1 开始，直接给候选窗显示。</summary>
    public int Page => _pageIndex + 1;

    /// <summary>缓存里一共有几页。</summary>
    public int Pages => (_matches.Count + PageSize - 1) / PageSize;

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
        var matches = Query(next);
        if (matches.Count == 0)
        {
            return Result(KeyStatus.NoMatch);
        }

        _buffer = next;
        _matches = matches;
        ShowPage(0);

        // 唯一且正好是全码命中才不必等空格，但四码不算：四码一律要按空格确认。
        // 看的是整段命中数而不是当前页条数，否则一页只剩一条就会把抢上屏带回来。
        var autoCommit = _matches.Count == 1 && _matches[0].Code == next
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
        _matches = _buffer.Length == 0 ? [] : Query(_buffer);
        ShowPage(0);
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

    /// <summary>往后翻一页；已经在最后一页就不动。</summary>
    public bool NextPage()
    {
        if (!IsComposing || _pageIndex + 1 >= Pages)
        {
            return false;
        }

        ShowPage(_pageIndex + 1);
        return true;
    }

    /// <summary>往前翻一页；和下一页一样，边界外不动。</summary>
    public bool PrevPage()
    {
        if (!IsComposing || _pageIndex == 0)
        {
            return false;
        }

        ShowPage(_pageIndex - 1);
        return true;
    }

    public void Clear()
    {
        _buffer = "";
        _matches = [];
        ShowPage(0);
    }

    private void ShowPage(int index)
    {
        _pageIndex = index;
        var from = index * PageSize;
        _candidates = from >= _matches.Count
            ? []
            : _matches.GetRange(from, Math.Min(PageSize, _matches.Count - from));
    }

    /// <summary>
    /// 按码表顺序取候选，同一词条只留第一次出现。名额是按页分的：每套码表每页先保底分到
    /// 半页，没用完的顺延给后面的表，翻页时各表接着上次的次序往下取。所以缓存五页也不会让
    /// 第一页被五笔的上千条同前缀词条占满、把拼音挤没。
    /// </summary>
    private List<Candidate> Query(string prefix)
    {
        if (_active.Length == 0)
        {
            return [];
        }

        var budget = PageSize * PageLimit;
        var streams = new IReadOnlyList<Candidate>[_active.Length];
        for (var t = 0; t < _active.Length; t++)
        {
            streams[t] = _active[t].Query(prefix, budget);
        }

        var quota = Math.Max(1, (PageSize + _active.Length - 1) / _active.Length);
        var cursor = new int[_active.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Candidate>(budget);

        while (result.Count < budget)
        {
            var before = result.Count;
            for (var t = 0; t < _active.Length && result.Count - before < PageSize; t++)
            {
                // 最后一套码表可以把这一页剩下的名额都拿走。
                var room = t == _active.Length - 1
                    ? PageSize - (result.Count - before)
                    : Math.Min(quota, PageSize - (result.Count - before));
                var taken = 0;
                while (taken < room && cursor[t] < streams[t].Count)
                {
                    var candidate = streams[t][cursor[t]++];
                    if (seen.Add(candidate.Text))
                    {
                        result.Add(candidate);
                        taken++;
                    }
                }
            }

            // 各套码表都取空了，就没有下一页。
            if (result.Count == before)
            {
                break;
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
