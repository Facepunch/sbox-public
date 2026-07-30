namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<BuilderBone> bones = [];

	private readonly record struct BuilderBone( string Name, string ParentName, Vector3 Position, Rotation Rotation, float Radius, bool Attachment );

	private void ApplyBones( NativeEngine.ModelBuilder modelBuilder )
	{
		foreach ( var bone in bones )
		{
			if ( bone.Attachment )
				modelBuilder.AddRunTimeAttachment( bone.Name, bone.ParentName, bone.Position, bone.Rotation );
			else
				modelBuilder.AddRunTimeBone( bone.Name, bone.ParentName, bone.Radius, bone.Position, bone.Rotation );
		}
	}

	/// <summary>
	/// A bone definition for use with <see cref="ModelBuilder"/>.
	/// </summary>
	/// <param name="Name">Name of the bone.</param>
	/// <param name="ParentName">Name of the parent bone, or empty for a root bone.</param>
	/// <param name="Position">Position of the bone, relative to its parent.</param>
	/// <param name="Rotation">Rotation of the bone, relative to its parent.</param>
	public readonly record struct Bone( string Name, string ParentName, Vector3 Position, Rotation Rotation );

	/// <summary>
	/// Adds a bone to the skeleton.
	/// </summary>
	/// <param name="bone">The bone to add.</param>
	public void AddBone( Bone bone )
	{
		AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
	}

	/// <summary>
	/// Adds multiple bones to the skeleton.
	/// </summary>
	/// <param name="bones">The bones to add.</param>
	public void AddBones( Bone[] bones )
	{
		if ( bones == null )
			return;

		foreach ( var bone in bones )
			AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
	}

	/// <summary>
	/// Adds a bone to the skeleton.
	/// </summary>
	/// <param name="name">The bone name.</param>
	/// <param name="position">The position relative to the parent bone.</param>
	/// <param name="rotation">The rotation relative to the parent bone.</param>
	/// <param name="parentName">The parent bone name, or null for a root bone.</param>
	public ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName = null )
	{
		return AddBone( name, position, rotation, parentName, false );
	}

	/// <summary>
	/// Adds a named attachment point to the skeleton.
	/// </summary>
	/// <param name="name">The attachment name.</param>
	/// <param name="position">The position relative to the parent bone.</param>
	/// <param name="rotation">The rotation relative to the parent bone.</param>
	/// <param name="parentName">The parent bone name, or null for a model-space attachment.</param>
	public ModelBuilder AddAttachment( string name, Vector3 position, Rotation rotation, string parentName = null )
	{
		return AddBone( name, position, rotation, parentName, true );
	}

	internal ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName, bool attachment )
	{
		bones.Add( new BuilderBone(
			string.IsNullOrWhiteSpace( name ) ? string.Empty : name,
			string.IsNullOrWhiteSpace( parentName ) ? string.Empty : parentName,
			position,
			rotation,
			-1,
			attachment ) );

		return this;
	}
}
