using Dialoid.Core.Explain;

namespace Dialoid.Core.Tests.Explain;

public class RuNumberTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(812.4, "812")]
    [InlineData(999, "999")]
    [InlineData(1234, "1,2 тыс.")]
    [InlineData(12_345, "12,3 тыс.")]
    [InlineData(999_960, "1 млн")]
    [InlineData(3_400_000, "3,4 млн")]
    [InlineData(2_500_000_000, "2,5 млрд")]
    [InlineData(-1500, "−1,5 тыс.")]
    public void Short_uses_russian_abbreviations(double v, string expected) => Assert.Equal(expected, RuNumber.Short(v));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(812.4, "812")]
    [InlineData(12_345, "12 345")]
    [InlineData(1_234_567.6, "1 234 568")]
    [InlineData(-12_345, "−12 345")]
    public void Grouped_separates_thousands_with_a_space(double v, string expected) => Assert.Equal(expected, RuNumber.Grouped(v));
}
