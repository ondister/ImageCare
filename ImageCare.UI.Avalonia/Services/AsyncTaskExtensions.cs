using System;
using System.Threading;
using System.Threading.Tasks;

using Serilog;

namespace ImageCare.UI.Avalonia.Services;

/// <summary>
/// Helpers for tasks that are intentionally started but never awaited.
/// </summary>
internal static class AsyncTaskExtensions
{
    /// <summary>
    /// Marks a task as intentionally not awaited and reports its failure to the logger.
    /// </summary>
    /// <remarks>
    /// Use it instead of a bare <c>_ = SomeAsync();</c> when the operation is expected to run
    /// in the background: a discarded task swallows its exception, so failures would be lost.
    /// </remarks>
    public static void Forget(this Task task, ILogger logger, string operationName)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(logger);

        _ = task.ContinueWith(
            completed => logger.Error(
                completed.Exception?.GetBaseException(),
                "Background operation '{Operation}' failed",
                operationName),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
