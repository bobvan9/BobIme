using System.Diagnostics;

namespace BobIme.Shell;

/// <summary>
/// 「关于」里那条「检查更新」的全部界面动作：查一次、按结果说一句、要下载就开下载页。
/// 入口只有那一个链接，所以防重入也收在这儿——检查期间整张窗口挂沙漏，再点不发第二个请求。
/// </summary>
internal static class UpdateFlow
{
    /// <summary>链接上的文字，<see cref="RunAsync"/> 靠它认是哪一条链接被点了。</summary>
    public const string CheckLabel = "检查更新";

    private static bool _running;

    public static async Task RunAsync(Form owner, string label)
    {
        if (label != CheckLabel || _running)
        {
            return;
        }

        _running = true;
        owner.UseWaitCursor = true;
        UpdateChecker.Result result;
        try
        {
            result = await UpdateChecker.CheckAsync();
        }
        finally
        {
            owner.UseWaitCursor = false;
            _running = false;
        }

        // 对话框在结果之后才弹：MessageBox 是阻塞的，压着沙漏等人点确定没道理。
        ShowResult(owner, result);
    }

    private static void ShowResult(Form owner, UpdateChecker.Result result)
    {
        switch (result.State)
        {
            case UpdateState.Failed:
                MessageBox.Show(owner, $"没能查到最新版本：{result.Detail}\n本机这份是 {result.Current}。", CheckLabel);
                break;
            case UpdateState.UpToDate:
                MessageBox.Show(owner, $"已经是最新版 {result.Current}。", CheckLabel);
                break;
            case UpdateState.Newer:
                if (MessageBox.Show(
                        owner,
                        $"有新版本：{result.Latest}（本机这份是 {result.Current}）\n要打开下载页吗？",
                        CheckLabel,
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    OpenDownloadPage(owner, result.Url);
                }

                break;
        }
    }

    private static void OpenDownloadPage(Form owner, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"没能叫浏览器打开：{ex.Message}\n下载地址：{url}", CheckLabel);
        }
    }
}
