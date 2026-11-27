using AirGlance.Core.Files;
using Xunit;

namespace AirGlance.Core.Tests;

/// <summary>
/// The sort order is persisted as one string, so parsing has to survive a hand-edited or
/// older settings file and formatting has to round-trip.
/// </summary>
public sealed class FolderSortOrderTests
{
    [Theory]
    [InlineData(null, FolderSortField.Name, false)]
    [InlineData("", FolderSortField.Name, false)]
    [InlineData("name", FolderSortField.Name, false)]
    [InlineData("name-descending", FolderSortField.Name, true)]
    [InlineData("date", FolderSortField.DateModified, false)]
    [InlineData("date-descending", FolderSortField.DateModified, true)]
    [InlineData("DATE-DESCENDING", FolderSortField.DateModified, true)]
    [InlineData("size", FolderSortField.Size, false)]
    [InlineData("size-descending", FolderSortField.Size, true)]
    [InlineData("type", FolderSortField.Type, false)]
    [InlineData("type-descending", FolderSortField.Type, true)]
    [InlineData("modified", FolderSortField.DateModified, false)]
    [InlineData("extension", FolderSortField.Type, false)]
    [InlineData("nonsense", FolderSortField.Name, false)]
    [InlineData("nonsense-descending", FolderSortField.Name, true)]
    public void Parsing_is_tolerant_and_normalises(string? value, FolderSortField field, bool descending)
    {
        var order = FolderSortOrder.Parse(value);

        Assert.Equal(new FolderSortOrder(field, descending), order);
    }

    [Theory]
    [InlineData(FolderSortField.Name, false, "name")]
    [InlineData(FolderSortField.Name, true, "name-descending")]
    [InlineData(FolderSortField.DateModified, false, "date")]
    [InlineData(FolderSortField.DateModified, true, "date-descending")]
    [InlineData(FolderSortField.Size, false, "size")]
    [InlineData(FolderSortField.Size, true, "size-descending")]
    [InlineData(FolderSortField.Type, false, "type")]
    [InlineData(FolderSortField.Type, true, "type-descending")]
    public void Formatting_round_trips(FolderSortField field, bool descending, string expected)
    {
        var order = new FolderSortOrder(field, descending);

        Assert.Equal(expected, order.ToSettingValue());
        Assert.Equal(order, FolderSortOrder.Parse(expected));
        Assert.Equal(order.Reversed().Field, order.Field);
        Assert.NotEqual(order.Reversed().Descending, order.Descending);
    }

    [Fact]
    public void The_default_order_is_the_natural_name_order()
    {
        Assert.Equal(FolderSortOrder.Default, default(FolderSortOrder));
        Assert.Equal("name", FolderSortOrder.Default.ToSettingValue());
    }
}
