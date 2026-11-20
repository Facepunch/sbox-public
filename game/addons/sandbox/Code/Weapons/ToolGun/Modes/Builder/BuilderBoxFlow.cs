/// <summary>
/// Draws a block as a footprint on a surface (first corner, opposite corner) and then a height off it. The shape follows
/// the surface, turned and tipped by R and Shift + R; Shift keeps the shape's proportions.
/// </summary>
internal sealed class BuilderBoxFlow : BuilderFlow
{
	const float HeightAimThreshold = 0.99985f;

	enum Stage
	{
		Footprint,
		Height
	}

	Stage _stage;
	BuilderPlane _plane;
	Vector2 _start;
	Vector2 _footprintStart;
	Vector2 _footprintEnd;
	float _height;
	Vector3 _heightAimStart;
	bool _heightFollowsAim;
	Rotation _twist = Rotation.Identity;

	Model _sampleModel;
	BuilderShapeKey _sampleKey;
	Vector3 _dimensions;

	public BuilderBoxFlow( BuilderTool tool ) : base( tool )
	{
	}

	BuilderOrientation Orientation => new( Tool.Definition.Up, _twist );

	public override string PrimaryHint => _stage == Stage.Footprint ? "#tool.hint.builder.corner" : "#tool.hint.builder.place";
	public override string SecondaryHint => _stage == Stage.Footprint ? "#tool.hint.builder.cancel" : "#tool.hint.builder.back";
	public override string GetReloadHint( bool drawing ) => "#tool.hint.builder.rotate";
	public override bool SupportsSymmetry => true;
	public override string HudHint => _stage == Stage.Footprint ? "#tool.hint.builder.hud_square" : "#tool.hint.builder.hud_cube";

	public override void Begin( in BuilderPlane plane, Vector2 start )
	{
		Clear();
		_plane = plane;
		_start = start;
		_stage = Stage.Footprint;
	}

	public override void Update( Ray eyes, int grid )
	{
		Clear();

		if ( _stage == Stage.Footprint )
		{
			Tool.DrawSnapPoints( _plane );
			if ( Tool.TryResolveStart( eyes, out var hovered, out _, out _ ) ) Tool.DrawSnapPoints( hovered );

			if ( !BuilderPlacement.TryProject( _plane, eyes, out var end ) )
			{
				Tool.HideGrid();
				return;
			}

			var start = _start;
			var proportions = PlaneProportions();
			var corner = Tool.KeepProportions && proportions.x > 0 && proportions.y > 0
				? Tool.PlaceProportional( _plane, start, end, new Vector2( proportions.x, proportions.y ), grid )
				: Tool.PlacePoint( _plane, end, grid );

			Tool.ShowGrid( _plane, grid, _plane.ToWorld( end ), _plane.ToWorld( corner ) );

			_footprintStart = Tool.Symmetrical ? start * 2 - corner : start;
			_footprintEnd = corner;
			Build( grid );
			return;
		}

		var center = _plane.ToWorld( (_footprintStart + _footprintEnd) * 0.5f );

		if ( !_heightFollowsAim && Vector3.Dot( eyes.Forward, _heightAimStart ) < HeightAimThreshold )
			_heightFollowsAim = true;

		var p = PlaneProportions();
		var footprint = _footprintEnd - _footprintStart;
		var proportional = MathF.Max(
			p.x > 0 ? MathF.Abs( footprint.x ) * p.z / p.x : 0,
			p.y > 0 ? MathF.Abs( footprint.y ) * p.z / p.y : 0 );
		var keepHeight = Tool.KeepProportions && p.z > 0 && proportional > 0;

		var aimed = (float)grid;
		if ( !keepHeight && Tool.TryAlignHeight( center, _plane.Normal, out var aligned ) )
		{
			_heightFollowsAim = true;
			_height = Math.Clamp( aligned, -BuilderShapeKey.MaxSize, BuilderShapeKey.MaxSize );
		}
		else if ( !_heightFollowsAim || BuilderPlacement.TryGetHeight( center, _plane.Normal, eyes, out aimed ) )
		{
			var height = Tool.OffGrid ? aimed : BuilderPlacement.Snap( aimed, grid );

			if ( keepHeight )
				height = proportional * (aimed < 0 ? -1 : 1);

			if ( MathF.Abs( height ) < grid ) height = aimed < 0 ? -grid : grid;

			_height = Math.Clamp( height, -BuilderShapeKey.MaxSize, BuilderShapeKey.MaxSize );
		}

		var placed = _plane.ToWorld( _footprintEnd );
		Tool.ShowGrid( _plane, grid, placed, placed, center + _plane.Normal * _height );
		Build( _height );
	}

	void Build( float height )
	{
		var footprint = _footprintEnd - _footprintStart;
		SetDimensions( new Vector3( MathF.Abs( footprint.x ), MathF.Abs( footprint.y ), MathF.Abs( height ) ) );

		if ( !BuilderPlacement.TryGetBlock( _plane, _footprintStart, _footprintEnd, height, Orientation, out var transform, out var size ) )
			return;

		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		Tool.GetSettings( values );

		HasBox = true;
		Transform = transform;
		Size = size;
		IsValid = Tool.TryValidate( size, values, out var key );
		Key = key;
	}

	void SetDimensions( Vector3 dimensions )
	{
		if ( Dimensions is not null && dimensions == _dimensions ) return;

		_dimensions = dimensions;
		Dimensions = $"{dimensions.x:0.##} × {dimensions.y:0.##} × {dimensions.z:0.##}";
	}

	Vector3 PlaneProportions()
	{
		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		Tool.GetSettings( values );

		var definition = Tool.Definition;
		var probe = new BuilderShapeKey( Tool.Shape, 16, 16, 16, true );
		for ( int i = 0; i < definition.Parameters.Length; i++ )
			probe = probe.WithParameter( i, values[i] );

		var p = Orientation.ToPlane( definition.GetProportions( probe ) );
		return new Vector3( MathF.Max( p.x, 0 ), MathF.Max( p.y, 0 ), MathF.Max( p.z, 0 ) );
	}

	public override bool Advance()
	{
		if ( _stage == Stage.Height ) return IsValid;
		if ( !HasBox ) return false;

		_stage = Stage.Height;
		_height = BuilderPlacement.ClampGrid( Tool.GridSize );
		_heightAimStart = Tool.Player.EyeTransform.Forward;
		_heightFollowsAim = false;
		return false;
	}

	public override bool Back()
	{
		if ( _stage == Stage.Footprint ) return false;

		_stage = Stage.Footprint;
		return true;
	}

	public override void Reload( in BuilderPlane hover, bool drawing ) => _twist = BuilderOrientation.Turn( _twist );

	/// <summary>Tips the shape over: scrolling up rolls its top away from the player, down towards them.</summary>
	public override void Scroll( int steps )
	{
		var eyes = Tool.Player.EyeTransform.Rotation;
		var view = new Vector3( Vector3.Dot( eyes.Forward, _plane.Right ), Vector3.Dot( eyes.Forward, _plane.Forward ), 0 );

		if ( view.Length < 0.3f )
			view = new Vector3( Vector3.Dot( eyes.Up, _plane.Right ), Vector3.Dot( eyes.Up, _plane.Forward ), 0 );

		for ( int i = 0; i < Math.Abs( steps ); i++ )
			_twist = BuilderOrientation.Tip( _twist, steps > 0 ? view : -view );
	}

	public override bool TryGetFallbackPreview( out Model model, out Vector3 size )
	{
		Span<float> values = stackalloc float[BuilderShapes.MaxParameters];
		Tool.GetSettings( values );

		if ( BuilderShapes.TryGetSample( Tool.Shape, values, out var key ) )
		{
			if ( _sampleModel is null || _sampleKey != key )
			{
				_sampleKey = key;
				_sampleModel = BuilderModelCache.TryGet( key, out var cached ) ? cached : BuilderGeometry.Create( key, collision: false );
			}

			if ( _sampleModel is not null )
			{
				model = _sampleModel;
				size = key.Size;
				return true;
			}
		}

		model = Model.Cube;
		size = model.Bounds.Size;
		return true;
	}
}
