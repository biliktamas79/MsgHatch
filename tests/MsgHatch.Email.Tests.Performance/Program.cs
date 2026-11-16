using BenchmarkDotNet.Running;
using MsgHatch.Email.Tests.Performance;

BenchmarkRunner.Run(typeof(PlaceholderTemplateEnginePerfTests).Assembly, args: args);
