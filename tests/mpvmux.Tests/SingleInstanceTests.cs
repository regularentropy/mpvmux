using mpvmux.Services;
using System;
using System.IO;
using Xunit;

namespace mpvmux.Tests;

public sealed class SingleInstanceTests : IDisposable
{
    private readonly string _rootPath;
    private readonly string _lockPath;

    public SingleInstanceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "mpvmux-instance-tests-" + Guid.NewGuid().ToString("N"));
        _lockPath = Path.Combine(_rootPath, "instance.lock");
    }

    [Fact]
    public void TheFirstInstanceTakesTheLock()
    {
        Assert.True(SingleInstanceGuard.TryAcquire(out var guard, _lockPath));
        Assert.NotNull(guard);

        guard!.Dispose();
    }

    [Fact]
    public void ASecondInstanceCannotTakeAHeldLock()
    {
        Assert.True(SingleInstanceGuard.TryAcquire(out var first, _lockPath));

        Assert.False(SingleInstanceGuard.TryAcquire(out var second, _lockPath));
        Assert.Null(second);

        first!.Dispose();
    }

    [Fact]
    public void ReleasingTheLockLetsTheNextInstanceTakeIt()
    {
        Assert.True(SingleInstanceGuard.TryAcquire(out var first, _lockPath));
        first!.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(out var second, _lockPath));
        second!.Dispose();
    }

    [Fact]
    public void ALeftoverLockFileWithoutAnOpenHandleDoesNotBlock()
    {
        // a crash leaves the file behind; only an open handle means "already running"
        Directory.CreateDirectory(_rootPath);
        File.WriteAllText(_lockPath, "12345\n");

        Assert.True(SingleInstanceGuard.TryAcquire(out var guard, _lockPath));
        guard!.Dispose();
    }

    [Fact]
    public void TheLockRecordsTheProcessIdForDiagnostics()
    {
        Assert.True(SingleInstanceGuard.TryAcquire(out var guard, _lockPath));
        guard!.Dispose();

        Assert.Contains(Environment.ProcessId.ToString(), File.ReadAllText(_lockPath));
    }

    [Fact]
    public void AcquiringCreatesTheDataFolder()
    {
        Assert.False(Directory.Exists(_rootPath));

        Assert.True(SingleInstanceGuard.TryAcquire(out var guard, _lockPath));
        guard!.Dispose();

        Assert.True(File.Exists(_lockPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_rootPath, recursive: true);
        }
        catch (IOException)
        {
            // best effort: a leftover temp directory must not fail the test run
        }
    }
}
