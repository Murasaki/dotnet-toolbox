using Toolbox.Concurrency;

namespace Toolbox.Concurrency.Tests;

public class AsyncPipelineTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Empty_pipeline_passes_items_through()
    {
        var results = await ToListAsync(AsyncPipeline.Create<int>().ProcessAsync([1, 2, 3]));

        Assert.Equal([1, 2, 3], results.Order());
    }

    [Fact]
    public async Task Stages_run_in_order_and_can_change_type()
    {
        var pipeline = AsyncPipeline.Create<int>()
            .AddStage(x => Task.FromResult(x * 2))
            .AddStage((x, _) => Task.FromResult($"#{x}"));

        var results = await ToListAsync(pipeline.WithMaxConcurrency(3).ProcessAsync([1, 2, 3]));

        Assert.Equal(["#2", "#4", "#6"], results.Order());
    }

    [Fact]
    public async Task Default_concurrency_is_one()
    {
        var tracker = new ConcurrencyTracker();
        var pipeline = AsyncPipeline.Create<int>().AddStage(tracker.Stage);

        await ToListAsync(pipeline.ProcessAsync(Enumerable.Range(0, 5)));

        Assert.Equal(1, pipeline.MaxConcurrency);
        Assert.Equal(1, tracker.MaxObserved);
    }

    [Fact]
    public async Task Never_exceeds_max_concurrency()
    {
        var tracker = new ConcurrencyTracker();
        var pipeline = AsyncPipeline.Create<int>().AddStage(tracker.Stage).WithMaxConcurrency(4);

        var results = await ToListAsync(pipeline.ProcessAsync(Enumerable.Range(0, 20)));

        Assert.Equal(20, results.Count);
        Assert.InRange(tracker.MaxObserved, 2, 4);
    }

    [Fact]
    public void WithMaxConcurrency_returns_a_new_pipeline_and_leaves_the_original_unchanged()
    {
        var original = AsyncPipeline.Create<int>();

        var configured = original.WithMaxConcurrency(8);

        Assert.Equal(1, original.MaxConcurrency);
        Assert.Equal(8, configured.MaxConcurrency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void WithMaxConcurrency_rejects_values_below_one(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AsyncPipeline.Create<int>().WithMaxConcurrency(value));
    }

    [Fact]
    public async Task Stage_failure_is_rethrown_from_the_enumeration()
    {
        var pipeline = AsyncPipeline.Create<int>()
            .AddStage(x => x == 3 ? throw new InvalidOperationException("boom") : Task.FromResult(x))
            .WithMaxConcurrency(2);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToListAsync(pipeline.ProcessAsync(Enumerable.Range(0, 10))).WaitAsync(Timeout));

        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public async Task Stage_failure_stops_the_remaining_items()
    {
        var started = 0;
        var pipeline = AsyncPipeline.Create<int>()
            .AddStage(async (x, ct) =>
            {
                Interlocked.Increment(ref started);
                await Task.Delay(5, ct);
                return x == 5 ? throw new InvalidOperationException() : x;
            })
            .WithMaxConcurrency(2);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ToListAsync(pipeline.ProcessAsync(Enumerable.Range(0, 1_000))).WaitAsync(Timeout));

        Assert.True(started < 100, $"expected processing to stop early, but {started} items started");
    }

    [Fact]
    public async Task External_cancellation_stops_processing()
    {
        using var cts = new CancellationTokenSource();
        var pipeline = AsyncPipeline.Create<int>()
            .AddStage(async (x, ct) => { await Task.Delay(10, ct); return x; })
            .WithMaxConcurrency(2);

        var consume = Task.Run(async () =>
        {
            await foreach (var _ in pipeline.ProcessAsync(Enumerable.Range(0, 1_000), cts.Token))
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consume.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Breaking_out_early_cancels_in_flight_work()
    {
        var cancelled = 0;
        var pipeline = AsyncPipeline.Create<int>()
            .AddStage(async (x, ct) =>
            {
                if (x == 0) return x;
                try { await Task.Delay(Timeout, ct); }
                catch (OperationCanceledException) { Interlocked.Increment(ref cancelled); throw; }
                return x;
            })
            .WithMaxConcurrency(3);

        var consume = Task.Run(async () =>
        {
            await foreach (var _ in pipeline.ProcessAsync(Enumerable.Range(0, 100)))
            {
                break;
            }
        });

        await consume.WaitAsync(Timeout);
        Assert.True(cancelled > 0, "expected the in-flight items to be cancelled");
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private sealed class ConcurrencyTracker
    {
        private int _current;
        private int _max;

        public int MaxObserved => Volatile.Read(ref _max);

        public async Task<int> Stage(int item)
        {
            var now = Interlocked.Increment(ref _current);
            InterlockedMax(ref _max, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref _current);
            return item;
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen;
            while (value > (seen = Volatile.Read(ref target)) &&
                   Interlocked.CompareExchange(ref target, value, seen) != seen)
            {
            }
        }
    }
}
