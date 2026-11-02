using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.Delivery;

/// <summary>The source-generated validator of <see cref="TestDeliveryOptions"/> (D64).</summary>
[OptionsValidator]
public sealed partial class TestDeliveryOptionsValidator : IValidateOptions<TestDeliveryOptions>;
