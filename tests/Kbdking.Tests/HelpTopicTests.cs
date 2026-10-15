using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Kbdking.Presentation;

namespace Kbdking.Tests;

public partial class HelpTopicTests
{
    private static string RepositoryRoot([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    [GeneratedRegex("""Topic="([^"]+)"|Tag="(tab-[^"]+)""")]
    private static partial Regex TopicRegex();

    [Fact]
    public void Every_info_button_in_the_app_has_an_explanation()
    {
        var views = Path.Combine(RepositoryRoot(), "src", "Kbdking.App", "Views");
        var used = Directory.GetFiles(views, "*.xaml")
            .SelectMany(f => TopicRegex().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
            .ToHashSet();
        Assert.True(used.Count > 20, $"only {used.Count} topics found");
        Assert.All(used, t => Assert.True(HelpTopics.All.ContainsKey(t), $"no help text for '{t}'"));
    }

    [Fact]
    public void Explanations_are_complete_sentences()
    {
        Assert.All(HelpTopics.All, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Value.Title), p.Key);
            Assert.True(p.Value.Text.Length >= 80, $"{p.Key} is too short to explain much");
            Assert.EndsWith(".", p.Value.Text.TrimEnd());
        });
        Assert.Equal("Help", HelpTopics.Get("does-not-exist").Title);
    }
}
