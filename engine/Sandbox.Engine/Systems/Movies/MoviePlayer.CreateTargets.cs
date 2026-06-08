namespace Sandbox.MovieMaker;

#nullable enable

partial class MoviePlayer
{
	private readonly HashSet<Guid> _createdTargets = new();

	/// <summary>
	/// If true, creates any missing <see cref="GameObject"/>s and <see cref="Component"/>s for the
	/// current movie to target.
	/// </summary>
	[Property, Group( "Playback" )]
	public bool CreateTargets
	{
		get;
		set
		{
			field = value;
			UpdatePosition();
		}
	}

	internal GameObject? CreatedTargetsRoot { get; private set; }

	public bool IsCreatedTarget( IValid? obj )
	{
		if ( !obj.IsValid() ) return false;
		if ( !CreatedTargetsRoot.IsValid() ) return false;

		return obj switch
		{
			Component cmp => cmp.GameObject.IsAncestor( CreatedTargetsRoot ),
			GameObject go => go.IsAncestor( CreatedTargetsRoot ),
			_ => false
		};
	}

	/// <summary>
	/// Forces the creation of any missing <see cref="GameObject"/>s or <see cref="Component"/>s for the current <see cref="Clip"/> to target.
	/// </summary>
	public void UpdateTargets()
	{
		UpdateTargets( CreateTargets ? Clip : null, force: true );
	}

	private IMovieClip? _targetSource;

	private void UpdateTargets( IMovieClip? clip, bool force = false )
	{
		if ( !force && _targetSource == clip ) return;

		DestroyUnusedTargets( clip );

		_targetSource = clip;

		if ( clip is null ) return;

		CreatedTargetsRoot ??= new GameObject( GameObject, name: "Auto-Created Targets" );
		CreatedTargetsRoot.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;

		foreach ( var trackRef in Binder.CreateTargets( clip, rootParent: CreatedTargetsRoot ) )
		{
			_createdTargets.Add( trackRef.Id );
		}
	}

	private void DestroyUnusedTargets( IMovieClip? clip )
	{
		if ( clip is null )
		{
			DestroyTargets();
			return;
		}

		var usedTargets = new HashSet<Guid>();

		foreach ( var track in clip.Tracks )
		{
			if ( track is not IReferenceTrack refTrack ) continue;

			usedTargets.Add( refTrack.Id );
		}

		DestroyTargets( _createdTargets
			.Where( x => !usedTargets.Contains( x ) ) );

		_createdTargets.RemoveWhere( x => !usedTargets.Contains( x ) || !Binder.TryGetBinding( x, out var value ) || !IsCreatedTarget( value ) );

		if ( _createdTargets.Count != 0 ) return;

		CreatedTargetsRoot?.Destroy();
		CreatedTargetsRoot = null;
	}

	private void DestroyTargets()
	{
		DestroyTargets( _createdTargets );

		_createdTargets.Clear();

		CreatedTargetsRoot?.Destroy();
		CreatedTargetsRoot = null;
	}

	private void DestroyTargets( IEnumerable<Guid> targets )
	{
		foreach ( var trackId in targets )
		{
			if ( !Binder.TryGetBinding( trackId, out var value ) ) continue;
			if ( !IsCreatedTarget( value ) ) continue;

			Binder.Unbind( trackId );

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
}
