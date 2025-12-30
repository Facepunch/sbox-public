using System.ComponentModel;

namespace Sandbox;

/// <summary>
/// Should be applied to a class that inherits from <see cref="GameResource"/>.
/// Makes the class able to be stored as an asset on disk.
/// </summary>
[AttributeUsage( AttributeTargets.Class )]
public class AssetTypeAttribute : System.Attribute, ITypeAttribute, IUninheritable
{
	/// <summary>
	/// This gets filled in by the TypeLibrary when the class is registered, it shouldn't be changed manually.
	/// </summary>
	[EditorBrowsable( EditorBrowsableState.Never )]
	public System.Type TargetType { get; set; }

	/// <summary>
	/// The title of this game resource.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// File extension for this game resource.
	/// </summary>
	public string Extension { get; set; }

	/// <summary>
	/// Category of this game resource, for grouping in UI.
	/// </summary>
	public string Category { get; set; } = "Other";

	/// <summary>
	/// Flags for this asset type.
	/// </summary>
	public AssetTypeFlags Flags { get; set; }
}

/// <summary>
/// Flags for <see cref="AssetTypeAttribute"/>
/// </summary>
public enum AssetTypeFlags
{
	None = 0,

	/// <summary>
	/// If set then this resource cannot be embedded. This means that in the editor
	/// it can only really exist as an asset file on disk, not inside another asset.
	/// </summary>
	NoEmbedding = 1 << 0,

	/// <summary>
	/// Include thumbnails when publishing as part of another package
	/// </summary>
	IncludeThumbnails = 1 << 1,
}

[Obsolete( "Use AssetType instead" )]
public class GameResourceAttribute : AssetTypeAttribute
{
	/// <summary>
	/// Icon to be used for this asset
	/// Can be an absolute path of a PNG
	/// Or a <a href="https://fonts.google.com/icons">material icon</a> for this game resource's thumbnail.
	/// </summary>
	public string Icon { get; set; } = "question_mark";

	/// <summary>
	/// Background color for this resource's thumbnail.
	/// </summary>
	public string IconBgColor { get; set; } = "#67ac5c";

	/// <summary>
	/// Foreground color (icon color) for this resource's thumbnail.
	/// </summary>
	public string IconFgColor { get; set; } = "#1a2c17";

	/// <summary>
	/// Can this GameResource be an embedded resource?
	/// Allows the ability to edit a resource inline instead of saving it to a specific file.
	/// </summary>
	public bool CanEmbed
	{
		get => (Flags & AssetTypeFlags.NoEmbedding) == 0;
		set => Flags = value ? (Flags & ~AssetTypeFlags.NoEmbedding) : (Flags | AssetTypeFlags.NoEmbedding);
	}

	/// <summary>
	/// Description of this game resource. This is obsolete, we'll use the xml summary description.
	/// </summary>
	[Obsolete]
	public string Description { get; set; }

	public GameResourceAttribute( string title, string extension, string description )
	{
		if ( extension.Length > 8 )
		{
			Log.Error( $"Resource extensions should be under 8 characters ({TargetType})" );
			extension = extension.Substring( 0, 8 );
		}

		if ( !extension.All( x => char.IsLetter( x ) ) )
		{
			Log.Error( $"Resource extensions can only contain letters ({TargetType})" );
			extension = "errored";
		}

		Name = title;
		Description = description;
		Extension = extension;
	}
}
