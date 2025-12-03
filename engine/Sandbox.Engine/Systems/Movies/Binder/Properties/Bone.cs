using System.Runtime.CompilerServices;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Pseudo-property on a <see cref="SkinnedModelRenderer"/> that has a sub-property for each bone.
/// Stores movie-driven transforms for each bone during playback, and applies them when
/// <see cref="MovieBoneAnimatorSystem"/> performs <see cref="GameObjectSystem.Stage.UpdateBones"/>.
/// </summary>
[Expose]
public sealed class BoneAccessor
{
	private readonly Dictionary<int, Transform> _parentSpaceOverrides = new();
	private readonly Dictionary<int, Transform> _localSpaceOverrides = new();
	private readonly SkinnedModelRenderer _renderer;

	/// <summary>
	/// Renderer this accessor was created for.
	/// </summary>
	public SkinnedModelRenderer Renderer => _renderer;

	public BoneAccessor( SkinnedModelRenderer renderer )
	{
		_renderer = renderer;
	}

	/// <summary>
	/// Helper to see if the renderer's model has a bone with the given <paramref name="name"/>.
	/// </summary>
	public bool HasBone( string name ) => _renderer.Model?.Bones.HasBone( name ) ?? false;

	/// <summary>
	/// Gets the current movie-driven parent-space transform of the given bone. If the bone
	/// isn't controlled by a movie, just returns the current parent-space transform.
	/// </summary>
	public Transform GetParentSpace( int index )
	{
		return _parentSpaceOverrides.TryGetValue( index, out var transform )
			? transform
			: Renderer.SceneModel?.GetParentSpaceBone( index ) ?? Transform.Zero;
	}

	/// <summary>
	/// Sets the current movie-driven parent-space transform of the given bone.
	/// </summary>
	public void SetParentSpace( int index, Transform value )
	{
		_parentSpaceOverrides[index] = value;
	}

	/// <summary>
	/// Clears any movie-driven bone transforms for this renderer.
	/// </summary>
	public void ClearOverrides()
	{
		_parentSpaceOverrides.Clear();
	}

	/// <summary>
	/// Applies any movie-driven bone transforms. Called during <see cref="GameObjectSystem.Stage.UpdateBones"/>.
	/// </summary>
	public void ApplyOverrides()
	{
		_renderer.ClearPhysicsBones();

		if ( _renderer.Model is not { } model ) return;
		if ( _renderer.SceneModel is not { } sceneModel ) return;
		if ( _parentSpaceOverrides.Count == 0 ) return;

		// TODO: I'm assuming parent bones are always listed before child bones

		_localSpaceOverrides.Clear();

		foreach ( var bone in model.Bones.AllBones )
		{
			if ( !_parentSpaceOverrides.TryGetValue( bone.Index, out var parentLocalTransform ) )
			{
				// Even if this bone doesn't have an override, one of its ancestors
				// might have so we need to update its local space transform

				parentLocalTransform = bone.Parent is { } parent
					? parent.LocalTransform.ToLocal( bone.LocalTransform )
					: bone.LocalTransform;
			}

			{
				var parentTransform = bone.Parent is { } parent
					? _localSpaceOverrides.TryGetValue( parent.Index, out var parentLocalOverride )
						? parentLocalOverride
						: sceneModel.GetBoneLocalTransform( parent.Index )
					: Transform.Zero;

				var localTransform = parentTransform.ToWorld( parentLocalTransform );

				_localSpaceOverrides[bone.Index] = localTransform;

				sceneModel.SetBoneOverride( bone.Index, localTransform );
			}
		}
	}
}

/// <summary>
/// Reads / writes a bone transform on a <see cref="SkinnedModelRenderer"/>.
/// </summary>
file sealed record BoneProperty( ITrackProperty<BoneAccessor?> Parent, string Name )
	: ITrackProperty<Transform>
{
	private (SkinnedModelRenderer? Renderer, int? Index)? _cached;

	public bool IsBound => Parent.Value?.HasBone( Name ) ?? false;

	public Transform Value
	{
		get => GetInfo().Index is not { } index ? Transform.Zero : Parent.Value?.GetParentSpace( index ) ?? Transform.Zero;
		set
		{
			if ( GetInfo().Index is { } index )
			{
				Parent.Value?.SetParentSpace( index, value );
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;

	private (SkinnedModelRenderer? Renderer, int? Index) GetInfo()
	{
		if ( _cached is { } cached && cached.Renderer == Parent.Value?.Renderer )
		{
			return cached;
		}

		var renderer = Parent.Value?.Renderer;
		var index = renderer?.Model?.Bones.GetBone( Name )?.Index;

		_cached = cached = (renderer, index);

		return cached;
	}
}

[Expose]
file sealed class BonePropertyFactory : ITrackPropertyFactory<ITrackProperty<BoneAccessor?>, Transform>
{
	/// <summary>
	/// Any property inside a <see cref="BoneAccessor"/> is a bone.
	/// </summary>
	public bool PropertyExists( ITrackProperty<BoneAccessor?> parent, string name ) => true;

	public ITrackProperty<Transform> CreateProperty( ITrackProperty<BoneAccessor?> parent, string name ) => new BoneProperty( parent, name );

	public IEnumerable<string> GetPropertyNames( ITrackProperty<BoneAccessor?> parent )
	{
		return parent is { IsBound: true, Value.Renderer.Model: { } model }
			? model.Bones.AllBones.Select( x => x.Name )
			: [];
	}

	public string GetCategoryName( ITrackProperty<BoneAccessor?> parent, string name )
	{
		var firstSep = name.IndexOf( '_' );
		var lastSep = name.LastIndexOf( '_' );

		if ( firstSep == lastSep ) return "Bones";

		return $"Bones/{name[..firstSep].ToTitleCase()}";
	}
}

file sealed record BoneAccessorProperty( ITrackReference<SkinnedModelRenderer> Parent )
	: ITrackProperty<BoneAccessor?>
{
	public const string PropertyName = "Bones";

	public string Name => PropertyName;

	public BoneAccessor? Value
	{
		get => Parent.Value is { } renderer
			? MovieBoneAnimatorSystem.Current?.GetBoneAccessor( renderer )
			: null;

		set
		{
			// Can't write (CanWrite = false)
		}
	}

	bool ITrackProperty.CanWrite => false;

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class BoneAccessorPropertyFactory : ITrackPropertyFactory<ITrackReference<SkinnedModelRenderer>, BoneAccessor?>
{
	public string GetCategoryName( ITrackReference<SkinnedModelRenderer> parent, string name ) => "Members";

	public IEnumerable<string> GetPropertyNames( ITrackReference<SkinnedModelRenderer> parent ) =>
		[BoneAccessorProperty.PropertyName];

	public bool PropertyExists( ITrackReference<SkinnedModelRenderer> parent, string name ) =>
		name == BoneAccessorProperty.PropertyName;

	public ITrackProperty<BoneAccessor?> CreateProperty( ITrackReference<SkinnedModelRenderer> parent, string name ) =>
		new BoneAccessorProperty( parent );
}

/// <summary>
/// Coordinates playing bone animations from <see cref="MoviePlayer"/>s. Holds a <see cref="BoneAccessor"/>
/// for <see cref="SkinnedModelRenderer"/>s in the scene, which store any movie-controlled bone transforms.
/// </summary>
[Expose]
public sealed class MovieBoneAnimatorSystem : GameObjectSystem<MovieBoneAnimatorSystem>
{
	private readonly ConditionalWeakTable<SkinnedModelRenderer, BoneAccessor> _accessors = new();

	public MovieBoneAnimatorSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.UpdateBones, -1_000, UpdateBones, "UpdateBones" );
	}

	/// <summary>
	/// Applies any active movie-driven bone transformations.
	/// </summary>
	public void UpdateBones()
	{
		foreach ( var (_, accessor) in _accessors )
		{
			accessor.ApplyOverrides();
		}
	}

	/// <summary>
	/// Clears all movie-driven bone transformations for the given <paramref name="renderer"/>.
	/// </summary>
	public void ClearBones( SkinnedModelRenderer renderer )
	{
		if ( _accessors.TryGetValue( renderer, out var accessor ) )
		{
			accessor.ClearOverrides();
		}
	}

	/// <summary>
	/// Gets the current movie-driven parent-space transform for the given bone. If this
	/// bone isn't currently being controlled by a movie, returns its current transform.
	/// </summary>
	public Transform GetParentSpaceBone( SkinnedModelRenderer renderer, int index )
	{
		return GetBoneAccessor( renderer ).GetParentSpace( index );
	}

	/// <summary>
	/// Sets the current movie-driven parent-space transform for the given bone.
	/// </summary>
	public void SetParentSpaceBone( SkinnedModelRenderer renderer, int index, Transform transform )
	{
		GetBoneAccessor( renderer ).SetParentSpace( index, transform );
	}

	internal BoneAccessor GetBoneAccessor( SkinnedModelRenderer renderer )
	{
		if ( _accessors.TryGetValue( renderer, out var existing ) ) return existing;

		existing = new BoneAccessor( renderer );
		_accessors.Add( renderer, existing );

		return existing;
	}
}
