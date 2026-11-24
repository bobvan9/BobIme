using BobIme.Core;
using BobIme.Shell;
using BobIme.Shell.Interop;

namespace BobIme.Core.Tests;

public class ImeControllerTests
{
    private const uint F1 = 0x70;

    [Fact]
    public void Letter_StartsCompositionAndIsConsumed()
    {
        var controller = NewController();

        var decision = controller.OnKeyDown(Letter('k'));

        Assert.True(decision.Consume);
        Assert.Null(decision.CommitText);
        Assert.True(decision.Composing);
        Assert.Equal("k", controller.Buffer);
    }

    [Fact]
    public void UncomposableLetter_WhenIdlePassesThrough() =>
        Assert.False(NewController().OnKeyDown(Letter('z')).Consume);

    [Fact]
    public void InvalidLetter_MidCompositionIsSwallowedWithoutCommitting()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Letter('z'));

        // 编码途中敲无效码：吞掉不出声，已输入的编码不能被打散。
        Assert.True(decision is { Consume: true, CommitText: null });
        Assert.Equal("k", controller.Buffer);
    }

    [Fact]
    public void UniqueShortFullCode_CommitsImmediately()
    {
        var controller = NewController();

        var decision = Type(controller, "psu");

        Assert.Equal("宋", decision.CommitText);
        Assert.False(decision.Composing);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void FourCodeUniqueFullCode_WaitsForSpace()
    {
        var controller = NewController();

        var decision = Type(controller, "klgh");

        Assert.Null(decision.CommitText);
        Assert.True(controller.IsComposing);
        Assert.Equal("咽下", controller.OnKeyDown(Key(VirtualKey.Space)).CommitText);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void Space_CommitsFirstCandidate()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.Space));

        Assert.Equal("中", decision.CommitText);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void Enter_BehavesLikeSpace()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        Assert.Equal("中", controller.OnKeyDown(Key(VirtualKey.Enter)).CommitText);
    }

    [Theory]
    [InlineData(1, "中")]
    [InlineData(9, "呈")]
    public void Digit_PicksThatCandidate(int digit, string expected)
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        Assert.Equal(expected, controller.OnKeyDown(DigitKey(digit)).CommitText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Digit_OutsideCandidateList_IsSwallowedWithoutCommitting(int digit)
    {
        var controller = NewController();
        Type(controller, "khlg");

        var decision = controller.OnKeyDown(DigitKey(digit));

        // khlg 只有 3 个候选，0 和 5 都不能上屏，但也不能把数字漏给目标程序。
        Assert.True(decision.Consume);
        Assert.Null(decision.CommitText);
    }

    [Fact]
    public void Backspace_ShrinksBufferThenReleasesTheKey()
    {
        var controller = NewController();
        Type(controller, "kh");

        Assert.True(controller.OnKeyDown(Key(VirtualKey.Back)).Consume);
        Assert.Equal("k", controller.Buffer);

        controller.OnKeyDown(Key(VirtualKey.Back));
        Assert.False(controller.IsComposing);
        Assert.False(controller.OnKeyDown(Key(VirtualKey.Back)).Consume);
    }

    [Fact]
    public void Escape_DiscardsCompositionWithoutCommitting()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.Escape));

        Assert.True(decision.Consume);
        Assert.Null(decision.CommitText);
        Assert.False(decision.Composing);
    }

    [Fact]
    public void CloseBracket_MovesToTheNextPage()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemCloseBrackets));

        Assert.True(decision is { Consume: true, CommitText: null, Composing: true });
        Assert.Equal(2, controller.Page);
    }

    [Fact]
    public void OpenBracket_OnFirstPageIsSwallowedWithoutCommitting()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemOpenBrackets));

        // 翻不动也照样吞掉：放行的话会走「其他按键」把首选顶上屏，还漏一个方括号。
        Assert.True(decision is { Consume: true, CommitText: null });
        Assert.Equal(1, controller.Page);
        Assert.Equal("中", controller.Candidates[0].Text);
    }

    [Fact]
    public void CloseBracket_OnLastPageStaysPut()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));
        for (var i = 0; i < controller.Pages; i++)
        {
            controller.OnKeyDown(Key(VirtualKey.OemCloseBrackets));
        }

        Assert.Equal(controller.Pages, controller.Page);
        Assert.True(controller.OnKeyDown(Key(VirtualKey.OemCloseBrackets)).Consume);
        Assert.Equal(controller.Pages, controller.Page);
    }

    [Fact]
    public void Digit_SelectsFromTheCurrentPage()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));
        controller.OnKeyDown(Key(VirtualKey.OemCloseBrackets));

        var first = controller.Candidates[0].Text;

        Assert.NotEqual("中", first);
        Assert.Equal(first, controller.OnKeyDown(DigitKey(1)).CommitText);
    }

    [Theory]
    [InlineData(VirtualKey.OemOpenBrackets)]
    [InlineData(VirtualKey.OemCloseBrackets)]
    public void Brackets_WhenIdlePassThroughToTheProgram(uint vk) =>
        Assert.False(NewController().OnKeyDown(Key(vk)).Consume);

    [Fact]
    public void TurnPage_MouseArrowWalksThePages()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        Assert.True(controller.TurnPage(true));
        Assert.Equal(2, controller.Page);

        for (var i = 2; i < controller.Pages; i++)
        {
            controller.TurnPage(true);
        }

        // 翻到头就返回 false，候选窗那档箭头也跟着变灰，不会白点。
        Assert.False(controller.TurnPage(true));
        Assert.Equal(controller.Pages, controller.Page);

        Assert.True(controller.TurnPage(false));
        Assert.True(controller.IsComposing);
        Assert.Equal("k", controller.Buffer);
    }

    [Fact]
    public void TurnPage_OnFirstPageAndWhenIdleDoesNothing()
    {
        var idle = NewController();
        Assert.False(idle.TurnPage(false));
        Assert.False(idle.TurnPage(true));

        var controller = NewController();
        controller.OnKeyDown(Letter('k'));
        Assert.False(controller.TurnPage(false));
        Assert.Equal("k", controller.Buffer);
        Assert.Equal("中", controller.Candidates[0].Text);
    }

    [Fact]
    public void UnassignedKey_PassesThroughAndCommitsFirstCandidate()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(F1));

        // 没派上用途的键要先把首候选送进去，再把键本身放行，让目标程序自己处理。
        Assert.False(decision.Consume);
        Assert.Equal("中", decision.CommitText);
    }

    [Fact]
    public void Comma_InChinesePunctuation_SendsTheMarkAfterTheWord()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemComma));

        // 中文标点档：先顶词再顶标点，合成一次上屏，键本身不能再漏给目标程序。
        Assert.True(decision.Consume);
        Assert.Equal("中，", decision.CommitText);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void Comma_InEnglishPunctuation_LeavesTheKeyToTheProgram()
    {
        var controller = NewController();
        controller.ChinesePunctuation = false;
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemComma));

        Assert.False(decision.Consume);
        Assert.Equal("中", decision.CommitText);
    }

    [Theory]
    [InlineData(VirtualKey.OemPeriod, false, "。")]
    [InlineData(VirtualKey.OemPeriod, true, "》")]
    [InlineData(VirtualKey.OemComma, true, "《")]
    [InlineData(VirtualKey.OemQuestion, true, "？")]
    [InlineData(VirtualKey.OemSemicolon, false, "；")]
    [InlineData(VirtualKey.OemSemicolon, true, "：")]
    [InlineData(VirtualKey.D1, true, "！")]
    [InlineData(VirtualKey.D6, true, "……")]
    [InlineData(VirtualKey.D8, true, "×")]
    [InlineData(VirtualKey.D9, true, "（")]
    [InlineData(VirtualKey.D0, true, "）")]
    [InlineData(VirtualKey.OemMinus, true, "——")]
    [InlineData(VirtualKey.OemTilde, false, "·")]
    [InlineData(VirtualKey.OemTilde, true, "～")]
    [InlineData(VirtualKey.OemBackslash, false, "、")]
    public void Punctuation_WhenIdleSendsOnlyTheMark(uint vk, bool shift, string expected)
    {
        var decision = NewController().OnKeyDown(Key(vk, shift: shift));

        Assert.True(decision.Consume);
        Assert.Equal(expected, decision.CommitText);
        Assert.False(decision.Composing);
    }

    [Fact]
    public void Quotes_AlternateOpenAndClose()
    {
        var controller = NewController();

        Assert.Equal("‘", controller.OnKeyDown(Key(VirtualKey.OemQuotes)).CommitText);
        Assert.Equal("’", controller.OnKeyDown(Key(VirtualKey.OemQuotes)).CommitText);
        Assert.Equal("“", controller.OnKeyDown(Key(VirtualKey.OemQuotes, shift: true)).CommitText);
        Assert.Equal("”", controller.OnKeyDown(Key(VirtualKey.OemQuotes, shift: true)).CommitText);
    }

    [Fact]
    public void Brackets_KeepPagingInChinesePunctuation()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemCloseBrackets));

        // 方括号被翻页占了，中文标点档也不能把它抢走。
        Assert.True(decision.Consume);
        Assert.Null(decision.CommitText);
        Assert.Equal(2, controller.Page);
    }

    [Fact]
    public void ShiftedKeyNotInTheTable_KeepsTheCodeUntouched()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.OemOpenBrackets, shift: true));

        // Shift+[ 打出的是 { ，既不该翻页也没进标点表，照旧放行、手上半截编码不许被打散。
        Assert.False(decision.Consume);
        Assert.True(controller.IsComposing);
    }

    [Fact]
    public void Punctuation_DoesNotFireInEnglishMode()
    {
        var controller = NewController();
        controller.SetChineseMode(false);

        var decision = controller.OnKeyDown(Key(VirtualKey.OemComma));

        Assert.False(decision.Consume);
        Assert.Null(decision.CommitText);
    }

    [Fact]
    public void ShortcutWithCtrl_LeavesCompositionUntouched()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.A, ctrl: true));

        Assert.False(decision.Consume);
        Assert.Null(decision.CommitText);
        Assert.True(decision.Composing);
        Assert.True(controller.IsComposing);
    }

    [Fact]
    public void ShiftedLetter_IsNotPartOfTheCode()
    {
        var controller = NewController();

        var decision = controller.OnKeyDown(Letter('k', shift: true));

        Assert.False(decision.Consume);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void CtrlSpace_TogglesBothWays()
    {
        var controller = NewController();

        Assert.True(controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true)).Consume);
        Assert.False(controller.Enabled);
        Assert.False(controller.OnKeyDown(Letter('k')).Consume);

        controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true));
        Assert.True(controller.Enabled);
        Assert.True(controller.OnKeyDown(Letter('k')).Consume);
    }

    [Fact]
    public void DisabledController_PassesEverythingThrough()
    {
        var controller = NewController();
        controller.Enabled = false;

        var decision = controller.OnKeyDown(Key(F1));

        Assert.False(decision.Consume);
        Assert.False(decision.Composing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapsLockLetter_IsNeverPartOfTheCode(bool composing)
    {
        var controller = NewController();
        if (composing)
        {
            controller.OnKeyDown(Letter('k'));
        }

        var decision = controller.OnKeyDown(CapsLetter('k'));

        // Caps Lock 开着就是在大写英文，键要原样漏给目标程序，已输入的编码也不许被搅乱。
        Assert.False(decision.Consume);
        Assert.Null(decision.CommitText);
        Assert.Equal(composing, controller.IsComposing);
        Assert.Equal(composing ? "k" : "", controller.Buffer);
    }

    [Fact]
    public void CapsLockOff_LettersStillCompose()
    {
        var controller = NewController();

        Assert.True(controller.OnKeyDown(Letter('k')).Consume);
        Assert.True(controller.IsComposing);
    }

    [Fact]
    public void ShiftTap_TogglesToEnglishAndLettersPassThrough()
    {
        var controller = NewController();

        controller.ToggleChineseMode();

        Assert.False(controller.ChineseMode);
        Assert.False(controller.OnKeyDown(Letter('k')).Consume);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void ShiftTap_TwiceBackToChineseComposingWorks()
    {
        var controller = NewController();
        controller.ToggleChineseMode();
        controller.ToggleChineseMode();

        Assert.True(controller.ChineseMode);
        Assert.True(controller.OnKeyDown(Letter('k')).Consume);
    }

    [Fact]
    public void ToggleChineseMode_CommitsTheHalfCodeByDefault()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.ToggleChineseMode();

        // 切英文不再吞掉手上那半截编码：原样交出去，组码状态照旧清干净。
        Assert.Equal("k", decision.CommitText);
        Assert.False(controller.IsComposing);
        Assert.Equal("", controller.Buffer);
        Assert.False(controller.ChineseMode);
    }

    [Fact]
    public void ToggleChineseMode_WithoutTheSettingDropsTheHalfCode()
    {
        var controller = NewController();
        controller.Settings = new AppSettings { EnglishToggleCommitsCode = false };
        controller.OnKeyDown(Letter('k'));

        Assert.Null(controller.ToggleChineseMode().CommitText);
        Assert.False(controller.IsComposing);
    }

    [Fact]
    public void EnglishToggleCombo_CommitsTheHalfCodeAndStillEatsTheKey()
    {
        var controller = NewController();
        controller.Settings = new AppSettings
        {
            EnglishToggle = KeyBinding.Combo(VirtualKey.Space, ctrl: true),
            EnabledToggle = KeyBinding.Combo(F1, ctrl: true),
        };
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true));

        Assert.True(decision.Consume);
        Assert.Equal("k", decision.CommitText);
        Assert.False(controller.ChineseMode);
    }

    [Fact]
    public void EnglishToggleCombo_WhileIdleCommitsNothing()
    {
        var controller = NewController();
        controller.Settings = new AppSettings
        {
            EnglishToggle = KeyBinding.Combo(VirtualKey.Space, ctrl: true),
            EnabledToggle = KeyBinding.Combo(F1, ctrl: true),
        };

        Assert.Null(controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true)).CommitText);
    }

    [Fact]
    public void ToggleChineseMode_WhileDisabledKeepsChineseMode()
    {
        var controller = NewController();
        controller.Enabled = false;

        var decision = controller.ToggleChineseMode();

        // 停用状态下左 Shift 不归我们管，模式得留着，重新启用后还是中文。
        Assert.False(decision.Consume);
        Assert.True(controller.ChineseMode);
    }

    [Fact]
    public void CtrlSpace_StillSwitchesOffInEnglishMode()
    {
        var controller = NewController();
        controller.ToggleChineseMode();

        controller.OnKeyDown(Key(VirtualKey.Space, ctrl: true));

        // 英文模式下 Ctrl+Space 仍是总开关，否则关掉之后就再也打不开。
        Assert.False(controller.Enabled);
    }

    private static ImeController NewController() => new(new ImeEngine(TestDict.Table) { PageSize = 9 });

    private static KeyDecision Type(ImeController controller, string code)
    {
        var last = KeyDecision.Passthrough;
        foreach (var letter in code)
        {
            last = controller.OnKeyDown(Letter(letter));
        }

        return last;
    }

    private static KeyEvent Letter(char letter, bool ctrl = false, bool shift = false) =>
        Key((uint)(VirtualKey.A + char.ToUpperInvariant(letter) - 'A'), ctrl, shift: shift);

    private static KeyEvent CapsLetter(char letter) =>
        Key((uint)(VirtualKey.A + char.ToUpperInvariant(letter) - 'A'), caps: true);

    private static KeyEvent DigitKey(int digit) => Key(VirtualKey.D0 + (uint)digit);

    private static KeyEvent Key(
        uint vk, bool ctrl = false, bool alt = false, bool shift = false, bool caps = false) =>
        new(vk, 0, ctrl, alt, shift, caps);
}
