using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The settings of the dispatch loops of the inbox processor or of the outbox dispatcher (section 4.9). The name of
/// the options is <c>Inbox</c> or <c>Outbox</c>.
/// </summary>
public sealed class DispatchOptions : IValidatableObject
{
    /// <summary>
    /// The key of the section under the root of <c>AddMsgHatch</c> (D329). The section of a name is
    /// <c>{root}:Dispatch:{Name}</c>, by default <c>MsgHatch:Dispatch:{Name}</c>.
    /// </summary>
    public const string SectionKey = "Dispatch";

    /// <summary>The number of send workers of each dispatch loop.</summary>
    [Range(1, 256)]
    public int MaxDegreeOfParallelism { get; set; } = 8;

    /// <summary>The capacity of the work queue of each dispatch loop. It is at least <see cref="MaxDegreeOfParallelism"/>.</summary>
    [Range(1, 4096)]
    public int QueueCapacity { get; set; } = 16;

    /// <summary>The duration of the lease of a claimed message (D103).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:05", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// A worker skips a message whose lease ends within this time, so that no other instance sends it too. It is less
    /// than <see cref="Lease"/>.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:00", "00:30:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan LeaseSafetyMargin { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The first poll delay after an empty claim.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:00.010", "00:10:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest poll delay. It is not less than <see cref="PollInterval"/>.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The source-generated validator checks this rule with its own copy of the attribute, with no reflection.")]
    [Range(typeof(TimeSpan), "00:00:00.010", "01:00:00", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Polls the store when no signal and no due time wakes a loop. Set it to false only where a signal reaches each
    /// loop, for example in one process (D102, D103).
    /// </summary>
    public bool EnablePolling { get; set; } = true;

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (QueueCapacity < MaxDegreeOfParallelism)
        {
            yield return new ValidationResult("QueueCapacity is at least MaxDegreeOfParallelism.", [nameof(QueueCapacity)]);
        }

        if (LeaseSafetyMargin >= Lease)
        {
            yield return new ValidationResult("LeaseSafetyMargin is less than Lease.", [nameof(LeaseSafetyMargin)]);
        }

        if (MaxPollInterval < PollInterval)
        {
            yield return new ValidationResult("MaxPollInterval is not less than PollInterval.", [nameof(MaxPollInterval)]);
        }
    }
}
