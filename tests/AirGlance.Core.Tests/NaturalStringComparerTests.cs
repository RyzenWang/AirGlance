using AirGlance.Core.Files;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class NaturalStringComparerTests
{
    private static int Compare(string left, string right) =>
        NaturalStringComparer.Instance.Compare(left, right);

    [Theory]
    [InlineData("img2.jpg", "img10.jpg")]
    [InlineData("photo-2.jpg", "photo-10.jpg")]
    [InlineData("a1b2", "a1b10")]
    [InlineData("1.jpg", "2.jpg")]
    [InlineData("img2.jpg", "img2a.jpg")]
    [InlineData("IMG2.jpg", "img10.jpg")]
    public void Sorts_numbers_by_value(string left, string right)
    {
        Assert.True(Compare(left, right) < 0, $"{left} should come before {right}");
        Assert.True(Compare(right, left) > 0, $"{right} should come after {left}");
    }

    [Fact]
    public void Leading_zeros_are_ordered_after_the_plain_number()
    {
        Assert.True(Compare("img2.jpg", "img02.jpg") < 0);
        Assert.True(Compare("img02.jpg", "img3.jpg") < 0);
    }

    [Fact]
    public void Big_numbers_compare_without_overflow()
    {
        Assert.True(Compare("img99999999999999999999.jpg", "img100000000000000000000.jpg") < 0);
        Assert.True(Compare("img1.jpg", "img0001.jpg") < 0);
    }

    [Fact]
    public void Equal_paths_compare_equal()
    {
        Assert.Equal(0, Compare("same.jpg", "same.jpg"));
    }

    [Fact]
    public void Case_differences_use_ordinal_tiebreak()
    {
        Assert.True(Compare("A.jpg", "a.jpg") < 0);
        Assert.True(Compare("a.jpg", "A.jpg") > 0);
    }
}
