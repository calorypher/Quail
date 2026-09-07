using Quail.MaintenanceService;

namespace Quail.MaintenanceService.Tests;

public sealed class MaintenanceServiceLifecycleTests
{
    [Fact]
    public void StartInvokesRuntimeWithCancellationToken()
    {
        using var runtime = new RecordingRuntime();
        var terminalFailures = 0;
        var lifecycle = new MaintenanceServiceLifecycle(
            runtime,
            TimeSpan.FromSeconds(1),
            _ => Interlocked.Increment(ref terminalFailures));

        lifecycle.Start();
        lifecycle.Stop();
        lifecycle.Stop();

        Assert.True(runtime.Started);
        Assert.True(runtime.CancellationRequested);
        Assert.Equal(0, terminalFailures);
    }

    [Fact]
    public void SynchronousStartupFailureIsPropagated()
    {
        var lifecycle = new MaintenanceServiceLifecycle(
            new ThrowingRuntime(new InvalidOperationException("startup")),
            TimeSpan.FromSeconds(1),
            _ => throw new Xunit.Sdk.XunitException("Startup failure must not use the post-start terminal path."));

        var exception = Assert.Throws<InvalidOperationException>(lifecycle.Start);

        Assert.Equal("startup", exception.Message);
    }

    [Fact]
    public async Task RuntimeFaultAfterSuccessfulStartUsesOneTerminalFailurePath()
    {
        var runtime = new FaultingRuntime();
        var terminalFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminalFailures = 0;
        var lifecycle = new MaintenanceServiceLifecycle(
            runtime,
            TimeSpan.FromSeconds(1),
            failure =>
            {
                Interlocked.Increment(ref terminalFailures);
                terminalFailure.TrySetResult(failure);
            });
        lifecycle.Start();
        runtime.Fail(new InvalidOperationException("background"));

        var exception = await terminalFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lifecycle.Stop();
        lifecycle.Stop();

        Assert.Equal("background", exception.Message);
        Assert.Equal(1, terminalFailures);
    }

    [Fact]
    public async Task RuntimeCompletionAfterSuccessfulStartUsesTerminalFailurePath()
    {
        var runtime = new CompletingRuntime();
        var terminalFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new MaintenanceServiceLifecycle(
            runtime,
            TimeSpan.FromSeconds(1),
            failure => terminalFailure.TrySetResult(failure));
        lifecycle.Start();

        runtime.Complete();

        var exception = await terminalFailure.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("The maintenance runtime completed without a stop request.", exception.Message);
        lifecycle.Stop();
    }

    [Fact]
    public void StopIsBoundedWhenRuntimeIgnoresCancellation()
    {
        var lifecycle = new MaintenanceServiceLifecycle(
            new BlockingRuntime(),
            TimeSpan.FromMilliseconds(25),
            _ => { });
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

    private sealed class CompletingRuntime : IMaintenanceServiceRuntime
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RunAsync(CancellationToken stoppingToken) => _completion.Task;

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class BlockingRuntime : IMaintenanceServiceRuntime
    {
        public Task RunAsync(CancellationToken stoppingToken) => Task.Delay(Timeout.InfiniteTimeSpan);
    }
}
