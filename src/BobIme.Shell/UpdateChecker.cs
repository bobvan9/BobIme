using System.Reflection;
using BobIme.Core;

namespace BobIme.Shell;

/// <summary>托盘「检查更新」查出来的三种结果。</summary>
public enum UpdateState
{
    UpToDate,
    Newer,
    Failed,
}

/// <summary>
/// 整个程序唯一的一处联网：读 GitHub Releases 的 latest 标签，跟本机这份的版本号比一下。
/// 不上报、不下载、不装——查到新版也只是给出下载页地址，开不开由人点。
/// </summary>
internal static class UpdateChecker
{
    private const string LatestUrl = "https://api.github.com/repos/bobvan9/BobIme/releases/latest";

    /// <summary>查到的下载地址要拿去开浏览器，所以只认这个前缀，别人塞进来的不算。</summary>
    private const string ReleaseHost = "https://github.com/";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <param name="Detail">查不到时要对用户说的那一句，只有 <see cref="UpdateState.Failed"/> 用得上。</param>
    public sealed record Result(UpdateState State, string Current, string Latest, string Url, string Detail);

    /// <summary>本机这份的版本号，取 SDK 拼在 InformationalVersion 的 '+' 之前那一段。</summary>
    public static string CurrentVersion()
    {
        var informational = typeof(UpdateChecker).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    public static async Task<Result> CheckAsync()
    {
        var current = CurrentVersion();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
            request.Headers.UserAgent.ParseAdd("BobIme");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Fail(current, $"GitHub 回了 {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            var info = ReleaseInfo.FromJson(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            // 地址前缀要管：那个字符串之后要拿去开浏览器。标签认不出成形版本号也别当成新版，直接说读不出来。
            if (info is null || !info.Url.StartsWith(ReleaseHost, StringComparison.Ordinal) ||
                !ReleaseInfo.IsVersion(info.Tag))
            {
                return Fail(current, "读不出最新版本号。");
            }

            return new Result(
                ReleaseInfo.Compare(info.Tag, current) > 0 ? UpdateState.Newer : UpdateState.UpToDate,
                current,
                info.Tag,
                info.Url,
                "");
        }
        catch (Exception ex)
        {
            return Fail(current, ex.Message);
        }
    }

    private static Result Fail(string current, string detail) => new(UpdateState.Failed, current, "", "", detail);
}
