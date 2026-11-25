using AirGlance.App.Services;
using Xunit;

namespace AirGlance.App.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task A_second_launch_forwards_its_paths_to_the_owner()
    {
        var scope = "single-instance-test-" + Guid.NewGuid().ToString("N");
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var owner = SingleInstance.Acquire(scope, paths => received.TrySetResult(paths));
        Assert.NotNull(owner);

        // The second acquisition is what a launching process performs first; it must
        // lose the name so it knows to forward instead of opening another window.
        Assert.Null(SingleInstance.Acquire(scope, _ => { }));

        var shared = new[] { "/tmp/a.jpg", "/tmp/b.png" };
        Assert.True(SingleInstance.Forward(scope, shared));

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(received.Task, completed);
        Assert.Equal(shared, await received.Task);
    }

    [Fact]
    public void Forwarding_without_an_owner_reports_failure()
    {
        var scope = "single-instance-absent-" + Guid.NewGuid().ToString("N");

        Assert.False(SingleInstance.Forward(scope, ["/tmp/a.jpg"], timeoutMilliseconds: 250));
    }
}
