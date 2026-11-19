using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.Resources;

/// <summary>
/// A JSON definition of an embedded resource. This is a resource that can be either standalone (in a .vtex file) or 
/// embedded in a GameResource's Json data. 
/// 
/// When it's detected in a GameResource we will create the named compiler and create the resource. When compiling the
/// GameResource this can optionally create a compiled version of the resource on disk.
/// 
/// When we compile a regular resource that contains this $compiler structure, it operates like any other compile, except
/// it's totally managed by c# instead of resourcecompiler.
/// </summary>
[Expose]
public struct EmbeddedResource
{
	/// <summary>
	/// The name of the ResourceCompiler to use
	/// </summary>
	[JsonPropertyName( "$compiler" )]
	public string ResourceCompiler { get; set; }

	/// <summary>
	/// The name of the ResourceGenerator that created this resource. This is basically a sub-compiler.
	/// </summary>
	[JsonPropertyName( "$source" )]
	public string ResourceGenerator { get; set; }

	/// <summary>
	/// Sometimes we'll want to embed a child class of a resource
	/// </summary>
	[JsonPropertyName( $"$type" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string TypeName { get; set; }

	/// <summary>
	/// Data that is serialized/deserialized from the ResourceGenerator
	/// </summary>
	[JsonPropertyName( "data" )]
	public JsonObject Data { get; set; }

	/// <summary>
	/// If this resource has been compiled to disk then this is the path to that resource.
	/// This avoids the need to generate this resource again.
	/// </summary>
	[JsonPropertyName( "compiled" )]
	public string CompiledPath { get; set; }
}
