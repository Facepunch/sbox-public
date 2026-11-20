namespace Sandbox;

public interface IToolInfo : IValid
{
	string Name { get; }
	string Description { get; }

	/// <summary>
	/// The key hints to show, in order.
	/// </summary>
	IEnumerable<ToolHint> Hints => [];
}

/// <summary>
/// What pressing <paramref name="Actions"/> together does.
/// </summary>
public readonly record struct ToolHint( string Label, string[] Actions );
