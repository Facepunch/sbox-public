/// <summary>
/// Drops this object's rope or string as a <see cref="RopeGib"/> when the object at one of its ends is destroyed: the
/// rope hangs from the end that is left. A rope removed on purpose, with both ends still there, just goes.
/// </summary>
public sealed class RopeGibDropper : Component
{
	// What the copy needs, kept while alive: OnDestroy runs after the components are already gone.
	LineRenderer _line;
	Scene _scene;
	GameObject _startHolder;
	GameObject _endHolder;
	Vector3 _lastStart;
	Vector3? _lastEnd;
	bool _dropped;

	protected override void OnUpdate()
	{
		// The line may be disabled, so include disabled ones.
		_line ??= GetComponent<LineRenderer>( true );
		if ( _line is null )
			return;

		var end = _line.Points?.LastOrDefault();
		if ( !end.IsValid() )
		{
			if ( _lastEnd is not null )
			{
				Drop();
				Destroy();
			}

			return;
		}

		// Each end moves with the object it's tied to: a rope end is a child of it, a loose object like a balloon is
		// its own.
		_scene = Scene;
		_startHolder = GameObject.Parent != Scene ? GameObject.Parent : GameObject;
		_lastStart = WorldPosition;
		_endHolder = end.Parent;
		_lastEnd = end.WorldPosition;
	}

	protected override void OnDestroy() => Drop();

	void Drop()
	{
		if ( _dropped || _lastEnd is null )
			return;

		_dropped = true;
		var startLost = IsLost( _startHolder );
		var endLost = IsLost( _endHolder );
		if ( startLost == endLost )
			return;

		var holder = startLost ? _endHolder : _startHolder;
		RopeGib.Spawn( _scene, _line, _lastStart, _lastEnd, holder == _scene ? null : holder, holdsEnd: startLost );
	}

	// The scene holds an end tied to nothing; it is never lost.
	bool IsLost( GameObject holder ) => holder != _scene && !IsAlive( holder );

	static bool IsAlive( GameObject go ) => go.IsValid() && !go.IsDestroyed;
}
