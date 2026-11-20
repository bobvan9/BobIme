namespace BobIme.Shell.Interop;

internal static class TextInserter
{
    /// <summary>用 SendInput 的 Unicode 事件送字，绕开键盘布局，中英文标点都能原样进目标程序。返回系统实际接收的事件数。</summary>
    public static uint SendText(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var inputs = new List<Native.INPUT>(text.Length * 2);
        for (var i = 0; i < text.Length;)
        {
            // 增补字符占两个 UTF-16 单元，要一起按下再一起抬起，目标程序才认得。
            var units = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            for (var k = 0; k < units; k++)
            {
                inputs.Add(Key(text[i + k], down: true));
            }

            for (var k = units - 1; k >= 0; k--)
            {
                inputs.Add(Key(text[i + k], down: false));
            }

            i += units;
        }

        return Native.SendInput((uint)inputs.Count, [.. inputs], Native.INPUT.Size);
    }

    private static Native.INPUT Key(char unit, bool down) => new()
    {
        type = 1,
        U = new Native.INPUTUNION
        {
            ki = new Native.KEYBDINPUT
            {
                wScan = unit,
                dwFlags = Native.KEYEVENTF_UNICODE | (down ? 0 : Native.KEYEVENTF_KEYUP),
            },
        },
    };
}
