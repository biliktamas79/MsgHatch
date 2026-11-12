using BenchmarkDotNet.Running;
using MsgHatch.Email.Tests.Performance;

BenchmarkRunner.Run(typeof(PlaceholderEmailEnginePerfTests).Assembly, args: args);
