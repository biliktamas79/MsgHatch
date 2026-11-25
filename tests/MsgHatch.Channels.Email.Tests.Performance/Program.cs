using BenchmarkDotNet.Running;
using MsgHatch.Channels.Email.Tests.Performance;

BenchmarkRunner.Run(typeof(PlaceholderTemplateEnginePerfTests).Assembly, args: args);
