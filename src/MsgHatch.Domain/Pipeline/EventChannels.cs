using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The channels of the checked routes of one event, in the order of their first route, with the route count of each
/// channel (D280). The first channel needs no list, so an event on one channel allocates nothing here.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal struct EventChannels<TEvent>
{
    private IChannelRegistration<TEvent>? _first;
    private int _firstCount;
    private List<(IChannelRegistration<TEvent> Channel, int Count)>? _others;

    /// <summary>The number of channels.</summary>
    public readonly int Count => _first is null ? 0 : 1 + (_others?.Count ?? 0);

    /// <summary>The number of checked routes on all channels.</summary>
    public int RouteCount { get; private set; }

    /// <summary>One channel with its route count, in the order of the first route of each channel.</summary>
    public readonly (IChannelRegistration<TEvent> Channel, int Count) this[int index] =>
        index == 0 ? (_first!, _firstCount) : _others![index - 1];

    /// <summary>True when all <paramref name="routeCount"/> routes of the router passed the check and are on one channel.</summary>
    public readonly bool HasOneChannelFor(int routeCount) => _others is null && _first is not null && _firstCount == routeCount;

    /// <summary>Counts one checked route of <paramref name="channel"/>.</summary>
    public void Add(IChannelRegistration<TEvent> channel)
    {
        RouteCount++;
        if (_first is null || ReferenceEquals(_first, channel))
        {
            _first = channel;
            _firstCount++;
            return;
        }

        _others ??= [];
        for (var index = 0; index < _others.Count; index++)
        {
            var (other, count) = _others[index];
            if (ReferenceEquals(other, channel))
            {
                _others[index] = (channel, count + 1);
                return;
            }
        }

        _others.Add((channel, 1));
    }
}
