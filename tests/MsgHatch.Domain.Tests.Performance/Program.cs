using BenchmarkDotNet.Running;
using MsgHatch.Domain.Tests.Performance;

BenchmarkRunner.Run(typeof(DeterministicIdPerfTests).Assembly, args: args);
