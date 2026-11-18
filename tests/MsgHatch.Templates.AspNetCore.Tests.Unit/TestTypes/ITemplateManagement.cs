using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AspNetCore.Tests.Unit.TestTypes;

/// <summary>Both management interfaces in one type, so that one substitute records the calls of each route (D231).</summary>
public interface ITemplateManagement : ITemplateQueries, ITemplateCommands;
