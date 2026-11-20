using BobIme.Core;
using BobIme.Shell.Interop;

namespace BobIme.Shell;

public static class VirtualKey
{
    public const uint Back = 0x08;
    public const uint Enter = 0x0D;
    public const uint Escape = 0x1B;
    public const uint Space = 0x20;
    public const uint A = 0x41;
    public const uint Z = 0x5A;
    public const uint D0 = 0x30;
    public const uint D9 = 0x39;
    public const uint NumPad0 = 0x60;
    public const uint NumPad9 = 0x69;
    public const uint OemOpenBrackets = 0xDB;
    public const uint OemCloseBrackets = 0xDD;
}

/// <summary>一次按键的处置结果。<see cref="Composing"/> 决定候选窗此刻该不该显示。</summary>
public sealed record KeyDecision(bool Consume, string? CommitText, bool Composing)
{
    public static readonly KeyDecision Passthrough = new(false, null, false);
}

/// <summary>
/// 把虚拟键码翻译成组码动作，不碰窗口也不碰 SendInput，所以能脱离消息循环单测。
/// </summary>
public sealed class ImeController
{
    private readonly ImeEngine _engine;

    public ImeController(ImeEngine engine) => _engine = engine;

    public bool Enabled { get; set; } = true;

    /// <summary>中文模式；左 Shift 单敲切换，英文模式下按键一律原样放行。</summary>
    public bool ChineseMode { get; private set; } = true;

    /// <summary>挂哪一类码表：五笔、拼音，<c>null</c> 是混输。换档会丢掉手上的半截编码。</summary>
    public CandidateSource? ActiveSource
    {
        get => _engine.ActiveSource;
        set => _engine.ActiveSource = value;
    }

    /// <summary>切中/英。输入法整个停用着的时候这个键不归我们管，所以不切。</summary>
    public KeyDecision ToggleChineseMode()
    {
        if (!Enabled)
        {
            return KeyDecision.Passthrough;
        }

        ChineseMode = !ChineseMode;
        // 切模式时手上的半截编码要丢掉，不然切回来它还在，上屏时机就乱了。
        _engine.Clear();
        return new KeyDecision(false, null, false);
    }

    public bool IsComposing => _engine.IsComposing;

    public string Buffer => _engine.Buffer;

    public IReadOnlyList<Candidate> Candidates => _engine.Candidates;

    /// <summary>当前页码，从 1 开始，给候选窗标页码用。</summary>
    public int Page => _engine.Page;

    public int Pages => _engine.Pages;

    /// <summary>候选窗上的箭头走的就是 <c>[</c> / <c>]</c> 那条路，只是不经过按键。</summary>
    public bool TurnPage(bool next) => IsComposing && (next ? _engine.NextPage() : _engine.PrevPage());

    /// <summary>丢掉当前编码，不产出任何文本。</summary>
    public void Reset() => _engine.Clear();

    public KeyDecision OnKeyDown(KeyEvent key)
    {
        // 开关判断要抢在 Enabled 检查前面，否则关掉之后就再也接不到这个键。
        if (key.Ctrl && key.Vk == VirtualKey.Space)
        {
            Enabled = !Enabled;
            _engine.Clear();
            return new KeyDecision(true, null, false);
        }

        if (!Enabled)
        {
            return KeyDecision.Passthrough;
        }

        // 英文模式：一个键都不吃，全交回目标程序。
        if (!ChineseMode)
        {
            return KeyDecision.Passthrough;
        }

        // 组合键既不参与组码，也不该顺手把候选上屏：编码留着，原样放行。
        if (key.Ctrl || key.Alt || key.Shift)
        {
            return new KeyDecision(false, null, IsComposing);
        }

        return key.Vk switch
        {
            // Caps Lock 开着敲出来的也是大写英文，同样不当编码用。
            >= VirtualKey.A and <= VirtualKey.Z when key.CapsLock => KeyDecision.Passthrough,
            >= VirtualKey.A and <= VirtualKey.Z => OnLetter((char)('a' + key.Vk - VirtualKey.A)),
            VirtualKey.Space or VirtualKey.Enter => OnPick(0),
            >= VirtualKey.D0 and <= VirtualKey.D9 => OnDigit((int)(key.Vk - VirtualKey.D0)),
            >= VirtualKey.NumPad0 and <= VirtualKey.NumPad9 => OnDigit((int)(key.Vk - VirtualKey.NumPad0)),
            VirtualKey.Back => OnBackspace(),
            VirtualKey.Escape => OnEscape(),
            VirtualKey.OemOpenBrackets => OnPage(false),
            VirtualKey.OemCloseBrackets => OnPage(true),
            _ => OnOtherKey(),
        };
    }

    private KeyDecision OnLetter(char letter)
    {
        var result = _engine.Press(letter);
        if (result.Status == KeyStatus.Updated)
        {
            return result.AutoCommit ? Commit(0) : new KeyDecision(true, null, true);
        }

        // 空闲时 z、v 这些非编码键要原样交给目标程序。
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        // 组码途中敲到无效码：吞掉，已有的编码不能被打散；不出声。
        return new KeyDecision(true, null, true);
    }

    private KeyDecision OnPick(int index) => IsComposing ? Commit(index) : KeyDecision.Passthrough;

    private KeyDecision OnDigit(int digit)
    {
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        if (digit >= 1 && digit <= Candidates.Count)
        {
            return Commit(digit - 1);
        }

        // 超出候选数的数字键要吞掉，不然会漏一串数字进目标程序。
        return new KeyDecision(true, null, true);
    }

    private KeyDecision OnBackspace()
    {
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        _engine.PressBackspace();
        return new KeyDecision(true, null, IsComposing);
    }

    private KeyDecision OnEscape()
    {
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        _engine.Clear();
        return new KeyDecision(true, null, false);
    }

    private KeyDecision OnPage(bool next)
    {
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        // 翻不动（已经在第一页或最后一页）也照样吞掉：放行的话会走「其他按键」分支
        // 把首选词顺手顶上屏，还漏一个方括号进目标程序。
        _ = TurnPage(next);
        return new KeyDecision(true, null, true);
    }

    private KeyDecision OnOtherKey()
    {
        if (!IsComposing)
        {
            return KeyDecision.Passthrough;
        }

        // 标点、方向键之类：先把首候选上屏，再把这个键放行。
        var committed = Commit(0);
        return committed with { Consume = false };
    }

    private KeyDecision Commit(int index)
    {
        var text = Candidates[index].Text;
        _engine.SelectAt(index);
        return new KeyDecision(true, text, false);
    }
}
