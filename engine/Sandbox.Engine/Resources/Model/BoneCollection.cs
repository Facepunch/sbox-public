using System;

namespace Sandbox;

/// <summary>
/// A collection of bones. This could be from a model, or an entity
/// </summary>
[Expose]
public abstract class BoneCollection
{
	/// <summary>
	/// Root bone of the model.
	/// </summary>
	public abstract Bone Root { get; }

	/// <summary>
	/// List of all bones of our object.
	/// </summary>
	public abstract IReadOnlyList<Bone> AllBones { get; }

	/// <summary>
	/// Whether the model or entity has a given bone by name.
	/// </summary>
	[Pure]
	public bool HasBone( string name )
	{
		return GetBone( name ) is not null;
	}

	/// <summary>
	/// Retrieve a bone by name.
	/// </summary>
	[Pure]
	public Bone GetBone( string name )
	{
		var bones = AllBones;
		for ( int i = 0; i < bones.Count; i++ )
		{
			if ( bones[i].IsNamed( name ) )
				return bones[i];
		}

		return null;
	}

	/// <summary>
	/// A bone in a <see cref="BoneCollection"/>.
	/// </summary>
	[Expose]
	public abstract class Bone
	{
		internal Bone _parent;
		internal List<Bone> _children;

		/// <summary>
		/// Numerical index of this bone.
		/// </summary>
		public virtual int Index { get; }

		/// <summary>
		/// Name of this bone.
		/// </summary>
		public virtual string Name { get; }

		/// <summary>
		/// The parent bone.
		/// </summary>
		public virtual Bone Parent => _parent;

		/// <summary>
		/// Transform on this bone, relative to the root bone.
		/// </summary>
		public virtual Transform LocalTransform { get; }

		/// <summary>
		/// Whether this bone has any child bones.
		/// </summary>
		public virtual bool HasChildren => _children != null;

		/// <summary>
		/// List of all bones that descend from this bone.
		/// </summary>
		public IReadOnlyList<Bone> Children => _children?.AsReadOnly() ?? Array.Empty<Bone>().AsReadOnly();

		internal void SetParent( Bone bone )
		{
			_parent = bone;
			bone.AddChild( this );
		}

		internal void AddChild( Bone child )
		{
			_children ??= new List<Bone>();
			_children.Add( child );
		}

		/// <summary>
		/// Whether this bone has given name or not.
		/// </summary>
		[Pure]
		public bool IsNamed( string name )
		{
			return string.Compare( Name, name, true ) == 0;
		}
	}
}
