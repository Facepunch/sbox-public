using Sandbox.Engine;
using Sandbox.Internal;

namespace Sandbox.MovieMaker;

#nullable enable

partial class TrackBinder
{
	/// <summary>
	/// Creates any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the given <paramref name="clip"/> to target.
	/// </summary>
	public IEnumerable<ITrackReference> CreateTargets( IMovieClip clip, GameObject? rootParent = null )
	{
		return CreateTargets( clip.Tracks.OfType<IReferenceTrack>(), rootParent );
	}

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
			.WithoutMapObjects()
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
				// Start disabled so we can remove unbound children before any scripts run

				var go = prefab.Clone( Transform.Zero, parentGo, name: goTrack.Name, startEnabled: false );

				BindCreatedTarget( target, go, createdTargets );
				RemoveUnboundTargets( go, goTrack, children, createdTargets );

				go.Enabled = true;
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
			var typeDesc = GlobalContext.Current.TypeLibrary.GetType( target.TargetType );
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

file static class ReferenceTrackExtensions
{
	extension( IEnumerable<IReferenceTrack> refTracks )
	{
		/// <summary>
		/// Skip tracks for objects that a <see cref="MapInstance"/> will create when it loads, along with
		/// everything inside them. Objects created at runtime and parented to the map instance's object
		/// are still included.
		/// </summary>
		/// <remarks>
		/// Only map instances whose track has <see cref="TrackFlags.MapObject"/> were recorded with it on the objects
		/// they created. For the others, like movies recorded before it existed, or legacy maps whose entities don't
		/// get it, we can't tell which objects the map creates, so skip everything inside them.
		/// </remarks>
		public IEnumerable<IReferenceTrack> WithoutMapObjects()
		{
			// Note: We're assuming we've ordered by depth, and we're assuming all
			// ancestors of tracks are included in the enumerable

			var refTrackArray = refTracks.ToArray();

			var untaggedMapObjects = refTrackArray
				.OfType<IReferenceTrack<MapInstance>>()
				.Where( x => !x.HasFlag( TrackFlags.MapObject ) )
				.Select( x => x.Parent )
				.OfType<IReferenceTrack>()
				.ToHashSet();

			var skipped = new HashSet<IReferenceTrack>();

			foreach ( var track in refTrackArray )
			{
				if ( IsMapCreated( track, untaggedMapObjects, skipped ) )
				{
					skipped.Add( track );
					continue;
				}

				yield return track;
			}
		}
	}

	private static bool IsMapCreated( IReferenceTrack track, HashSet<IReferenceTrack> untaggedMapObjects, HashSet<IReferenceTrack> skipped )
	{
		if ( track.Parent is { } parent && skipped.Contains( parent ) ) return true;

		// On a map instance, the flag means the objects it loaded have the flag, not that the map created it

		if ( track is IReferenceTrack<MapInstance> ) return false;
		if ( track is IReferenceTrack<GameObject> && track.HasFlag( TrackFlags.MapObject ) ) return true;

		return track.Parent is { } untagged && untaggedMapObjects.Contains( untagged );
	}

	private static bool HasFlag( this IReferenceTrack track, TrackFlags flag ) =>
		((track.Metadata?.Flags ?? TrackFlags.None) & flag) != 0;
}
