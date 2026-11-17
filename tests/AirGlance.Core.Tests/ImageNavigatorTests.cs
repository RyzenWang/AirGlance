using AirGlance.Core.Navigation;
using Xunit;

namespace AirGlance.Core.Tests;

public sealed class ImageNavigatorTests
{
    private static ImageNavigator Navigator(params string[] files) => new(files);

    [Fact]
    public void Next_and_previous_wrap_and_report_it()
    {
        var navigator = Navigator("a.jpg", "b.jpg", "c.jpg");

        var first = navigator.MoveNext();
        Assert.NotNull(first);
        Assert.Equal("b.jpg", first!.Value.Path);
        Assert.False(first.Value.Wrapped);

        navigator.MoveNext();
        var wrapped = navigator.MoveNext();
        Assert.NotNull(wrapped);
        Assert.Equal("a.jpg", wrapped!.Value.Path);
        Assert.True(wrapped.Value.Wrapped);

        var previous = navigator.MovePrevious();
        Assert.NotNull(previous);
        Assert.Equal("c.jpg", previous!.Value.Path);
        Assert.True(previous.Value.Wrapped);
    }

    [Fact]
    public void Single_image_does_not_report_wrapping()
    {
        var navigator = Navigator("only.jpg");

        var step = navigator.MoveNext();

        Assert.NotNull(step);
        Assert.Equal(0, step!.Value.Index);
        Assert.False(step.Value.Wrapped);
    }

    [Fact]
    public void Empty_navigator_returns_nothing()
    {
        var navigator = Navigator();

        Assert.False(navigator.HasImages);
        Assert.Null(navigator.CurrentPath);
        Assert.Null(navigator.MoveNext());
        Assert.Null(navigator.MovePrevious());
        Assert.Null(navigator.GoTo(0));
    }

    [Fact]
    public void GoTo_selects_an_index_without_wrapping()
    {
        var navigator = Navigator("a.jpg", "b.jpg", "c.jpg");

        var step = navigator.GoTo(2);

        Assert.NotNull(step);
        Assert.Equal("c.jpg", step!.Value.Path);
        Assert.False(step.Value.Wrapped);
        Assert.Null(navigator.GoTo(2));
        Assert.Equal(2, navigator.CurrentIndex);
    }
}
