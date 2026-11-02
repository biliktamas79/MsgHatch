using System.Threading.Channels;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// One dispatch loop (section 4.9): a claim loop writes the claimed messages into a bounded work queue, and the send
/// workers read it. The claim loop waits for the earliest of a signal of its topic, the next due time, a time that a
/// worker gives to it, and the poll delay (D102, D103).
/// </summary>
/// <typeparam name="T">The message type.</typeparam>
internal sealed class DispatchLoop<T>(IDispatchWork<T> work, DispatchOptions options, IDispatchSignal signal, TimeProvider time)
    where T : class
{
    private readonly Lock _gate = new();
    private DateTime? _givenAtUtc;
    private CancellationTokenSource? _wake;
    private DateTime? _wakeDueUtc;

    private enum Wake
    {
        Due,
        Signal,
        TimeGiven,
    }

    public async ValueTask RunAsync(CancellationToken ct)
    {
        var queue = Channel.CreateBounded<T>(new BoundedChannelOptions(options.QueueCapacity) { SingleWriter = true });
        var workers = Enumerable.Range(0, options.MaxDegreeOfParallelism).Select(_ => WorkAsync(queue.Reader, ct)).ToArray();
        await ClaimUntilCancelledAsync(queue, ct);
        queue.Writer.TryComplete();
        await Task.WhenAll(workers);
    }

    public async ValueTask<ErrorOr<DispatchSummary>> RunOnceAsync(int batchSize, CancellationToken ct)
    {
        await work.BeforeBatchAsync(ct);
        var claimed = await ClaimSafelyAsync(batchSize, time.GetUtcNowDateTime(), ct);
        if (claimed.IsError)
        {
            return claimed.Error;
        }

        work.AfterClaim(claimed.Value);
        var messages = claimed.Value.Messages;
        var outcomes = new ItemOutcome[messages.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, messages.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxDegreeOfParallelism, CancellationToken = ct },
            async (position, token) => outcomes[position] = (await work.ProcessAsync(messages[position], token)).Outcome);
        ct.ThrowIfCancellationRequested();
        return new DispatchSummary(
            messages.Count, outcomes.Count(static o => o == ItemOutcome.Succeeded), outcomes.Count(static o => o == ItemOutcome.Retried),
            outcomes.Count(static o => o == ItemOutcome.Failed));
    }

    private async Task ClaimUntilCancelledAsync(Channel<T> queue, CancellationToken ct)
    {
        var pollDelay = options.PollInterval;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var free = options.QueueCapacity - queue.Reader.Count;
                if (free <= 0)
                {
                    await queue.Writer.WaitToWriteAsync(ct);
                    continue;
                }

                pollDelay = await ClaimOneRoundAsync(queue.Writer, free, pollDelay, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<TimeSpan> ClaimOneRoundAsync(ChannelWriter<T> writer, int free, TimeSpan pollDelay, CancellationToken ct)
    {
        await work.BeforeClaimAsync(ct);
        ForgetGivenTime();
        var nowUtc = time.GetUtcNowDateTime();
        var claimed = await ClaimSafelyAsync(free, nowUtc, ct);
        if (claimed.IsError)
        {
            await WaitAsync(nowUtc + pollDelay, ct);
            return Longer(pollDelay);
        }

        var wakeAtUtc = work.AfterClaim(claimed.Value);
        foreach (var message in claimed.Value.Messages)
        {
            writer.TryWrite(message);
        }

        var count = claimed.Value.Messages.Count;
        if (count >= free)
        {
            return options.PollInterval;
        }

        var pollAtUtc = options.EnablePolling ? nowUtc + pollDelay : (DateTime?)null;
        var signalled = await WaitAsync(Earliest(wakeAtUtc, pollAtUtc), ct);
        return signalled || count > 0 ? options.PollInterval : Longer(pollDelay);
    }

    private async ValueTask<ErrorOr<ClaimResult<T>>> ClaimSafelyAsync(int maxCount, DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            return await work.ClaimAsync(maxCount, nowUtc, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new Error(Errors.Exception, exception.Message, exception);
        }
    }

    /// <returns>True when a signal woke the loop, and false when a due time came.</returns>
    private async ValueTask<bool> WaitAsync(DateTime? deadlineUtc, CancellationToken ct)
    {
        var wake = Wake.TimeGiven;
        while (wake == Wake.TimeGiven)
        {
            wake = await WaitUntilTheEarliestTimeAsync(deadlineUtc, ct);
        }

        return wake == Wake.Signal;
    }

    private async ValueTask<Wake> WaitUntilTheEarliestTimeAsync(DateTime? deadlineUtc, CancellationToken ct)
    {
        var dueUtc = EarliestWithGivenTime(deadlineUtc);
        var delay = dueUtc - time.GetUtcNowDateTime();
        if (delay <= TimeSpan.Zero)
        {
            return Wake.Due;
        }

        using var timeout = delay is { } wait ? new CancellationTokenSource(wait, time) : null;
        using var wake = timeout is null
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        if (!Publish(wake, dueUtc))
        {
            return Wake.TimeGiven;
        }

        try
        {
            var error = await signal.WaitAsync(work.Topic, wake.Token);
            ct.ThrowIfCancellationRequested();
            return error is null ? Wake.Signal
                : error.Type != Errors.OperationCancelled ? await WaitForTheBrokenSignalAsync(dueUtc, ct)
                : timeout?.IsCancellationRequested == true ? Wake.Due
                : Wake.TimeGiven;
        }
        finally
        {
            Unpublish();
        }
    }

    private async ValueTask<Wake> WaitForTheBrokenSignalAsync(DateTime? dueUtc, CancellationToken ct)
    {
        var nowUtc = time.GetUtcNowDateTime();
        var delay = (dueUtc ?? nowUtc + options.PollInterval) - nowUtc;
        await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, time, ct);
        return Wake.Due;
    }

    private async Task WorkAsync(ChannelReader<T> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var message in reader.ReadAllAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (await ProcessOneAsync(message, ct) is { } giveAtUtc)
                {
                    Give(giveAtUtc);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async ValueTask<DateTime?> ProcessOneAsync(T message, CancellationToken ct) =>
        LeaseEndsSoon(message) is { } leaseEndUtc ? leaseEndUtc : (await work.ProcessAsync(message, ct)).GiveAtUtc;

    private DateTime? LeaseEndsSoon(T message) =>
        work.LeaseUntilUtcOf(message) is { } leaseEndUtc && leaseEndUtc <= time.GetUtcNowDateTime() + options.LeaseSafetyMargin
            ? leaseEndUtc
            : null;

    private void Give(DateTime atUtc)
    {
        lock (_gate)
        {
            if (_givenAtUtc is null || atUtc < _givenAtUtc)
            {
                _givenAtUtc = atUtc;
            }

            if (_wake is not null && (_wakeDueUtc is null || atUtc < _wakeDueUtc))
            {
                _wake.Cancel();
            }
        }
    }

    private void ForgetGivenTime()
    {
        lock (_gate)
        {
            _givenAtUtc = null;
        }
    }

    private DateTime? EarliestWithGivenTime(DateTime? deadlineUtc)
    {
        lock (_gate)
        {
            return Earliest(deadlineUtc, _givenAtUtc);
        }
    }

    private bool Publish(CancellationTokenSource wake, DateTime? dueUtc)
    {
        lock (_gate)
        {
            if (Earliest(dueUtc, _givenAtUtc) != dueUtc)
            {
                return false;
            }

            (_wake, _wakeDueUtc) = (wake, dueUtc);
            return true;
        }
    }

    private void Unpublish()
    {
        lock (_gate)
        {
            (_wake, _wakeDueUtc) = (null, null);
        }
    }

    private TimeSpan Longer(TimeSpan pollDelay) =>
        pollDelay * 2 < options.MaxPollInterval ? pollDelay * 2 : options.MaxPollInterval;

    private static DateTime? Earliest(DateTime? first, DateTime? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;
}
