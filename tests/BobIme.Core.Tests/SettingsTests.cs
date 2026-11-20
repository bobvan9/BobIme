using BobIme.Core;
using BobIme.Shell;
using BobIme.Shell.Interop;

namespace BobIme.Core.Tests;

/// <summary>设置模型、按键记号和按设置分发的按键判定。</summary>
public class SettingsTests
{
    private const uint OemMinus = 0xBD;
    private const uint OemEqual = 0xBB;
    private const uint OemOpen = 0xDB;
    private const uint OemPeriod = 0xBE;
    private const uint Backtick = 0xC0;
    private const uint F12 = 0x7B;

    [Theory]
    [InlineData(0xBE, true, false, false, "ctrl+.", "Ctrl + .")]
    [InlineData(0x20, true, false, false, "ctrl+space", "Ctrl + 空格")]
    [InlineData(0x41, true, false, true, "ctrl+shift+a", "Ctrl + Shift + A")]
    [InlineData(0x2E, true, true, false, "ctrl+alt+delete", "Ctrl + Alt + Delete")]
    [InlineData(0x70, false, true, false, "alt+f1", "Alt + F1")]
    [InlineData(0x61, true, false, false, "ctrl+num1", "Ctrl + 小键盘1")]
    public void Combo_WritesAndReadsItsToken(uint vk, bool ctrl, bool alt, bool shift, string token, string display)
    {
        var binding = KeyBinding.Combo(vk, ctrl, alt, shift);

        Assert.Equal(token, binding.Token);
        Assert.Equal(display, binding.Display);
        Assert.True(KeyBinding.TryParse(token, out var parsed));
        Assert.Equal(binding, parsed);
    }

    [Fact]
    public void TapBinding_KeepsOnlyTheModifier()
    {
        var binding = KeyBinding.TapOf(0xA0);

        Assert.True(binding.IsTap);
        Assert.False(binding.IsCombo);
        Assert.Equal("tap:lshift", binding.Token);
        Assert.Equal("左 Shift 单敲", binding.Display);
        Assert.Equal(binding, Parse("tap:lshift"));

        // 单敲形态没有组合键那一路，按键事件一律不匹配。
        Assert.False(binding.Matches(0xA0, false, false, false));
    }

    [Fact]
    public void None_IsStoredAsEmptyText()
    {
        Assert.Equal("", KeyBinding.None.Token);
        Assert.Equal("未绑定", KeyBinding.None.Display);
        Assert.True(KeyBinding.TryParse("", out var parsed));
        Assert.True(parsed.IsNone);
    }

    [Theory]
    [InlineData("ctrl")]
    [InlineData("f1")]
    [InlineData("ctrl+bogus")]
    [InlineData("ctrl+meta+p")]
    [InlineData("tap:space")]
    public void UnusableToken_IsRejected(string token) => Assert.False(KeyBinding.TryParse(token, out _));

    [Fact]
    public void Settings_SurviveAFileRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bobime-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings
            {
                StartupMode = InputMode.Pinyin,
                ChinesePunctuation = false,
                EnglishToggle = KeyBinding.Combo(Backtick, ctrl: true),
                PunctuationToggle = KeyBinding.None,
                EnabledToggle = KeyBinding.Combo(F12, ctrl: true, alt: true),
                PageSize = 5,
                PageKeys = PageKeySet.LeftRight,
                SymbolPanelPlacement = PanelPlacement.Caret,
                TrayDoubleClickOpensSymbols = true,
            };

            settings.Save(path);
            var loaded = AppSettings.Load(path);

            Assert.Equal(InputMode.Pinyin, loaded.StartupMode);
            Assert.False(loaded.ChinesePunctuation);
            Assert.Equal(settings.EnglishToggle, loaded.EnglishToggle);
            Assert.True(loaded.PunctuationToggle.IsNone);
            Assert.Equal(settings.EnabledToggle, loaded.EnabledToggle);
            Assert.Equal(5, loaded.PageSize);
            Assert.Equal(PageKeySet.LeftRight, loaded.PageKeys);
            Assert.Equal(PanelPlacement.Caret, loaded.SymbolPanelPlacement);
            Assert.True(loaded.TrayDoubleClickOpensSymbols);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingOrBrokenFile_FallsBackToDefaults()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"bobime-none-{Guid.NewGuid():N}.json");
        Assert.Equal(new AppSettings().EnglishToggle, AppSettings.Load(missing).EnglishToggle);

        var broken = Path.Combine(Path.GetTempPath(), $"bobime-broken-{Guid.NewGuid():N}.json");
        File.WriteAllText(broken, "{ 这不是 json");
        try
        {
            Assert.Equal(new AppSettings().PageSize, AppSettings.Load(broken).PageSize);
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public void HandEditedValues_OutOfRangeAreClamped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bobime-clamp-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{"pageSize": 40, "pageKeys": 99}""");
            var loaded = AppSettings.Load(path);

            Assert.Equal(9, loaded.PageSize);
            Assert.Equal(PageKeySet.Brackets, loaded.PageKeys);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Conflict_IsReportedForTheTwoNamesSharingAKey()
    {
        var conflict = Conflicting().FindToggleConflict();

        Assert.NotNull(conflict);
        Assert.Equal(("切换中英输入", "切换中英标点"), conflict!.Value);
    }

    [Fact]
    public void UnboundToggles_NeverConflict() =>
        Assert.Null(new AppSettings
        {
            EnglishToggle = KeyBinding.None,
            PunctuationToggle = KeyBinding.None,
            EnabledToggle = KeyBinding.None,
        }.FindToggleConflict());

    [Fact]
    public void DefaultPunctuationToggle_SwitchesPunctuationAndIsConsumed()
    {
        var controller = NewController();

        var decision = controller.OnKeyDown(Key(OemPeriod, ctrl: true));

        Assert.True(decision.Consume);
        Assert.False(controller.ChinesePunctuation);

        controller.OnKeyDown(Key(OemPeriod, ctrl: true));
        Assert.True(controller.ChinesePunctuation);
    }

    [Fact]
    public void PunctuationToggle_LeavesEnglishModeAlone()
    {
        var controller = NewController();
        controller.SetChineseMode(false);

        Assert.False(controller.OnKeyDown(Key(OemPeriod, ctrl: true)).Consume);
        Assert.True(controller.ChinesePunctuation);
    }

    [Fact]
    public void PunctuationToggle_KeepsTheHalfTypedCode()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        controller.OnKeyDown(Key(OemPeriod, ctrl: true));

        // 换标点档不该把手上那半截编码弄丢。
        Assert.Equal("k", controller.Buffer);
    }

    [Fact]
    public void ComboEnglishToggle_SwitchesBothWays()
    {
        var controller = NewController(new AppSettings { EnglishToggle = KeyBinding.Combo(Backtick, ctrl: true) });

        Assert.True(controller.OnKeyDown(Key(Backtick, ctrl: true)).Consume);
        Assert.False(controller.ChineseMode);
        Assert.False(controller.OnKeyDown(Letter('k')).Consume);

        controller.OnKeyDown(Key(Backtick, ctrl: true));
        Assert.True(controller.ChineseMode);
    }

    [Fact]
    public void CustomEnabledToggle_ReplacesCtrlSpace()
    {
        var controller = NewController(new AppSettings
        {
            EnabledToggle = KeyBinding.Combo(F12, ctrl: true, alt: true),
        });

        Assert.False(controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true)).Consume);
        Assert.True(controller.Enabled);

        Assert.True(controller.OnKeyDown(Key(F12, ctrl: true, alt: true)).Consume);
        Assert.False(controller.Enabled);
    }

    [Fact]
    public void PageKeys_CanMoveToMinusEqual()
    {
        var controller = NewController(new AppSettings { PageKeys = PageKeySet.MinusEqual });
        controller.OnKeyDown(Letter('k'));

        // - 是上一页、= 是下一页，和 [ ] 的分工一样。
        Assert.True(controller.OnKeyDown(Key(OemEqual)).Consume);
        Assert.Equal(2, controller.Page);
        Assert.True(controller.OnKeyDown(Key(OemMinus)).Consume);
        Assert.Equal(1, controller.Page);

        // 换到 - = 之后，方括号就还回目标程序：顺手上屏首候选再放行。
        var bracket = controller.OnKeyDown(Key(OemOpen));
        Assert.False(bracket.Consume);
        Assert.NotNull(bracket.CommitText);
    }

    [Fact]
    public void PageSize_DrivesHowManyCandidatesOnePageHolds()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        Assert.Equal(9, controller.Candidates.Count);

        controller.PageSize = 3;

        // 改档位当场重切当前页，不用等下一次按键。
        Assert.Equal(3, controller.Candidates.Count);
        Assert.True(controller.Pages > 3);
    }

    [Fact]
    public void PageSize_GrowingKeepsThePageInsideTheList()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));
        for (var i = 1; i < controller.Pages; i++)
        {
            controller.TurnPage(true);
        }

        Assert.Equal(controller.Pages, controller.Page);

        controller.PageSize = 3;

        // 页码不能停在一页不存在的位置上。
        Assert.True(controller.Page <= controller.Pages);
        Assert.Equal("k", controller.Buffer);
    }

    [Theory]
    [InlineData(Keys.A | Keys.Control, 0x41, true, false, false)]
    [InlineData(Keys.F12 | Keys.Control | Keys.Alt, 0x7B, true, true, false)]
    [InlineData(Keys.OemPeriod | Keys.Control | Keys.Shift, 0xBE, true, false, true)]
    [InlineData(Keys.Space | Keys.Shift, 0x20, false, false, true)]
    public void CaptureBoxKeyData_BecomesACombo(Keys keyData, uint vk, bool ctrl, bool alt, bool shift) =>
        Assert.Equal(KeyBinding.Combo(vk, ctrl, alt, shift), KeyBinding.FromKeyData(keyData));

    [Theory]
    [InlineData(Keys.B)]
    [InlineData(Keys.LShiftKey)]
    [InlineData(Keys.RMenu)]
    public void CaptureBoxKeyData_WithoutAUsableShapeIsRejected(Keys keyData) =>
        Assert.Null(KeyBinding.FromKeyData(keyData));

    [Theory]
    [InlineData(0xA0, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x10, false)]
    [InlineData(0x20, false)]
    public void OnlyLeftRightModifierVksCanServeAsATapKey(uint vk, bool expected) =>
        Assert.Equal(expected, KeyBinding.IsModifierKey(vk));

    private static AppSettings Conflicting() => new()
    {
        EnglishToggle = KeyBinding.Combo(0x41, ctrl: true),
        PunctuationToggle = KeyBinding.Combo(0x41, ctrl: true),
    };

    private static KeyBinding Parse(string token)
    {
        KeyBinding.TryParse(token, out var binding);
        return binding;
    }

    private static ImeController NewController(AppSettings? settings = null) =>
        new(new ImeEngine(TestDict.Table)) { Settings = settings ?? new AppSettings() };

    private static KeyEvent Letter(char letter) => Key((uint)(VirtualKey.A + char.ToUpperInvariant(letter) - 'A'));

    private static KeyEvent Key(uint vk, bool ctrl = false, bool alt = false, bool shift = false) =>
        new(vk, 0, ctrl, alt, shift, false);
}
