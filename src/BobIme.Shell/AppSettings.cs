using System.Text.Json;
using System.Text.Json.Serialization;

namespace BobIme.Shell;

/// <summary>翻页键方案：编码途中用来翻页的那对键，四选一。</summary>
public enum PageKeySet
{
    Brackets,
    MinusEqual,
    PageUpDown,
    LeftRight,
}

/// <summary>符号面板出现的位置。</summary>
public enum PanelPlacement
{
    Center,
    Caret,
}

/// <summary>候选窗摆在哪儿：贴着正在打字的光标、跟着鼠标，或者钉在桌面右下角。</summary>
public enum CandidatePlacement
{
    Caret,
    Mouse,
    BottomRight,
}

public static class CandidatePlacementInfo
{
    public static string Display(this CandidatePlacement placement) => placement switch
    {
        CandidatePlacement.Mouse => "跟随鼠标",
        CandidatePlacement.BottomRight => "桌面右下角",
        _ => "跟随输入光标",
    };
}

public static class PageKeySetInfo
{
    private static readonly (uint Prev, uint Next)[] Pairs =
    [
        (0xDB, 0xDD), // [ ]
        (0xBD, 0xBB), // - =
        (0x21, 0x22), // PageUp PageDown
        (0x25, 0x27), // ← →
    ];

    public static uint Prev(this PageKeySet set) => Pairs[(int)set].Prev;

    public static uint Next(this PageKeySet set) => Pairs[(int)set].Next;

    public static string Display(this PageKeySet set) => set switch
    {
        PageKeySet.MinusEqual => "- 和 =",
        PageKeySet.PageUpDown => "PageUp 和 PageDown",
        PageKeySet.LeftRight => "← 和 →",
        _ => "[ 和 ]",
    };
}

/// <summary>
/// 一个可绑定的按键。两种形态：带修饰键的组合（<c>Ctrl+.</c>）和单敲一个修饰键
/// （按住期间没碰过别的键才算，用来承接原来「左 Shift 单敲切中英」）。
/// </summary>
public readonly record struct KeyBinding
{
    public static readonly KeyBinding None = default;

    /// <summary>0 表示不绑定；<see cref="Tap"/> 为真时这里是修饰键的虚拟键码。</summary>
    public uint Vk { get; init; }

    public bool Ctrl { get; init; }

    public bool Alt { get; init; }

    public bool Shift { get; init; }

    /// <summary>单敲形态：不等组合键，等那个修饰键自己抬起。</summary>
    public bool Tap { get; init; }

    public bool IsNone => Vk == 0;

    public bool IsTap => Tap && !IsNone;

    public bool IsCombo => !Tap && !IsNone;

    public static KeyBinding Combo(uint vk, bool ctrl = false, bool alt = false, bool shift = false) =>
        new() { Vk = vk, Ctrl = ctrl, Alt = alt, Shift = shift };

    public static KeyBinding TapOf(uint modifierVk) => new() { Vk = modifierVk, Tap = true };

    /// <summary>录入框里单按修饰键要走「抬起才算」那条路，不在组合键里判。</summary>
    public static bool IsModifierKey(uint vk) => vk is >= 0xA0 and <= 0xA5;

    /// <summary>
    /// 把 WinForms 的一次按键翻译成组合键；不成形的（裸键、单修饰键）返回 <c>null</c>，
    /// 由录入框决定是提示还是要不要记成单敲。
    /// </summary>
    public static KeyBinding? FromKeyData(Keys keyData)
    {
        var vk = (uint)(keyData & Keys.KeyCode);
        if (IsModifierKey(vk))
        {
            return null;
        }

        var ctrl = keyData.HasFlag(Keys.Control);
        var alt = keyData.HasFlag(Keys.Alt);
        var shift = keyData.HasFlag(Keys.Shift);
        return ctrl || alt || shift ? Combo(vk, ctrl, alt, shift) : null;
    }

    /// <summary>组合键形态才在这里判；单敲形态由钩子在抬起时判，压根没有 <see cref="KeyEvent"/>。</summary>
    public bool Matches(uint vk, bool ctrl, bool alt, bool shift) =>
        IsCombo && vk == Vk && ctrl == Ctrl && alt == Alt && shift == Shift;

    /// <summary>写进 JSON 的短记号，例如 <c>ctrl+.</c>、<c>tap:lshift</c>；不绑定写成空串。</summary>
    public string Token => IsNone ? "" : IsTap ? $"tap:{KeyToken(Vk)}"
        : $"{(Ctrl ? "ctrl+" : "")}{(Alt ? "alt+" : "")}{(Shift ? "shift+" : "")}{KeyToken(Vk)}";

    /// <summary>界面上给人看的样子。</summary>
    public string Display
    {
        get
        {
            if (IsNone)
            {
                return "未绑定";
            }

            if (IsTap)
            {
                return $"{KeyDisplay(Vk)} 单敲";
            }

            var mods = new List<string>(3);
            if (Ctrl) mods.Add("Ctrl");
            if (Alt) mods.Add("Alt");
            if (Shift) mods.Add("Shift");
            return string.Join(" + ", [.. mods, KeyDisplay(Vk)]);
        }
    }

    public static bool TryParse(string? text, out KeyBinding binding)
    {
        binding = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var value = text.Trim();
        if (value.StartsWith("tap:", StringComparison.OrdinalIgnoreCase))
        {
            var modifier = KeyFromToken(value[4..]);
            if (modifier is null || !IsModifierKey(modifier.Value))
            {
                return false;
            }

            binding = TapOf(modifier.Value);
            return true;
        }

        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        var vk = KeyFromToken(parts[^1]);
        if (vk is null || IsModifierKey(vk.Value))
        {
            return false;
        }

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl": binding = binding with { Ctrl = true }; break;
                case "alt": binding = binding with { Alt = true }; break;
                case "shift": binding = binding with { Shift = true }; break;
                default: return false;
            }
        }

        binding = binding with { Vk = vk.Value };
        return true;
    }

    private static string KeyToken(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)('a' + vk - 0x41)).ToString(),
        >= 0x30 and <= 0x39 => ((char)('0' + vk - 0x30)).ToString(),
        >= 0x60 and <= 0x69 => $"num{vk - 0x60}",
        >= 0x70 and <= 0x7B => $"f{vk - 0x6F}",
        _ => KeyTable.Token(vk),
    };

    private static string KeyDisplay(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => ((char)('A' + vk - 0x41)).ToString(),
        >= 0x30 and <= 0x39 => ((char)('0' + vk - 0x30)).ToString(),
        >= 0x60 and <= 0x69 => $"小键盘{vk - 0x60}",
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        0xA0 => "左 Shift",
        0xA1 => "右 Shift",
        0xA2 => "左 Ctrl",
        0xA3 => "右 Ctrl",
        0xA4 => "左 Alt",
        0xA5 => "右 Alt",
        _ => KeyTable.Display(vk),
    };

    private static uint? KeyFromToken(string token)
    {
        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return 0x41 + (uint)(c - 'A');
            }

            if (c is >= '0' and <= '9')
            {
                return 0x30 + (uint)(c - '0');
            }
        }

        if (token.Length is 2 or 3 && (token[0] is 'f' or 'F')
            && int.TryParse(token[1..], out var function) && function is >= 1 and <= 12)
        {
            return 0x70 + (uint)(function - 1);
        }

        if (token.Length == 4 && token.StartsWith("num", StringComparison.OrdinalIgnoreCase)
            && token[3] is >= '0' and <= '9')
        {
            return 0x60 + (uint)(token[3] - '0');
        }

        return KeyTable.FromToken(token.ToLowerInvariant());
    }
}

/// <summary>虚拟键码与短记号、显示名的对照，只收字母数字和 F 键之外的那些。</summary>
internal static class KeyTable
{
    private static readonly (uint Vk, string Token, string Display)[] Names =
    [
        (0x08, "backspace", "退格"),
        (0x09, "tab", "Tab"),
        (0x0D, "enter", "回车"),
        (0x10, "shift", "Shift"),
        (0x11, "ctrl", "Ctrl"),
        (0x12, "alt", "Alt"),
        (0x14, "caps", "大写锁定"),
        (0x1B, "esc", "Esc"),
        (0x20, "space", "空格"),
        (0x21, "pgup", "PageUp"),
        (0x22, "pgdn", "PageDown"),
        (0x23, "end", "End"),
        (0x24, "home", "Home"),
        (0x25, "left", "←"),
        (0x26, "up", "↑"),
        (0x27, "right", "→"),
        (0x28, "down", "↓"),
        (0x2D, "insert", "Insert"),
        (0x2E, "delete", "Delete"),
        (0x6F, "divide", "小键盘/"),
        (0x90, "numlock", "数字锁定"),
        (0x91, "scrolllock", "滚动锁定"),
        (0xBA, ";", ";"),
        (0xBB, "=", "="),
        (0xBC, ",", ","),
        (0xBD, "-", "-"),
        (0xBE, ".", "."),
        (0xBF, "/", "/"),
        (0xC0, "`", "`"),
        (0xDB, "[", "["),
        (0xDC, "\\", "\\"),
        (0xDD, "]", "]"),
        (0xDE, "'", "'"),
        (0xA0, "lshift", "左 Shift"),
        (0xA1, "rshift", "右 Shift"),
        (0xA2, "lctrl", "左 Ctrl"),
        (0xA3, "rctrl", "右 Ctrl"),
        (0xA4, "lalt", "左 Alt"),
        (0xA5, "ralt", "右 Alt"),
    ];

    public static string Token(uint vk)
    {
        var hit = Names.FirstOrDefault(n => n.Vk == vk);
        return hit.Vk == 0 ? $"vk{vk:X2}" : hit.Token;
    }

    public static string Display(uint vk)
    {
        var hit = Names.FirstOrDefault(n => n.Vk == vk);
        return hit.Vk == 0 ? $"0x{vk:X2}" : hit.Display;
    }

    public static uint? FromToken(string token)
    {
        foreach (var (vk, name, _) in Names)
        {
            if (name == token)
            {
                return vk;
            }
        }

        if (token.StartsWith("vk", StringComparison.Ordinal) && token.Length == 4
            && uint.TryParse(token[2..], System.Globalization.NumberStyles.HexNumber, null, out var raw))
        {
            return raw;
        }

        return null;
    }
}

/// <summary>
/// 托盘「设置…」读写的那份配置，落在 <c>%APPDATA%\BobIme\settings.json</c>。
/// 读的时候一律容错：文件没了、坏了、值离谱，都退回默认，不让常驻进程起不来。
/// </summary>
public sealed class AppSettings
{
    private const int MinPageSize = 3;
    private const int MaxPageSize = 9;

    /// <summary>下次启动时用的输入档；改完不当场生效，档位本身随时能在托盘菜单切。</summary>
    public InputMode StartupMode { get; set; } = InputMode.Mixed;

    /// <summary>下次启动时标点档开在哪一头。</summary>
    public bool ChinesePunctuation { get; set; } = true;

    /// <summary>切中文/英文模式，默认沿用左 Shift 单敲。</summary>
    public KeyBinding EnglishToggle { get; set; } = KeyBinding.TapOf(0xA0);

    /// <summary>切中文/英文标点，默认 Ctrl+.（和微软拼音一致）。</summary>
    public KeyBinding PunctuationToggle { get; set; } = KeyBinding.Combo(0xBE, ctrl: true);

    /// <summary>启用/停用整个输入法，默认 Ctrl+Space。</summary>
    public KeyBinding EnabledToggle { get; set; } = KeyBinding.Combo(0x20, ctrl: true);

    /// <summary>候选窗每页几条，1–9 的数字键按这个数选词。</summary>
    public int PageSize { get; set; } = 9;

    public PageKeySet PageKeys { get; set; } = PageKeySet.Brackets;

    /// <summary>候选窗的位置。默认贴光标，取不到光标才退到鼠标。</summary>
    public CandidatePlacement CandidatePlacement { get; set; } = CandidatePlacement.Caret;

    public PanelPlacement SymbolPanelPlacement { get; set; } = PanelPlacement.Center;

    /// <summary>托盘图标双击开符号面板，默认关着，免得误碰。</summary>
    public bool TrayDoubleClickOpensSymbols { get; set; }

    /// <summary>
    /// 「按程序定档」规则表：程序获得焦点时先切到哪一档，空表等于没这个功能。
    /// 中文档只把中文模式打开，码表档仍旧用当前那一档。
    /// 存成 <c>"wow.exe:english"</c> 这种短记号，比对范围和条数上限见 <see cref="WindowRule.Normalize"/>。
    /// </summary>
    public List<WindowRule> WindowRules { get; set; } = [];

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BobIme", "settings.json");

    /// <summary>设置界面改的是副本，取消就丢掉，保存才拿这份覆盖回来。</summary>
    public AppSettings Clone() => new()
    {
        StartupMode = StartupMode,
        ChinesePunctuation = ChinesePunctuation,
        EnglishToggle = EnglishToggle,
        PunctuationToggle = PunctuationToggle,
        EnabledToggle = EnabledToggle,
        PageSize = PageSize,
        PageKeys = PageKeys,
        CandidatePlacement = CandidatePlacement,
        SymbolPanelPlacement = SymbolPanelPlacement,
        TrayDoubleClickOpensSymbols = TrayDoubleClickOpensSymbols,
        // 名单要是共享同一个列表，设置界面里删一条就会当场改掉正在生效的那份。
        WindowRules = [.. WindowRules],
    };

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? new AppSettings();
            loaded.Normalized();
            return loaded;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        Normalized();
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>手改过的 JSON 里越界值夹回可用范围。</summary>
    public AppSettings Normalized()
    {
        PageSize = Math.Clamp(PageSize, MinPageSize, MaxPageSize);
        if (!Enum.IsDefined(PageKeys))
        {
            PageKeys = PageKeySet.Brackets;
        }

        if (!Enum.IsDefined(CandidatePlacement))
        {
            CandidatePlacement = CandidatePlacement.Caret;
        }

        if (!Enum.IsDefined(SymbolPanelPlacement))
        {
            SymbolPanelPlacement = PanelPlacement.Center;
        }

        WindowRules = [.. WindowRule.Normalize(WindowRules)];
        return this;
    }

    /// <summary>三处快捷键里有没有撞车，撞了返回那两名的中文名，给设置界面提示。</summary>
    public (string First, string Second)? FindToggleConflict()
    {
        var all = new (string Name, KeyBinding Binding)[]
        {
            ("切换中英输入", EnglishToggle),
            ("切换中英标点", PunctuationToggle),
            ("启用/停用输入法", EnabledToggle),
        };

        for (var i = 0; i < all.Length; i++)
        {
            for (var j = i + 1; j < all.Length; j++)
            {
                if (!all[i].Binding.IsNone && all[i].Binding == all[j].Binding)
                {
                    return (all[i].Name, all[j].Name);
                }
            }
        }

        return null;
    }

    private static JsonSerializerOptions JsonOptions => new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new KeyBindingJsonConverter(), new WindowRuleJsonConverter() },
    };

    /// <summary>按键存成 <c>"ctrl+."</c> 这种短记号，手改文件时不用去查虚拟键码。</summary>
    private sealed class KeyBindingJsonConverter : JsonConverter<KeyBinding>
    {
        public override KeyBinding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            KeyBinding.TryParse(text, out var binding);
            return binding;
        }

        public override void Write(Utf8JsonWriter writer, KeyBinding value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Token);
    }

    /// <summary>规则存成 <c>"wow.exe:english"</c> 一行一条，档位漏写按英文档。</summary>
    private sealed class WindowRuleJsonConverter : JsonConverter<WindowRule>
    {
        public override WindowRule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? WindowRule.Parse(reader.GetString()) ?? Empty : Empty;

        public override void Write(Utf8JsonWriter writer, WindowRule value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Token);

        /// <summary>读不成形时给一条空名的，<see cref="WindowRule.Normalize"/> 会把空项丢掉。</summary>
        private static readonly WindowRule Empty = new("", WindowMode.English);
    }
}
