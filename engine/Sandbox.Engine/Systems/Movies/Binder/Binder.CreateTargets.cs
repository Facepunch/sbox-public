using Sandbox.Internal;

namespace Sandbox.MovieMaker;

#nullable enable

partial class TrackBinder
{
	/// <summary>
	/// Creates any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given <paramref name="clip"/> to target.
	/// </summary>
	public IEnumerable<ITrackReference> CreateTargets( IMovieClip clip, GameObject? rootParent = null ) => CreateTargets( clip.Tracks.OfType<IReferenceTrack>(), rootParent );

	/// <summary>
	/// Creates any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given
	/// set of <paramref name="tracks"/> to target.
	/// </summary>
	public IEnumerable<ITrackReference> CreateTargets( IEnumerable<IReferenceTrack> tracks, GameObject? rootParent = null )
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

		var createdTargets = new List<ITrackReference>();

		foreach ( var track in allTracks )
		{
			CreateTarget( track, children, rootParent, createdTargets );
		}

		return createdTargets;
	}

	/// <summary>
	/// Create any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given track
	/// and its children to target.
	/// </summary>
	private void CreateTarget( IReferenceTrack track, IReadOnlyDictionary<IReferenceTrack<GameObject>, IReferenceTrack[]> children, GameObject? rootParent, List<ITrackReference> createdTargets )
	{
		var target = Get( track );
		var parentGo = target.Parent?.Value ?? rootParent;

		if ( parentGo is not null )
		{
			Assert.IsValid( parentGo );
			Assert.AreEqual( Scene, parentGo.Scene );
		}

		// Attempt auto-bind
		_ = target.Value;

		if ( TryGetBinding( target.Id, out _ ) ) return;

		if ( track is IReferenceTrack<GameObject> goTrack )
		{
			if ( goTrack.Metadata?.PrefabSource is { } prefabSource && GameObject.GetPrefab( prefabSource ) is { } prefab )
			{
				var go = prefab.Clone( Transform.Zero, parentGo, name: goTrack.Name );

				BindCreatedTarget( target, go, createdTargets );
				RemoveUnboundTargets( go, goTrack, children, createdTargets );
			}
			else
			{
				if ( goTrack.Metadata?.PrefabSource is { } missingSource )
				{
					Log.Warning( $"Unknown prefab \"{missingSource}\"" );
				}

				var go = new GameObject( parentGo, name: goTrack.Name );

				BindCreatedTarget( target, go, createdTargets );
			}
		}
		else if ( target.Parent is not null && parentGo is not null )
		{
			var typeDesc = GlobalGameNamespace.TypeLibrary.GetType( target.TargetType );
			if ( typeDesc is null ) return;

			var cmp = parentGo.Components.Create( typeDesc );

			BindCreatedTarget( target, cmp, createdTargets );
		}
	}

	/// <summary>
	/// We've created <paramref name="go"/> from a prefab, but we only want child objects / <see cref="Component"/>s
	/// that we have tracks for. Remove the rest here.
	/// </summary>
	private void RemoveUnboundTargets( GameObject go, IReferenceTrack<GameObject> track, IReadOnlyDictionary<IReferenceTrack<GameObject>, IReferenceTrack[]> children, List<ITrackReference> createdTargets )
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
				BindCreatedTarget( Get( match ), child, createdTargets );
				RemoveUnboundTargets( child, match, children, createdTargets );
			}
		}

		var visitedTracks = new HashSet<IReferenceTrack>();

		foreach ( var cmp in go.Components.GetAll().ToArray() )
		{
			var match = childTracks
				.Where( x => x.TargetType == cmp.GetType() )
				.FirstOrDefault( x => !visitedTracks.Contains( x ) );

			if ( match is null )
			{
				cmp.Destroy();
			}
			else
			{
				visitedTracks.Add( match );

				BindCreatedTarget( Get( match ), cmp, createdTargets );
			}
		}
	}

	/// <summary>
	/// We've created <paramref name="inst"/> during <see cref="CreateTargets(Sandbox.MovieMaker.IMovieClip,GameObject)"/>,
	/// bind it to the track reference it was created for and keep track of it for removal
	/// during <see cref="MoviePlayer.DestroyTargets()"/>.
	/// </summary>
	private void BindCreatedTarget( ITrackReference reference, IValid inst, List<ITrackReference> createdTargets )
	{
		reference.Bind( inst );
		createdTargets.Add( reference );
	}
}
