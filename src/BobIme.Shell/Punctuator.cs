namespace BobIme.Shell;

/// <summary>
/// 中文标点档的按键对照。引号靠内部记的开合状态一开一合地轮，方括号被翻页占了，所以不进这张表。
/// </summary>
internal sealed class Punctuator
{
    private static readonly Dictionary<uint, string> Plain = new()
    {
        [VirtualKey.OemComma] = "，",
        [VirtualKey.OemPeriod] = "。",
        [VirtualKey.OemSemicolon] = "；",
        [VirtualKey.OemTilde] = "·",
        [VirtualKey.OemBackslash] = "、",
    };

    private static readonly Dictionary<uint, string> Shifted = new()
    {
        [VirtualKey.OemComma] = "《",
        [VirtualKey.OemPeriod] = "》",
        [VirtualKey.OemQuestion] = "？",
        [VirtualKey.OemSemicolon] = "：",
        [VirtualKey.OemTilde] = "～",
        [VirtualKey.OemBackslash] = "｜",
        [VirtualKey.OemMinus] = "——",
        [VirtualKey.D1] = "！",
        [VirtualKey.D6] = "……",
        [VirtualKey.D8] = "×",
        [VirtualKey.D9] = "（",
        [VirtualKey.D0] = "）",
    };

    private bool _quoteOpen = true;

    /// <summary>命中就返回全角文本；引号顺手把开合翻过去，所以这一下是有状态的。</summary>
    public bool TryTranslate(uint vk, bool shift, out string text)
    {
        if (vk == VirtualKey.OemQuotes)
        {
            var (open, close) = shift ? ("“", "”") : ("‘", "’");
            text = _quoteOpen ? open : close;
            _quoteOpen = !_quoteOpen;
            return true;
        }

        return (shift ? Shifted : Plain).TryGetValue(vk, out text!);
    }
}
