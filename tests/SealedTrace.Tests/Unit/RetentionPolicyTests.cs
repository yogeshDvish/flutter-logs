using FluentAssertions;
using Arovyx.SealedTrace.Infrastructure.Services;

namespace Arovyx.SealedTrace.Tests.Unit;

public sealed class RetentionPolicyTests
{
    [Fact]
    public void Default_options_have_expected_values()
    {
        var options = new RetentionOptions();
        options.DefaultMaxAgeDays.Should().Be(30);
        options.DefaultMaxEvents.Should().Be(1_000_000);
    }

    [Fact]
    public void Options_section_name_is_retention()
    {
        RetentionOptions.SectionName.Should().Be("Retention");
    }
}
