using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Toolbox.Concurrency;

/// <summary>Entry point for building an <see cref="AsyncPipeline{TIn, TOut}"/>.</summary>
public static class AsyncPipeline
{
    /// <summary>Starts an empty pipeline that passes items of type <typeparamref name="T"/> through unchanged.</summary>
    public static AsyncPipeline<T, T> Create<T>() => new((item, _) => Task.FromResult(item), maxConcurrency: 1);
}

/// <summary>
/// An immutable chain of async stages that turns <typeparamref name="TIn"/> items into
/// <typeparamref name="TOut"/> results, running up to <see cref="MaxConcurrency"/> items at once.
/// </summary>
/// <remarks>
/// <para>Each item goes through every stage in order; different items run concurrently, so results are
/// yielded in <b>completion order</b>, not input order.</para>
/// <para>Fail-fast: the first exception thrown by a stage cancels the remaining work and is rethrown from
/// the enumeration. Results that completed before the failure may still be yielded first.</para>
/// <para>Stopping the enumeration early (e.g. <c>break</c>) cancels any work still in flight.</para>
/// </remarks>
public sealed class AsyncPipeline<TIn, TOut>
{
    private readonly Func<TIn, CancellationToken, Task<TOut>> _run;

    internal AsyncPipeline(Func<TIn, CancellationToken, Task<TOut>> run, int maxConcurrency)
    {
        _run = run;
        MaxConcurrency = maxConcurrency;
    }

    /// <summary>Maximum number of items processed at the same time. Defaults to 1.</summary>
    public int MaxConcurrency { get; }

    /// <summary>Returns a new pipeline with <paramref name="stage"/> appended.</summary>
    public AsyncPipeline<TIn, TNext> AddStage<TNext>(Func<TOut, CancellationToken, Task<TNext>> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        var previous = _run;

        return new AsyncPipeline<TIn, TNext>(
            async (input, ct) => await stage(await previous(input, ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            MaxConcurrency);
    }

    /// <summary>Returns a new pipeline with <paramref name="stage"/> appended.</summary>
    public AsyncPipeline<TIn, TNext> AddStage<TNext>(Func<TOut, Task<TNext>> stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return AddStage<TNext>((item, _) => stage(item));
    }

    /// <summary>Returns a new pipeline that processes up to <paramref name="maxConcurrency"/> items at once.</summary>
    public AsyncPipeline<TIn, TOut> WithMaxConcurrency(int maxConcurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrency, 1);
        return new AsyncPipeline<TIn, TOut>(_run, maxConcurrency);
    }

    /// <summary>Runs every item through the pipeline, yielding results as they complete.</summary>
    public async IAsyncEnumerable<TOut> ProcessAsync(
        IEnumerable<TIn> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var output = Channel.CreateBounded<TOut>(new BoundedChannelOptions(MaxConcurrency) { SingleReader = true });
        var run = RunAsync(items, output.Writer, cts.Token);
        var drained = false;

        try
        {
            while (await output.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (output.Reader.TryRead(out var result))
                {
                    yield return result;
                }
            }

            drained = true;
        }
        finally
        {
            if (!drained)
            {
                // The consumer stopped early or was cancelled: stop in-flight work and let it unwind.
                // Any error it raises is moot because the enumeration is already ending.
                cts.Cancel();
                try { await run.ConfigureAwait(false); } catch { }
            }
        }

        // Every result has been read; surface a stage failure or cancellation, if any.
        await run.ConfigureAwait(false);
    }

    private async Task RunAsync(IEnumerable<TIn> items, ChannelWriter<TOut> writer, CancellationToken cancellationToken)
    {
        try
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxConcurrency,
                CancellationToken = cancellationToken,
            };

            await Parallel.ForEachAsync(items, options, async (item, ct) =>
            {
                var result = await _run(item, ct).ConfigureAwait(false);
                await writer.WriteAsync(result, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            writer.TryComplete();
        }
    }
}
