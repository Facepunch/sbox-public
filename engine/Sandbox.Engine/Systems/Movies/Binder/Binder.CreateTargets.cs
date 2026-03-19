using Sandbox.Internal;

namespace Sandbox.MovieMaker;

#nullable enable

partial class TrackBinder
{
	private readonly record struct CreatedTarget( ITrackReference Reference, IValid Value );

	private readonly List<CreatedTarget> _createdTargets = new();

	/// <summary>
	/// Creates any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given <paramref name="clip"/> to target.
	/// </summary>
	public void CreateTargets( IMovieClip clip, bool replace = true, GameObject? rootParent = null ) => CreateTargets( clip.Tracks.OfType<IReferenceTrack>(), replace, rootParent );

	/// <summary>
	/// Creates any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given
	/// set of <paramref name="tracks"/> to target.
	/// </summary>
	public void CreateTargets( IEnumerable<IReferenceTrack> tracks, bool replace = true, GameObject? rootParent = null )
	{
		// Make sure GameObjects get created in this scene

		using var sceneScope = Scene.Push();

		var allTracks = tracks
			.OrderBy( x => x.GetDepth() )
			.ThenBy( x => x.Id )
			.ToArray();

		var children = allTracks
			.Where( x => x.Parent is not null )
			.GroupBy( x => x.Parent! )
			.ToDictionary( x => x.Key, x => x.ToArray() );

		foreach ( var track in allTracks )
		{
			CreateTarget( track, children, rootParent );
		}

		if ( !replace ) return;

		// We're replacing, destroy any old targets that aren't still referenced

		var usedTargets = allTracks
			.Select( x => Get( x ).Value as IValid )
			.Where( x => x.IsValid() )
			.ToHashSet();

		var unusedTargets = _createdTargets
			.Where( x => !usedTargets.Contains( x.Value ) )
			.ToHashSet();

		DestroyTargets( unusedTargets );

		_createdTargets.RemoveAll( unusedTargets.Contains );
	}

	/// <summary>
	/// Destroy any instances created by <see cref="CreateTargets(Sandbox.MovieMaker.IMovieClip,bool,GameObject)"/>.
	/// </summary>
	public void DestroyTargets()
	{
		DestroyTargets( _createdTargets );
		_createdTargets.Clear();
	}

	private static void DestroyTargets( IEnumerable<CreatedTarget> targets )
	{
		foreach ( var (reference, value) in targets )
		{
			reference.Reset();

			if ( !value.IsValid() ) continue;

			switch ( value )
			{
				case Component cmp:
					cmp.Destroy();
					break;

				case GameObject go:
					go.Destroy();
					break;
			}
		}
	}

	private static GameObjectFlags CreatedTargetGameObjectFlags => GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
	private static ComponentFlags CreatedTargetComponentFlags => ComponentFlags.NotSaved | ComponentFlags.NotNetworked;

	/// <summary>
	/// Create any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given track
	/// and its children to target.
	/// </summary>
	private void CreateTarget( IReferenceTrack track, IReadOnlyDictionary<IReferenceTrack<GameObject>, IReferenceTrack[]> children, GameObject? rootParent = null )
	{
		var target = Get( track );
		var parentGo = target.Parent?.Value ?? rootParent;

		if ( parentGo is not null )
		{
			Assert.IsValid( parentGo );
			Assert.AreEqual( Scene, parentGo.Scene );
		}

		if ( track is IReferenceTrack<GameObject> goTrack && target is ITrackReference<GameObject> { IsBound: false } goRef )
		{
			if ( goTrack.Metadata?.PrefabSource is not { } prefabSource )
			{
				var go = new GameObject( parentGo, name: goTrack.Name );

				go.Flags |= CreatedTargetGameObjectFlags;

				BindCreatedTarget( goRef, go );
			}
			else
			{
				var prefab = GameObject.GetPrefab( prefabSource );

				if ( prefab is null )
				{
					Log.Warning( $"Unknown prefab \"{prefabSource}\"" );
					return;
				}

				var go = GameObject.GetPrefab( prefabSource ).Clone( Transform.Zero, parentGo, name: goTrack.Name );

				go.Flags |= CreatedTargetGameObjectFlags;

				BindCreatedTarget( goRef, go );
				RemoveUnboundTargets( go, goTrack, children );
			}
		}
		else if ( target.Parent is not null && parentGo is not null && target is { IsBound: false } cmpRef )
		{
			var typeDesc = GlobalGameNamespace.TypeLibrary.GetType( target.TargetType );
			if ( typeDesc is null ) return;

			var cmp = parentGo.Components.Create( typeDesc );

			cmp.Flags |= CreatedTargetComponentFlags;

			BindCreatedTarget( cmpRef, cmp );
		}
	}

	/// <summary>
	/// We've created <paramref name="go"/> from a prefab, but we only want child objects / <see cref="Component"/>s
	/// that we have tracks for. Remove the rest here.
	/// </summary>
	private void RemoveUnboundTargets( GameObject go, IReferenceTrack<GameObject> track, IReadOnlyDictionary<IReferenceTrack<GameObject>, IReferenceTrack[]> children )
	{
		var childTracks = children.GetValueOrDefault( track, [] );

		foreach ( var child in go.Children.ToArray() )
		{
			var match = childTracks
				.OfType<IReferenceTrack<GameObject>>()
				.FirstOrDefault( x => x.Name == child.Name );

			if ( match is null )
			{
				child.Destroy();
			}
			else
			{
				child.Flags |= CreatedTargetGameObjectFlags;

				BindCreatedTarget( Get( match ), child );
				RemoveUnboundTargets( child, match, children );
			}
		}

		foreach ( var cmp in go.Components.GetAll().ToArray() )
		{
			var match = childTracks
				.FirstOrDefault( x => x.TargetType == cmp.GetType() );

			if ( match is null )
			{
				cmp.Destroy();
			}
			else
			{
				cmp.Flags |= CreatedTargetComponentFlags;

				BindCreatedTarget( Get( match ), cmp );
			}
		}
	}

	/// <summary>
	/// We've created <paramref name="inst"/> during <see cref="CreateTargets(Sandbox.MovieMaker.IMovieClip,bool,GameObject)"/>,
	/// bind it to the track reference it was created for and keep track of it for removal
	/// during <see cref="DestroyTargets()"/>.
	/// </summary>
	private void BindCreatedTarget( ITrackReference reference, IValid inst )
	{
		reference.Bind( inst );
		_createdTargets.Add( new CreatedTarget( reference, inst ) );
	}
}
