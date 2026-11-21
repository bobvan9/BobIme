using System.Runtime.InteropServices;

namespace BobIme.Shell.Interop;

/// <summary>
/// UIA（无障碍接口）的客户端管道：缓存一个 CUIAutomation 实例，向外只给焦点元素和模式接口。
/// 浏览器、Electron 这类程序不建系统光标，候选窗的位置只能问它。
/// </summary>
internal static class Uia
{
    internal const int TextPatternId = 10014;

    /// <summary>UIA 客户端类的注册键；文档里另有一个没登记进注册表的旧值，别照抄。</summary>
    private static readonly Guid AutomationClsid = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    private static Automation? _automation;
    private static bool _unavailable;

    /// <summary>当前焦点元素；取不到就是 <c>null</c>，调用方自己退回下一条路。</summary>
    public static Element? FocusedElement()
    {
        if (Instance() is not { } automation)
        {
            return null;
        }

        try
        {
            automation.GetFocusedElement(out var element);
            return element;
        }
        catch
        {
            // 目标程序没实现无障碍、正在忙、或者已经退了。
            return null;
        }
    }

    /// <summary>焦点元素实现的 TextPattern；没实现（或读不到）就是 <c>null</c>。</summary>
    public static TextPattern? TextPatternOf(Element element)
    {
        try
        {
            element.GetCurrentPattern(TextPatternId, out var pattern);
            return pattern as TextPattern;
        }
        catch
        {
            return null;
        }
    }

    private static Automation? Instance()
    {
        if (_unavailable)
        {
            return null;
        }

        if (_automation is not null)
        {
            return _automation;
        }

        try
        {
            var type = Type.GetTypeFromCLSID(AutomationClsid);
            _automation = type is null ? null : (Automation?)Activator.CreateInstance(type);
        }
        catch
        {
            _automation = null;
        }

        _unavailable = _automation is null;
        return _automation;
    }

    /// <summary>
    /// 虚表顺序照 SDK 的 <c>uiautomationclient.h</c>：用到的槽位写真签名，前面按顺序补空方法占位，
    /// <c>InterfaceIsIUnknown</c> 的接口不用重写 IUnknown 那三项。签名写错的代价是调用打到隔壁槽位，
    /// 所以宁可不声明也别乱序。
    /// </summary>
    [ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Automation
    {
        void CompareElements();
        void CompareRuntimeIds();
        void GetRootElement();
        void ElementFromHandle();
        void ElementFromPoint();
        void GetFocusedElement([MarshalAs(UnmanagedType.Interface)] out Element? element);
    }

    [ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Element
    {
        void SetFocus();
        void GetRuntimeId();
        void FindFirst();
        void FindAll();
        void FindFirstBuildCache();
        void FindAllBuildCache();
        void BuildUpdatedCache();
        void GetCurrentPropertyValue();
        void GetCurrentPropertyValueEx();
        void GetCachedPropertyValue();
        void GetCachedPropertyValueEx();
        void GetCurrentPatternAs();
        void GetCachedPatternAs();
        void GetCurrentPattern(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    }

    [ComImport, Guid("32eba289-3583-42c9-9c59-3b6d9a1e9b6a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface TextPattern
    {
        void RangeFromPoint();
        void RangeFromChild();
        void GetSelection([MarshalAs(UnmanagedType.Interface)] out TextRangeArray ranges);
    }

    [ComImport, Guid("ce4ae76a-e717-4c98-81ea-47371d028eb6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface TextRangeArray
    {
        void get_Length(out int length);
        void GetElement(int index, [MarshalAs(UnmanagedType.Interface)] out TextRange range);
    }

    [ComImport, Guid("a543cc6a-f4ae-494b-8239-c814481187a8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface TextRange
    {
        void Clone();
        void Compare();
        void CompareEndpoints();
        void ExpandToEnclosingUnit();
        void FindAttribute();
        void FindText();
        void GetAttributeValue();

        /// <summary>每四个浮点数一块，依次是 left、top、width、height，屏幕物理坐标。</summary>
        void GetBoundingRectangles(
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rectangles);
    }
}
