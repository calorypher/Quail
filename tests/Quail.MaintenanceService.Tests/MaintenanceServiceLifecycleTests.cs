using Quail.MaintenanceService;

namespace Quail.MaintenanceService.Tests;

public sealed class MaintenanceServiceLifecycleTests
{
    [Fact]
    public void StartInvokesRuntimeWithCancellationToken()
    {
        using var runtime = new RecordingRuntime();
        var lifecycle = new MaintenanceServiceLifecycle(runtime, TimeSpan.FromSeconds(1));

        lifecycle.Start();
        lifecycle.Stop();

        Assert.True(runtime.Started);
        Assert.True(runtime.CancellationRequested);
    }

    [Fact]
    public void SynchronousStartupFailureIsPropagated()
    {
        var lifecycle = new MaintenanceServiceLifecycle(
            new ThrowingRuntime(new InvalidOperationException("startup")),
            TimeSpan.FromSeconds(1));

        var exception = Assert.Throws<InvalidOperationException>(lifecycle.Start);

        Assert.Equal("startup", exception.Message);
    }

    [Fact]
    public void BackgroundFailureIsPropagatedWhenStopping()
    {
        var runtime = new FaultingRuntime();
        var lifecycle = new MaintenanceServiceLifecycle(
            runtime,
            TimeSpan.FromSeconds(1));
        lifecycle.Start();
        runtime.Fail(new InvalidOperationException("background"));

        var exception = Assert.Throws<InvalidOperationException>(lifecycle.Stop);

        Assert.Equal("background", exception.Message);
    }

    [Fact]
    public void StopIsBoundedWhenRuntimeIgnoresCancellation()
    {
        var lifecycle = new MaintenanceServiceLifecycle(
            new BlockingRuntime(),
            TimeSpan.FromMilliseconds(25));
        lifecycle.Start();

        Assert.Throws<TimeoutException>(lifecycle.Stop);
    }

    private sealed class RecordingRuntime : IMaintenanceServiceRuntime, IDisposable
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationToken _token;

        public bool Started { get; private set; }
        public bool CancellationRequested => _token.IsCancellationRequested;

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            Started = true;
            _token = stoppingToken;
            await _completion.Task.WaitAsync(stoppingToken);
        }

        public void Dispose() => _completion.TrySetResult();
    }

    private sealed class ThrowingRuntime(Exception failure) : IMaintenanceServiceRuntime
    {
        public Task RunAsync(CancellationToken stoppingToken) => throw failure;
    }

    private sealed class FaultingRuntime : IMaintenanceServiceRuntime
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(CancellationToken stoppingToken) => _completion.Task;

        public void Fail(Exception failure) => _completion.TrySetException(failure);
    }

    private sealed class BlockingRuntime : IMaintenanceServiceRuntime
    {
        public Task RunAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
