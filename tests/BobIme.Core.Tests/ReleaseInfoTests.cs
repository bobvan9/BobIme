namespace BobIme.Core.Tests;

public class ReleaseInfoTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData(" v1.2.3 ", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v2", "2")]
    [InlineData("version-2", "version-2")]
    [InlineData("v", "v")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeTag_OnlyStripsAVersionPrefix(string? tag, string expected) =>
        Assert.Equal(expected, ReleaseInfo.NormalizeTag(tag));

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2.3")]
    [InlineData("1.10")]
    [InlineData("0")]
    public void IsVersion_WantsEverySegmentNumeric(string tag) => Assert.True(ReleaseInfo.IsVersion(tag));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1.2.x")]
    [InlineData("1..2")]
    [InlineData("1.2-rc1")]
    [InlineData("latest")]
    public void IsVersion_RejectsTagsThatCannotBeOrdered(string? tag) =>
        Assert.False(ReleaseInfo.IsVersion(tag));

    [Theory]
    [InlineData("v1.1.4", "1.1.4", 0)]
    [InlineData("1.1.4", "1.1.5", -1)]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2.0", "1.99.99", 1)]
    [InlineData("1.1.4", "1.1.4.1", -1)]
    [InlineData("v0.9", "1.0", -1)]
    public void Compare_OrdersSegmentBySegmentAsNumbers(string left, string right, int expected) =>
        Assert.Equal(expected, Math.Sign(ReleaseInfo.Compare(left, right)));

    [Fact]
    public void Compare_FallsBackToTextForTagsOutsideTheRule() =>
        // 认不出来的两边退回字符串比，调用方靠 IsVersion 先把这种标签挡掉。
        Assert.True(ReleaseInfo.Compare("1.2-rc1", "1.2.0") < 0);

    [Fact]
    public void FromJson_ReadsTagAndUrlFromTheLatestRelease()
    {
        const string json = """
            {"url":"https://api.github.com/repos/bobvan9/BobIme/releases/454144897",
             "html_url":"https://github.com/bobvan9/BobIme/releases/tag/v1.1.4",
             "tag_name":"v1.1.4","name":"Bob 五笔 1.1.4","draft":false,"prerelease":false,
             "assets":[{"name":"BobIme-Setup-1.1.4.exe","digest":"sha256:e69125d4"}]}
            """;

        var info = ReleaseInfo.FromJson(json);
        Assert.Equal("v1.1.4", info!.Tag);
        Assert.Equal("https://github.com/bobvan9/BobIme/releases/tag/v1.1.4", info.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"tag_name":"v1.2.3"}""")]
    [InlineData("""{"html_url":"https://github.com/x/y"}""")]
    [InlineData("""{"tag_name":null,"html_url":null}""")]
    [InlineData("""{"tag_name":"","html_url":"https://github.com/x/y"}""")]
    [InlineData("""{"tag_name":123,"html_url":["https://github.com/x/y"]}""")]
    public void FromJson_ReturnsNullWhenTheAnswerIsNotUsable(string json) =>
        Assert.Null(ReleaseInfo.FromJson(json));

    [Fact]
    public void FromJson_AcceptsNullWithoutThrowing() => Assert.Null(ReleaseInfo.FromJson(null));
}
