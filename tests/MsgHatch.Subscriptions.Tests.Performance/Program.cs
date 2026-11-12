using BenchmarkDotNet.Running;
using MsgHatch.Subscriptions.Tests.Performance;

BenchmarkRunner.Run(typeof(SubscriptionResolverPerfTests).Assembly, args: args);
