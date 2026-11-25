namespace Sandbox;

/// <summary>
/// Commits slider edits when the drag ends or the numeric entry loses focus.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class ApplyOnReleaseAttribute : Attribute
{
}
