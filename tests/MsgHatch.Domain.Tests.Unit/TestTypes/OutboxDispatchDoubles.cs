using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the outbox dispatcher tests: an outbox dispatch store substitute whose claims give the queued results of
/// each channel, and then empty results. Each claim and each recorded try is observable, so that a test awaits the
/// call and never sleeps.
/// </summary>
public sealed class OutboxDispatchDoubles
{
    private readonly Channel<ClaimCall> _claims = Channel.CreateUnbounded<ClaimCall>();
    private readonly Channel<RecordCall> _records = Channel.CreateUnbounded<RecordCall>();
    private readonly ConcurrentDictionary<ChannelId, ConcurrentQueue<ErrorOr<ClaimResult<OutgoingMessage>>>> _results = new();

    public OutboxDispatchDoubles()
    {
        Store.ClaimDueAsync(default, default, default, default, default).ReturnsForAnyArgs(call =>
        {
            var claim = new ClaimCall(call.ArgAt<ChannelId>(0), call.ArgAt<int>(1), call.ArgAt<DateTime>(2), call.ArgAt<TimeSpan>(3));
            _claims.Writer.TryWrite(claim);
            return ValueTask.FromResult(NextResultOf(claim.Channel));
        });
        Store.RecordTryAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
        {
            _records.Writer.TryWrite(new RecordCall(call.ArgAt<OutgoingMessage>(0), call.ArgAt<PublishTry>(1), call.ArgAt<DateTime?>(2)));
            return ValueTask.FromResult<Error?>(null);
        });
    }

    public WaitRecordingTimeProvider Time { get; } = new();

    public IOutboxDispatchStore Store { get; } = Substitute.For<IOutboxDispatchStore>();

    public DateTime Now => Time.UtcNow;

    public void ClaimsGive(ChannelId channel, params ErrorOr<ClaimResult<OutgoingMessage>>[] results)
    {
        var queue = _results.GetOrAdd(channel, static _ => new ConcurrentQueue<ErrorOr<ClaimResult<OutgoingMessage>>>());
        foreach (var result in results)
        {
            queue.Enqueue(result);
        }
    }

    public Task<ClaimCall> NextClaimAsync() => _claims.Reader.ReadAsync().AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);

    public Task<RecordCall> NextRecordAsync() => _records.Reader.ReadAsync().AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System);

    public bool HasMoreClaims => _claims.Reader.TryPeek(out _);

    public bool HasMoreRecords => _records.Reader.TryPeek(out _);

    public ServiceProvider Build(Action<DispatchOptions>? dispatch = null, Action<MsgHatchBuilder>? more = null, params ITransport[] transports)
    {
        var services = TestHost.CreateServices(Time);
        var builder = services.AddMsgHatch(static o => o.EnableLogging = false)
            .AddTestChannel()
            .AddSecondTestChannel()
            .AddThirdTestChannel()
            .UseOutboxStore(static (_, _) => Substitute.For<IOutboxStore>())
            .UseOutboxDispatchStore(_ => Store)
            .ConfigureOutboxDispatch(o => dispatch?.Invoke(o));
        foreach (var transport in transports)
        {
            builder.UseTransport(_ => transport);
        }

        more?.Invoke(builder);
        return services.Build();
    }

    public static OutgoingMessage<TestEnvelope> AMessage(
        ChannelId? channel = null,
        int tryCount = 0,
        int maxTryCount = 8,
        DateTime? expiresAtUtc = null,
        DateTime? leaseUntilUtc = null,
        Guid? id = null,
        MessageFlags flags = MessageFlags.None,
        TenantId? tenant = null) =>
        TestMessages.AnOutgoingMessage(
            channel: channel ?? TestChannel.Id, flags: flags, tenant: tenant,
            delivery: TestMessages.ADeliveryState(id) with
            {
                TryCount = tryCount,
                MaxTryCount = maxTryCount,
                ExpiresAtUtc = expiresAtUtc,
                LeaseUntilUtc = leaseUntilUtc,
            });

    public static ErrorOr<ClaimResult<OutgoingMessage>> Claim(params OutgoingMessage[] messages) =>
        new ClaimResult<OutgoingMessage>(messages, NextDueAtUtc: null);

    public static ErrorOr<ClaimResult<OutgoingMessage>> ClaimWithNextDue(DateTime nextDueAtUtc, params OutgoingMessage[] messages) =>
        new ClaimResult<OutgoingMessage>(messages, nextDueAtUtc);

    private ErrorOr<ClaimResult<OutgoingMessage>> NextResultOf(ChannelId channel) =>
        _results.TryGetValue(channel, out var queue) && queue.TryDequeue(out var result)
            ? result
            : new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null);

    public readonly record struct ClaimCall(ChannelId Channel, int MaxCount, DateTime NowUtc, TimeSpan Lease);

    public readonly record struct RecordCall(OutgoingMessage Message, PublishTry Try, DateTime? NextTryAtUtc);
}
