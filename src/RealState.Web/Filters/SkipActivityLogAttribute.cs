namespace RealState.Web.Filters;

/// <summary>Excludes an action / controller from <see cref="ActivityLogFilter"/> (e.g. navigation bookkeeping such as
/// recording a visited page or starring a favorite — not business actions worth auditing).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipActivityLogAttribute : Attribute { }
