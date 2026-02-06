using System.Text.Json.Serialization;

namespace Sandbox;

#nullable enable

/// <summary>
/// A serialized reference to a data file (like navdata) that needs to be tracked and packaged.
/// These files have no dependencies themselves.
/// </summary>
internal readonly struct FileReference
{
	public const string ReferenceTypeName = "filereference";

	public static FileReference FromPath( string path ) => new( ReferenceTypeName, path );

	[JsonPropertyName( "$reference_type" )]
	public string ReferenceType { get; }

	[JsonPropertyName( "path" )]
	public string Path { get; }

	[JsonConstructor]
	private FileReference( string referenceType, string path )
	{
		ReferenceType = referenceType;
		Path = path;
	}

	public override string ToString() => Path;

	public static implicit operator string( FileReference reference ) => reference.Path;
}
