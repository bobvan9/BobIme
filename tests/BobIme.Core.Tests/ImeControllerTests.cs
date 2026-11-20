using BobIme.Core;
using BobIme.Shell;
using BobIme.Shell.Interop;

namespace BobIme.Core.Tests;

public class ImeControllerTests
{
    private const uint Period = 0xBE;
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
    public void UnassignedKey_PassesThroughAndCommitsFirstCandidate()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        var decision = controller.OnKeyDown(Key(Period));

        // 标点要先把首候选送进去，再把键本身放行，让目标程序自己打句号。
        Assert.False(decision.Consume);
        Assert.Equal("中", decision.CommitText);
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
    public void ToggleChineseMode_DropsHalfCode()
    {
        var controller = NewController();
        controller.OnKeyDown(Letter('k'));

        controller.ToggleChineseMode();

        Assert.False(controller.IsComposing);
        Assert.Equal("", controller.Buffer);
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
