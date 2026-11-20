internal sealed class BuilderPathFlow : BuilderFlow
{
	/// <summary>Radius change per degree of mouse movement, per grid cell.</summary>
	const float RadiusPerDegree = 1f / 8f;

	const float MinRadius = 0.5f;
	const float MaxRadius = 1024f;

	enum Stage
	{
		End,
		Radius
	}

	Stage _stage;
	Vector3 _start;
	Rotation _frame;
	BuilderPlane _startPlane;
	BuilderPlane _endPlane;
	Vector3 _end;
	Vector3 _endNormal;
	float _radius = 8f;
	float _drawRadius;
	float _mouseRadius;
	float _shownRadius;

	int _endTurn;
	const int EndTurns = 5;

	bool _refit;
	int _fitGrid;

	bool _hasBuilt;
	Vector3 _builtEnd;
	Vector3 _builtNormal;
	float _builtRadius;
	float _builtThickness;
	bool _builtValid;

	public BuilderPathFlow( BuilderTool tool ) : base( tool )
	{
	}

	public override bool StretchesPreview => false;
	public override bool AbsorbsMouse => _stage == Stage.Radius;

	public override string StartHint => "#tool.hint.builder.pipe_start";
	public override string PrimaryHint => _stage == Stage.End ? "#tool.hint.builder.pipe_end" : "#tool.hint.builder.pipe_place";
	public override string SecondaryHint => _stage == Stage.End ? "#tool.hint.builder.cancel" : "#tool.hint.builder.back";
	public override string HudHint => _stage == Stage.Radius ? "#tool.hint.builder.hud_radius" : "#tool.hint.builder.hud_pipe_end";

	public override string GetReloadHint( bool drawing ) => drawing ? "#tool.hint.builder.pipe_turn" : null;

	public override void Reload( in BuilderPlane hover, bool drawing )
	{
		if ( drawing ) TurnEnd( 1 );
	}

	void TurnEnd( int steps )
	{
		_endTurn = ((_endTurn + steps) % EndTurns + EndTurns) % EndTurns;
		_refit = true;
	}

	public override void Scroll( int steps ) => TurnEnd( steps );

	Vector3 EndDirection()
	{
		var plane = _endPlane;
		if ( _endTurn == 0 ) return plane.Normal;

		var toStart = _start - _end;
		float right = Vector3.Dot( toStart, plane.Right ), forward = Vector3.Dot( toStart, plane.Forward );
		var first = MathF.Abs( right ) >= MathF.Abs( forward ) ? (right >= 0 ? 0 : 2) : (forward >= 0 ? 1 : 3);

		return ((first + _endTurn - 1) % 4) switch
		{
			0 => plane.Right,
			1 => plane.Forward,
			2 => -plane.Right,
			_ => -plane.Forward
		};
	}

	public override void Begin( in BuilderPlane plane, Vector2 start )
	{
		Clear();
		_startPlane = plane;
		_start = plane.ToWorld( start );
		_frame = Rotation.LookAt( plane.Normal, plane.Forward );
		_stage = Stage.End;
		_drawRadius = _radius;
		_endTurn = 0;
		_refit = true;
	}

	public override void Update( Ray eyes, int grid )
	{
		Clear();

		if ( _stage == Stage.End )
		{
			// The start's face keeps its markers while the end is aimed, alongside the face being aimed at
			Tool.DrawSnapPoints( _startPlane );

			if ( !Tool.TryResolveStart( eyes, out var plane, out var point, out _ ) )
			{
				Tool.HideGrid();
				return;
			}

			var snapped = Tool.PlacePoint( plane, point, grid );
			Tool.ShowGrid( plane, grid, plane.ToWorld( point ), plane.ToWorld( snapped ) );
			Tool.DrawSnapPoints( plane );

			_endPlane = plane;
			var end = plane.ToWorld( snapped );
			if ( _refit || end != _end || plane.Normal != _endNormal || grid != _fitGrid )
			{
				_refit = false;
				_fitGrid = grid;
				_end = end;
				_endNormal = plane.Normal;

				_drawRadius = _radius;
				Build();
				if ( !IsValid ) _drawRadius = MathF.Max( grid * 0.5f, MinRadius );
			}

			Build();

			HasBox = IsValid;
			return;
		}

		_mouseRadius = Math.Clamp( _mouseRadius - Input.AnalogLook.pitch * RadiusPerDegree * grid, MinRadius, MaxRadius );
		_drawRadius = Tool.OffGrid ? _mouseRadius : MathF.Max( grid, BuilderPlacement.Snap( _mouseRadius * 2, grid ) ) / 2;
		Tool.ShowGrid( _endPlane, grid, _end, _end );

		Build();
		HasBox = true;
	}

	void Build()
	{
		var inverse = _frame.Inverse;
		var end = inverse * (_end - _start);
		var normal = inverse * EndDirection();

		Transform = new Transform( _start, _frame );
		SetDimensions( _drawRadius );

		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		Tool.GetSettings( values );
		var thickness = values[BuilderPipe.ThicknessSlot];

		if ( _hasBuilt && end == _builtEnd && normal == _builtNormal && _drawRadius == _builtRadius && thickness == _builtThickness )
		{
			IsValid = _builtValid;
			return;
		}

		_hasBuilt = true;
		_builtEnd = end;
		_builtNormal = normal;
		_builtRadius = _drawRadius;
		_builtThickness = thickness;
		_builtValid = false;

		values[BuilderPipe.RadiusSlot] = _drawRadius;
		values[BuilderPipe.EndSlot] = end.x;
		values[BuilderPipe.EndSlot + 1] = end.y;
		values[BuilderPipe.EndSlot + 2] = end.z;
		values[BuilderPipe.NormalSlot] = normal.x * BuilderPipe.NormalScale;
		values[BuilderPipe.NormalSlot + 1] = normal.y * BuilderPipe.NormalScale;
		values[BuilderPipe.NormalSlot + 2] = normal.z * BuilderPipe.NormalScale;

		if ( !BuilderPipe.TryCreate( end, normal, _drawRadius, thickness, out var pipe ) ) return;

		_builtValid = Tool.TryValidate( pipe.Bounds.Size, values, out var key );
		IsValid = _builtValid;
		Key = key;
		Size = key.Size;
	}

	void SetDimensions( float radius )
	{
		if ( Dimensions is not null && radius == _shownRadius ) return;

		_shownRadius = radius;
		Dimensions = $"Ø {radius * 2:0.##}";
	}

	public override bool Advance()
	{
		if ( !IsValid ) return false;

		if ( _stage == Stage.Radius )
		{
			_radius = _drawRadius;
			return true;
		}

		_stage = Stage.Radius;
		_mouseRadius = _drawRadius;
		return false;
	}

	public override bool Back()
	{
		if ( _stage == Stage.End ) return false;

		_stage = Stage.End;
		_refit = !IsValid;
		return true;
	}
}
