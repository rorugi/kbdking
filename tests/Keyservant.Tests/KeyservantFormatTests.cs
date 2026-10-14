using Keyservant.Core.Formats;
using Keyservant.Core.Model;
using Keyservant.Core.Templates;

namespace Keyservant.Tests;

public class KeyservantFormatTests
{
    [Theory]
    [MemberData(nameof(TestLayouts.Templates), MemberType = typeof(TestLayouts))]
    public void Templates_round_trip_unchanged(string templateId)
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.Create(templateId));
        Assert.Equal(json, KeyservantFormat.Serialize(KeyservantFormat.Parse(json)));
    }

    [Fact]
    public void Everything_layout_round_trips_unchanged()
    {
        var json = KeyservantFormat.Serialize(TestLayouts.Everything());
        Assert.Equal(json, KeyservantFormat.Serialize(KeyservantFormat.Parse(json)));
    }

    [Fact]
    public void Each_key_is_written_on_its_own_line()
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.German());
        Assert.Contains("{\"vk\": \"Q\", \"attrs\": [\"capslock\"], \"base\": \"q\", \"shift\": \"Q\", \"altgr\": \"@\"}", json);
    }

    [Fact]
    public void Invisible_characters_are_escaped_and_visible_ones_are_not()
    {
        var json = KeyservantFormat.Serialize(TestLayouts.Everything());
        Assert.Contains("\"\\u0323\"", json);   // combining dot below
        Assert.Contains("\"\\u001b\"", json);   // escape
        Assert.Contains("\"ā\"", json);
        Assert.Contains("\"ṭ\"", json);
    }

    [Fact]
    public void Dead_keys_are_written_as_objects()
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.German());
        Assert.Contains("\"base\": {\"dead\": \"^\"}", json);
    }

    [Fact]
    public void Unknown_properties_produce_a_warning()
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.UnitedStates()).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"futureThing\": true,");
        var report = new ConversionReport();
        KeyservantFormat.Parse(json, report);
        Assert.Contains(report.Warnings, w => w.Contains("futureThing"));
    }

    [Theory]
    [InlineData("{}", "Not a Keyservant layout")]
    [InlineData("[1,2]", "must contain a JSON object")]
    [InlineData("{\"format\": \"keyservant-layout\", \"schemaVersion\": 99}", "newer")]
    [InlineData("not json", "Invalid JSON")]
    public void Malformed_files_give_clear_errors(string json, string expected)
    {
        var ex = Assert.Throws<LayoutFormatException>(() => KeyservantFormat.Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Typo_in_shift_state_is_reported_with_the_key()
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.UnitedStates()).Replace("\"vk\": \"Q\", \"attrs\": [\"capslock\"], \"base\"", "\"vk\": \"Q\", \"attrs\": [\"capslock\"], \"bsae\"");
        var ex = Assert.Throws<LayoutFormatException>(() => KeyservantFormat.Parse(json));
        Assert.Contains("bsae", ex.Message);
        Assert.Contains("Q", ex.Message);
    }

    [Fact]
    public void Comments_and_trailing_commas_are_accepted()
    {
        var json = KeyservantFormat.Serialize(LayoutTemplates.UnitedStates())
            .Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, // the schema\n");
        var layout = KeyservantFormat.Parse(json);
        Assert.Equal("US", layout.Metadata.Name);
    }

    [Fact]
    public void Sample_layout_parses_and_is_valid()
    {
        var layout = TestLayouts.Sample("german-austria-iast");
        Assert.Equal("kbdiast", layout.Metadata.DllName);
        Assert.False(Core.Validation.LayoutValidator.Validate(layout).HasErrors);
    }
}
