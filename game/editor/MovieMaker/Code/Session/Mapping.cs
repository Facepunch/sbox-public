using System.Linq;
using System.Reflection;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable

partial class Session
{
	private T? GetTrackCore<T>( IValid obj, string name, GameObject? parent )
		where T : IProjectReferenceTrack
	{
		if ( parent is Scene )
		{
			parent = null;
		}

		var objType = obj.GetType();

		// First look for a track that is currently bound to the given object

		var bound = Project.Tracks
			.OfType<T>()
			.Where( x => x.TargetType == objType )
			.FirstOrDefault( x => Binder.Get( x ) is { IsBound: true } bound && ReferenceEquals( bound.Value, obj ) );

		if ( bound is not null ) return bound;

		// Next, look for an unbound track with the same name

		IEnumerable<T> availableTracks;

		if ( parent is null )
		{
			availableTracks = Project.RootTracks.OfType<T>();
			
		}
		else if( GetTrack( parent ) is { } parentTrack )
		{
			availableTracks = parentTrack.Children.OfType<T>();
		}
		else
		{
			return default;
		}

		return availableTracks
			.Where( x => x.TargetType == objType )
			.Where( x => x.Name == name )
			.FirstOrDefault( x => !Binder.Get( x ).IsBound );
	}

	public ProjectReferenceTrack<GameObject>? GetTrack( GameObject go ) =>
		GetTrackCore<ProjectReferenceTrack<GameObject>>( go, go.Name, go.Parent );

	public IProjectReferenceTrack? GetTrack( Component cmp ) =>
		GetTrackCore<IProjectReferenceTrack>( cmp, cmp.GetType().Name, cmp.GameObject );

	public IProjectTrack? GetTrack( GameObject go, string propertyPath )
	{
		return GetTrack( GetTrack( go ), propertyPath );
	}

	public IProjectTrack? GetTrack( Component cmp, string propertyPath )
	{
		return GetTrack( GetTrack( cmp ), propertyPath );
	}

	public IProjectTrack? GetTrack( IProjectTrack? parentTrack, string propertyPath )
	{
		while ( parentTrack is not null && propertyPath.Length > 0 )
		{
			var propertyName = propertyPath;

			// TODO: Hack for anim graph parameters including periods

			if ( parentTrack.TargetType != typeof( SkinnedModelRenderer.ParameterAccessor ) && propertyPath.IndexOf( '.' ) is var index and > -1 )
			{
				propertyName = propertyPath[..index];
				propertyPath = propertyPath[(index + 1)..];
			}
			else
			{
				propertyPath = string.Empty;
			}

			parentTrack = parentTrack.Children.FirstOrDefault( x => x.Name == propertyName );
		}

		return parentTrack;
	}

	public ProjectSequenceTrack? GetTrack( MovieResource resource )
	{
		return Project.Tracks
			.OfType<ProjectSequenceTrack>()
			.FirstOrDefault( x => x.Blocks.Any( y => y.Resource == resource ) );
	}

	public ProjectReferenceTrack<GameObject> GetOrCreateTrack( GameObject go )
	{
		if ( GetTrack( go ) is ProjectReferenceTrack<GameObject> existing )
		{
			// We might be re-using an existing unbound track, so should bind it here

			Binder.Get( existing ).Bind( go );

			return existing;
		}

		IProjectTrack? parentTrack = null;

		if ( go.Parent is { } parentGo and not Scene )
		{
			// Procedural bone objects need a parent track

			if ( (go.Flags & GameObjectFlags.Bone) != 0 )
			{
				parentTrack = GetOrCreateTrack( parentGo );
			}

			// Otherwise, if parent has a track, use it

			else
			{
				parentTrack = GetTrack( parentGo );
			}
		}

		var track = (ProjectReferenceTrack<GameObject>)Project.AddReferenceTrack( go.Name, typeof(GameObject), parentTrack );

		track.ReferenceId = go.Id;

		Binder.Get( track ).Bind( go );

		// If we have root tracks for child objects, parent them to the new track

		foreach ( var child in go.Children )
		{
			if ( GetTrack( child ) is IProjectTrackInternal { Parent: null } childTrack )
			{
				((IProjectTrackInternal)track).AddChild( childTrack );
			}
		}

		return track;
	}

	public IProjectReferenceTrack GetOrCreateTrack( Component cmp )
	{
		if ( GetTrack( cmp ) is IProjectReferenceTrack existing )
		{
			// We might be re-using an existing unbound track, so should bind it here

			Binder.Get( existing ).Bind( cmp );

			return existing;
		}

		// Nest component tracks inside the containing game object's track
		var goTrack = GetOrCreateTrack( cmp.GameObject );
		var track = Project.AddReferenceTrack( cmp.GetType().Name, cmp.GetType(), goTrack );

		track.ReferenceId = cmp.Id;

		Binder.Get( track ).Bind( cmp );

		return track;
	}

	public IProjectTrack GetOrCreateTrack( GameObject go, string propertyPath )
	{
		if ( GetTrack( go, propertyPath ) is { } existing ) return existing;

		// Nest property tracks inside the containing GameObject's track

		return GetOrCreateTrack( GetOrCreateTrack( go ), propertyPath );
	}

	public IProjectTrack GetOrCreateTrack( Component cmp, string propertyPath )
	{
		if ( GetTrack( cmp, propertyPath ) is { } existing ) return existing;

		// Nest property tracks inside the containing Component's track

		return GetOrCreateTrack( GetOrCreateTrack( cmp ), propertyPath );
	}

	public ProjectSequenceTrack GetOrCreateTrack( MovieResource resource )
	{
		if ( GetTrack( resource ) is { } existing ) return existing;

		return Project.AddSequenceTrack( $"{resource.ResourceName.ToTitleCase()} Sequence" );
	}

	public IProjectTrack GetOrCreateTrack( IProjectTrack parentTrack, string propertyPath )
	{
		while ( propertyPath.Length > 0 )
		{
			var propertyName = propertyPath;

			// TODO: Hack for anim graph parameters including periods

			if ( parentTrack.TargetType != typeof( SkinnedModelRenderer.ParameterAccessor ) && propertyPath.IndexOf( '.' ) is var index and > -1 )
			{
				propertyName = propertyPath[..index];
				propertyPath = propertyPath[(index + 1)..];
			}
			else
			{
				propertyPath = string.Empty;
			}

			parentTrack = GetOrCreateTrackCore( parentTrack, propertyName );
		}

		return parentTrack;
	}

	/// <summary>
	/// Create a track hierarchy matching the given <paramref name="preset"/>, rooted on <paramref name="rootTrack"/>.
	/// </summary>
	public void LoadPreset( IProjectTrack rootTrack, ITrackTarget rootTarget, TrackPresetNode preset )
	{
		if ( preset.AllChildren )
		{
			foreach ( var (name, _, _) in TrackProperty.GetAll( rootTarget ) )
			{
				GetOrCreateTrack( rootTrack, name );
			}

			return;
		}

		foreach ( var childPreset in preset.Children )
		{
			if ( GetOrCreatePresetTrackCore( rootTrack, rootTarget, childPreset ) is { } childTrack )
			{
				var childTarget = Binder.Get( childTrack );

				LoadPreset( childTrack, childTarget, childPreset );
			}
		}
	}

	public void RemovePreset( IProjectTrack rootTrack, ITrackTarget rootTarget, TrackPresetNode preset )
	{
		if ( preset.AllChildren )
		{
			foreach ( var childTrack in rootTrack.Children.ToArray() )
			{
				if ( childTrack.IsEmpty )
				{
					childTrack.Remove();
				}
			}

			return;
		}

		foreach ( var childPreset in preset.Children )
		{
			if ( rootTrack.Children.FirstOrDefault( x => x.Name == childPreset.PropertyName ) is not { } childTrack ) continue;
			if ( !childTrack.TargetType.IsAssignableTo( childPreset.PropertyType ) ) continue;

			RemovePreset( childTrack, Binder.Get( childTrack ), childPreset );

			if ( childTrack.IsEmpty )
			{
				childTrack.Remove();
			}
		}
	}

	private IProjectTrack? GetOrCreatePresetTrackCore( IProjectTrack rootTrack, ITrackTarget rootTarget, TrackPresetNode childPreset )
	{
		if ( rootTarget is not ITrackReference<GameObject> { Value: { } rootGameObject } )
		{
			return GetOrCreateTrack( rootTrack, childPreset.PropertyName );
		}

		if ( childPreset.PropertyType == typeof( GameObject ) )
		{
			var child = rootGameObject.Children.FirstOrDefault( x => x.Name == childPreset.PropertyName );

			return child is null ? null : GetOrCreateTrack( child );
		}
			
		if ( childPreset.PropertyType.IsAssignableTo( typeof( Component ) ) )
		{
			var component = rootGameObject.Components.FirstOrDefault( childPreset.PropertyType.IsInstanceOfType );

			return component is null ? null : GetOrCreateTrack( component );
		}

		return GetOrCreateTrack( rootTrack, childPreset.PropertyName );
	}

	private IProjectTrack GetOrCreateTrackCore( IProjectTrack parentTrack, string propertyName )
	{
		if ( parentTrack.Children.FirstOrDefault( x => x.Name == propertyName ) is { } existingTrack )
		{
			return existingTrack;
		}

		if ( Binder.Get( parentTrack ) is not { } parentProperty )
		{
			throw new Exception( "Parent track not registered." );
		}

		if ( TrackProperty.Create( parentProperty, propertyName ) is { } property )
		{
			return Project.AddPropertyTrack( property.Name, property.TargetType, parentTrack );
		}

		if ( parentTrack is ProjectReferenceTrack<GameObject> && TypeLibrary.GetType<Component>( propertyName ) is { TargetType: { } componentType } )
		{
			return Project.AddReferenceTrack( propertyName, componentType, parentTrack );
		}

		throw new Exception( $"Unknown property \"{propertyName}\" in type \"{parentProperty.TargetType}\"." );
	}

	private readonly HashSet<SkinnedModelRenderer> _controlledSkinnedModelRenderers = new();

	private IEnumerable<T> GetControlled<T>()
		where T : Component
	{
		// When rendering, the whole scene is considered part of the movie.
		// Otherwise, we're just previewing playback in the editor, so only consider
		// stuff we've explicitly bound to this movie.

		return Renderer.IsRendering
			? Player.Scene.GetAll<T>()
			: Binder.GetComponents<T>( Project );
	}

	/// <summary>
	/// Advance all bound <see cref="SkinnedModelRenderer"/>s by the given <paramref name="deltaTime"/>.
	/// </summary>
	public void AdvanceAnimations( MovieTime deltaTime )
	{
		// Negative deltas aren't supported :(

		var dt = Math.Min( (float)deltaTime.Absolute.TotalSeconds, 1f );

		Time.Delta = dt;

		using var sceneScope = Player.Scene.Push();

		_controlledSkinnedModelRenderers.Clear();

		foreach ( var controller in GetControlled<PlayerController>() )
		{
			controller.MovieEditorFixedUpdate();

			if ( controller.Renderer is not { } renderer ) continue;

			_controlledSkinnedModelRenderers.Add( renderer );

			controller.UpdateAnimation( renderer );
		}

		foreach ( var renderer in GetControlled<SkinnedModelRenderer>() )
		{
			_controlledSkinnedModelRenderers.Add( renderer );
		}

		foreach ( var renderer in _controlledSkinnedModelRenderers )
		{
			UpdateAnimationPlaybackRate( renderer, dt );
		}
	}

	private void UpdateAnimationPlaybackRate( SkinnedModelRenderer renderer, float dt )
	{
		if ( renderer.SceneModel is not { } model ) return;

		if ( dt > 0f && IsEditorScene )
		{
			model.PlaybackRate = renderer.PlaybackRate;
			model.Update( dt );
		}

		model.PlaybackRate = IsEditorScene ? 0f : 1f;
	}
}

file static class PlayerControllerExtensions
{
	private static Action<PlayerController> UpdateHeadroom { get; } = typeof( PlayerController )
		.GetMethod( nameof( UpdateHeadroom ), BindingFlags.Instance | BindingFlags.NonPublic )!
		.CreateDelegate<Action<PlayerController>>();

	private static Action<PlayerController> UpdateFalling { get; } = typeof( PlayerController )
		.GetMethod( nameof( UpdateFalling ), BindingFlags.Instance | BindingFlags.NonPublic )!
		.CreateDelegate<Action<PlayerController>>();

	public static void MovieEditorFixedUpdate( this PlayerController controller )
	{
		IScenePhysicsEvents physicsEvents = controller;

		physicsEvents.PrePhysicsStep();
		physicsEvents.PostPhysicsStep();

		UpdateHeadroom( controller );
		UpdateFalling( controller );
	}
}
