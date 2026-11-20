/// <summary>
/// A reusable property attribute that stores a string tag for resource picker filtering.
/// The editor can read this attribute to filter GameResource assets by matching metadata.
/// </summary>
[AttributeUsage( AttributeTargets.Property, AllowMultiple = false )]
public class MetadataAttribute : Attribute
{
	public string Tag { get; }

	public MetadataAttribute( string tag )
	{
		Tag = tag;
	}
}
