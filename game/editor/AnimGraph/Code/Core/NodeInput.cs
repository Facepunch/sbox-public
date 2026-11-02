using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

/// <summary>
/// Represents a connection from an input plug to a specific output on another node.
/// Stored as the value of [Input] properties on nodes.
/// </summary>
public struct NodeInput : IValid
{
	/// <summary>
	/// The identifier (GUID) of the source node.
	/// </summary>
	[Hide, Browsable( false )]
	public string Identifier { get; set; }

	/// <summary>
	/// The name of the output plug on the source node.
	/// </summary>
	[Hide, Browsable( false )]
	public string Output { get; set; }

	[Browsable( false ), JsonIgnore, Hide]
	public readonly bool IsValid => !string.IsNullOrWhiteSpace( Identifier ) && !string.IsNullOrWhiteSpace( Output );

	public NodeInput()
	{
		Identifier = "";
		Output = "";
	}

	public override readonly string ToString() => IsValid ? $"{Identifier}.{Output}" : "null";

	public static bool operator ==( NodeInput a, NodeInput b ) => a.Identifier == b.Identifier && a.Output == b.Output;
	public static bool operator !=( NodeInput a, NodeInput b ) => !(a == b);
	public override readonly bool Equals( object obj ) => obj is NodeInput input && this == input;
	public override readonly int GetHashCode() => HashCode.Combine( Identifier, Output );
}
