namespace Sandbox;

/// <summary>
/// Non-destructive cable data + mesh generation.
/// </summary>
[Hide]
[Title( "Cable Component" ), Icon( "cable" )]
public sealed class CableComponent : Component, Component.ExecuteInEditor
{
	struct CablePathSample
	{
		public GameObject NodeObject;
		public Vector3 Position;
		public float RadiusScale;
		public float Roll;
	}

	public enum CableTextureOrientation
	{
		Horizontal,
		Vertical
	}

	Vector3[] _controlPoints = [];
	float _size = 8f;
	int _subdivisions = 8;
	int _pathDetail = 6;
	float _slack;
	bool _capEnds = true;
	CableTextureOrientation _textureOrientation = CableTextureOrientation.Horizontal;
	float _textureScale = 0.25f;
	float _textureRepeatsCircumference = 1.0f;
	float _textureOffsetAlongPath;
	float _textureOffsetCircumference;
	Material _material;
	bool _needsRebuild = true;
	Vector3[] _cachedNodePositions = [];
	float[] _cachedNodeRadiusScales = [];
	float[] _cachedNodeRolls = [];
	bool _isEditingNodes;
	RealTimeSince _timeSinceNodeChange;
	RealTimeSince _timeSincePreviewRebuild;

	[Property, Hide]
	public Vector3[] ControlPoints
	{
		get => _controlPoints;
		set
		{
			_controlPoints = value ?? [];
			MarkDirty();
		}
	}

	[Property, Range( 0.5f, 64f, slider: false ), Step( 0.5f ), Title( "Radius" )]
	public float Size
	{
		get => _size;
		set
		{
			_size = Math.Max( 0.1f, value );
			MarkDirty();
		}
	}

	[Property, Range( 3, 32, slider: false ), Step( 1 ), Title( "Subdivisions" )]
	public int Subdivisions
	{
		get => _subdivisions;
		set
		{
			_subdivisions = Math.Clamp( value, 3, 32 );
			MarkDirty();
		}
	}

	[Property, Range( 0, 16, slider: false ), Step( 1 ), Title( "Spacing" )]
	public int PathDetail
	{
		get => _pathDetail;
		set
		{
			_pathDetail = Math.Clamp( value, 0, 16 );
			MarkDirty();
		}
	}

	[Property, Range( -512f, 512f, slider: false ), Step( 0.5f ), Title( "Slack" )]
	public float Slack
	{
		get => _slack;
		set
		{
			_slack = Math.Clamp( value, -512f, 512f );
			MarkDirty();
		}
	}

	[Property( "FakeSlack" ), Hide]
	public float LegacyFakeSlack
	{
		get => _slack;
		set => Slack = value;
	}

	[Property]
	public bool CapEnds
	{
		get => _capEnds;
		set
		{
			_capEnds = value;
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" )]
	public Material Material
	{
		get => _material;
		set
		{
			_material = value;
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" ), Title( "Texture Orientation" )]
	public CableTextureOrientation TextureOrientation
	{
		get => _textureOrientation;
		set
		{
			if ( _textureOrientation == value )
				return;

			_textureOrientation = value;
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" ), Range( -4f, 4f, slider: false ), Step( 0.01f ), Title( "Texture Scale" )]
	public float TextureScale
	{
		get => _textureScale;
		set
		{
			var sign = value < 0 ? -1f : 1f;
			var abs = Math.Clamp( MathF.Abs( value ), 1.0f / 256.0f, 4f );
			_textureScale = abs * sign;
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" ), Range( -32f, 32f, slider: false ), Step( 0.01f ), Title( "Texture Repeats Circumference" )]
	public float TextureRepeatsCircumference
	{
		get => _textureRepeatsCircumference;
		set
		{
			var sign = value < 0 ? -1f : 1f;
			var abs = Math.Clamp( MathF.Abs( value ), 1.0f / 256.0f, 32f );
			_textureRepeatsCircumference = abs * sign;
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" ), Range( -1f, 1f, slider: false ), Step( 0.01f ), Title( "Texture Offset Along Path" )]
	public float TextureOffsetAlongPath
	{
		get => _textureOffsetAlongPath;
		set
		{
			_textureOffsetAlongPath = Math.Clamp( value, -1f, 1f );
			MarkDirty();
		}
	}

	[Property, Category( "Rendering" ), Range( -1f, 1f, slider: false ), Step( 0.01f ), Title( "Texture Offset Circumference" )]
	public float TextureOffsetCircumference
	{
		get => _textureOffsetCircumference;
		set
		{
			_textureOffsetCircumference = Math.Clamp( value, -1f, 1f );
			MarkDirty();
		}
	}

	protected override void OnUpdate()
	{
		EnsureNodesFromLegacyPoints();
		DetectNodeChanges();

		if ( _isEditingNodes && _timeSinceNodeChange > 0.08f )
		{
			_isEditingNodes = false;
			MarkDirty();
		}

		if ( !_needsRebuild )
			return;

		if ( _isEditingNodes && _timeSincePreviewRebuild < 0.03f )
			return;

		RebuildMesh();
	}

	void MarkDirty() => _needsRebuild = true;
	internal void NotifyNodeChanged() => MarkDirty();

	Vector3 LocalToWorld( Vector3 point ) => GameObject.WorldTransform.PointToWorld( point );

	protected override void DrawGizmos()
	{
		if ( !Scene.IsEditor )
			return;

		var nodeData = GetNodeData();
		if ( nodeData.Count == 0 )
			return;

		if ( !IsCableOrNodeSelected( nodeData ) )
			return;

		var localPoints = nodeData.Select( x => x.Position ).ToArray();

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 2;
		Gizmo.Draw.Color = Color.Yellow;

		for ( int i = 0; i < localPoints.Length - 1; i++ )
		{
			Gizmo.Draw.Line( localPoints[i], localPoints[i + 1] );
		}

		for ( int i = 0; i < localPoints.Length; i++ )
		{
			var local = localPoints[i];
			var world = LocalToWorld( local );
			var size = 3.0f * Gizmo.Camera.Position.Distance( world ) / 1000.0f;

			using ( Gizmo.Scope( $"cable-point-select-{i}" ) )
			{
				Gizmo.Object = nodeData[i].NodeObject;
				Gizmo.Hitbox.DepthBias = 0.01f;
				Gizmo.Hitbox.Sphere( new Sphere( local, size * 2 ) );
				if ( Gizmo.WasClicked && nodeData[i].NodeObject.IsValid() )
					Gizmo.Select( allowUnselect: false, allowMultiSelect: false );

				Gizmo.Draw.Color = Gizmo.IsHovered ? Color.Yellow : Color.White;
				Gizmo.Draw.SolidSphere( local, Gizmo.IsHovered ? size * 2 : size );

				var labelColor = Gizmo.IsHovered ? Gizmo.Colors.Active : Color.White;
				var textScope = new TextRendering.Scope
				{
					Text = $"{i + 1}",
					TextColor = labelColor,
					FontSize = 22 * Gizmo.Settings.GizmoScale * Screen.DesktopScale,
					FontName = "Roboto Mono",
					FontWeight = 400,
					LineHeight = 1,
					Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
				};

				Gizmo.Draw.ScreenText( textScope, world, Vector2.Up * 32 );
			}
		}
	}

	bool IsCableOrNodeSelected( IReadOnlyList<CablePathSample> nodeData )
	{
		using ( Gizmo.ObjectScope( GameObject, new Transform( 0 ) ) )
		{
			if ( Gizmo.IsSelected )
				return true;
		}

		for ( int i = 0; i < nodeData.Count; i++ )
		{
			var nodeObject = nodeData[i].NodeObject;
			if ( !nodeObject.IsValid() )
				continue;

			using ( Gizmo.ObjectScope( nodeObject, new Transform( 0 ) ) )
			{
				if ( Gizmo.IsSelected )
					return true;
			}
		}

		return false;
	}

	void RebuildMesh()
	{
		_needsRebuild = false;
		_timeSincePreviewRebuild = 0;

		var mesh = BuildMesh( _isEditingNodes );
		var meshComponent = GameObject.Components.GetOrCreate<MeshComponent>();
		meshComponent.Mesh = mesh;
		meshComponent.SmoothingAngle = 180.0f;
		meshComponent.Enabled = mesh is not null;
	}

	[Button( "Make Editable Mesh" )]
	public void MakeEditableMesh()
	{
		var mesh = BuildMesh( fastPreview: false );
		if ( mesh is null )
			return;

		var nodeObjects = GameObject.Children
			.Where( child => child.GetComponent<CableNodeComponent>() is not null )
			.ToArray();

		var existingMeshComponent = GameObject.GetComponent<MeshComponent>();
		var undoScope = Scene?.Editor?
			.UndoScope( "Make Cable Editable Mesh" )
			.WithGameObjectDestructions( nodeObjects )
			.WithComponentDestructions( this );

		undoScope = existingMeshComponent is not null
			? undoScope?.WithComponentChanges( existingMeshComponent )
			: undoScope?.WithComponentCreations();

		using ( undoScope?.Push() )
		{
			var meshComponent = existingMeshComponent ?? GameObject.Components.GetOrCreate<MeshComponent>();
			meshComponent.Mesh = mesh;
			meshComponent.SmoothingAngle = 180.0f;
			meshComponent.Enabled = true;

			foreach ( var child in nodeObjects )
			{
				child.Destroy();
			}

			Destroy();
		}
	}

	PolygonMesh BuildMesh( bool fastPreview = false )
	{
		var nodes = GetNodeData();
		if ( nodes.Count < 2 )
			return null;

		var effectivePathDetail = fastPreview ? Math.Min( _pathDetail, 1 ) : _pathDetail;
		var path = BuildPathPoints( nodes, effectivePathDetail, _slack );
		if ( path.Count < 2 ) return null;

		var sides = Math.Max( 3, _subdivisions );
		if ( fastPreview )
			sides = Math.Min( sides, 8 );
		var radius = Math.Max( 0.1f, _size );
		var material = _material;

		var mesh = new PolygonMesh();
		var rings = new HalfEdgeMesh.VertexHandle[path.Count][];
		var pathParam = BuildPathUvs( path, material );
		var circumferenceParam = BuildCircumferenceUvs( sides );

		var tangent = (path[1].Position - path[0].Position).Normal;
		var normal = BuildInitialNormal( tangent );

		for ( int i = 0; i < path.Count; i++ )
		{
			var point = path[i].Position;
			tangent = BuildTangent( path, i );
			normal = BuildNormalFromPrevious( tangent, normal );
			var rollRotation = Rotation.FromAxis( tangent, path[i].Roll );
			normal = rollRotation * normal;
			var bitangent = tangent.Cross( normal ).Normal;
			var nodeRadius = radius * Math.Max( 0.01f, path[i].RadiusScale );

			var ring = new Vector3[sides];
			for ( int j = 0; j < sides; j++ )
			{
				var angle = (MathF.PI * 2.0f * j) / sides;
				var offset = normal * MathF.Cos( angle ) * nodeRadius + bitangent * MathF.Sin( angle ) * nodeRadius;
				ring[j] = point + offset;
			}

			rings[i] = mesh.AddVertices( ring );
		}

		for ( int i = 0; i < rings.Length - 1; i++ )
		{
			for ( int j = 0; j < sides; j++ )
			{
				var next = (j + 1) % sides;
				var nextCircumference = next == 0 ? GetCircumferenceUvAtRingEnd( circumferenceParam[0] ) : circumferenceParam[next];
				var face = mesh.AddFace( [rings[i][j], rings[i][next], rings[i + 1][next], rings[i + 1][j]] );
				mesh.SetFaceMaterial( face, material );

				var uv0 = BuildTextureUv( pathParam[i], circumferenceParam[j] );
				var uv1 = BuildTextureUv( pathParam[i], nextCircumference );
				var uv2 = BuildTextureUv( pathParam[i + 1], nextCircumference );
				var uv3 = BuildTextureUv( pathParam[i + 1], circumferenceParam[j] );
				mesh.SetFaceTextureCoords( face, [uv0, uv1, uv2, uv3] );
			}
		}

		if ( _capEnds && !fastPreview )
		{
			var startCapPositions = new Vector3[sides];
			var endCapPositions = new Vector3[sides];
			for ( int i = 0; i < sides; i++ )
			{
				startCapPositions[i] = mesh.GetVertexPosition( rings[0][sides - 1 - i] );
				endCapPositions[i] = mesh.GetVertexPosition( rings[^1][i] );
			}

			var startCap = mesh.AddVertices( startCapPositions );
			var endCap = mesh.AddVertices( endCapPositions );
			var startFace = mesh.AddFace( startCap );
			var endFace = mesh.AddFace( endCap );
			mesh.SetFaceMaterial( startFace, material );
			mesh.SetFaceMaterial( endFace, material );

			var startUvs = new Vector2[sides];
			var endUvs = new Vector2[sides];
			for ( int i = 0; i < sides; i++ )
			{
				var u = (float)i / Math.Max( 1, sides - 1 );
				startUvs[i] = new Vector2( u, 0.0f );
				endUvs[i] = new Vector2( u, 1.0f );
			}

			mesh.SetFaceTextureCoords( startFace, startUvs );
			mesh.SetFaceTextureCoords( endFace, endUvs );
		}

		mesh.SetSmoothingAngle( 180.0f );
		return mesh;
	}

	float[] BuildPathUvs( IReadOnlyList<CablePathSample> pathPoints, Material material )
	{
		var uvs = new float[pathPoints.Count];
		if ( pathPoints.Count <= 0 )
			return uvs;

		var scaleSign = _textureScale < 0.0f ? -1.0f : 1.0f;
		var scaleAbs = Math.Max( 1.0f / 256.0f, MathF.Abs( _textureScale ) );
		var texelsPerUnit = scaleSign * (1.0f / scaleAbs);
		var materialSizeTexels = GetPathTextureSizeTexels( material, _textureOrientation );
		float length = 0.0f;
		uvs[0] = _textureOffsetAlongPath;

		for ( int i = 1; i < pathPoints.Count; i++ )
		{
			length += pathPoints[i].Position.Distance( pathPoints[i - 1].Position );
			var uvSize = (length * texelsPerUnit) / materialSizeTexels;
			uvs[i] = uvSize + _textureOffsetAlongPath;
		}

		return uvs;
	}

	Vector2 BuildTextureUv( float pathParam, float circumferenceParam )
	{
		return _textureOrientation == CableTextureOrientation.Vertical
			? new Vector2( circumferenceParam, pathParam )
			: new Vector2( pathParam, circumferenceParam );
	}

	float[] BuildCircumferenceUvs( int sides )
	{
		var uvs = new float[sides];
		var repeats = _textureRepeatsCircumference;
		for ( int i = 0; i < sides; i++ )
		{
			var ratio = i / (float)sides;
			uvs[i] = ratio * repeats + _textureOffsetCircumference;
		}

		return uvs;
	}

	float GetCircumferenceUvAtRingEnd( float firstUv )
	{
		return firstUv + _textureRepeatsCircumference;
	}

	static float GetPathTextureSizeTexels( Material material, CableTextureOrientation textureOrientation )
	{
		var texture = material?.FirstTexture;
		var size = texture is not null && texture.IsValid
			? (textureOrientation == CableTextureOrientation.Vertical ? texture.Height : texture.Width)
			: 0;
		return Math.Max( 1.0f, size );
	}

	List<CablePathSample> GetNodeData()
	{
		var nodes = new List<CablePathSample>();
		foreach ( var child in GameObject.Children )
		{
			var node = child.GetComponent<CableNodeComponent>();
			if ( node is null )
				continue;

			nodes.Add( new CablePathSample
			{
				NodeObject = child,
				Position = child.LocalPosition,
				RadiusScale = node.RadiusScale,
				Roll = node.Roll
			} );
		}

		return nodes;
	}

	void EnsureNodesFromLegacyPoints()
	{
		if ( _controlPoints is not { Length: > 0 } )
			return;

		if ( GetNodeData().Count > 0 )
		{
			_controlPoints = [];
			return;
		}

		for ( int i = 0; i < _controlPoints.Length; i++ )
		{
			var nodeObject = new GameObject( true, $"Cable Node {i + 1}" );
			nodeObject.SetParent( GameObject, false );
			nodeObject.LocalPosition = _controlPoints[i];
			nodeObject.Components.GetOrCreate<CableNodeComponent>();
		}

		_controlPoints = [];
		MarkDirty();
	}

	void DetectNodeChanges()
	{
		var nodes = GetNodeData();
		if ( nodes.Count != _cachedNodePositions.Length )
		{
			CacheNodeState( nodes );
			OnNodeDataChanged();
			return;
		}

		for ( int i = 0; i < nodes.Count; i++ )
		{
			if ( !_cachedNodePositions[i].AlmostEqual( nodes[i].Position ) ||
				MathF.Abs( _cachedNodeRadiusScales[i] - nodes[i].RadiusScale ) > 0.0001f ||
				MathF.Abs( _cachedNodeRolls[i] - nodes[i].Roll ) > 0.0001f )
			{
				CacheNodeState( nodes );
				OnNodeDataChanged();
				return;
			}
		}
	}

	void OnNodeDataChanged()
	{
		_isEditingNodes = true;
		_timeSinceNodeChange = 0;
		MarkDirty();
	}

	void CacheNodeState( IReadOnlyList<CablePathSample> nodes )
	{
		_cachedNodePositions = new Vector3[nodes.Count];
		_cachedNodeRadiusScales = new float[nodes.Count];
		_cachedNodeRolls = new float[nodes.Count];
		for ( int i = 0; i < nodes.Count; i++ )
		{
			_cachedNodePositions[i] = nodes[i].Position;
			_cachedNodeRadiusScales[i] = nodes[i].RadiusScale;
			_cachedNodeRolls[i] = nodes[i].Roll;
		}
	}

	static List<CablePathSample> BuildPathPoints( IReadOnlyList<CablePathSample> controlPoints, int pathDetail, float fakeSlack )
	{
		if ( controlPoints.Count <= 1 )
			return [.. controlPoints];

		if ( pathDetail <= 0 && MathF.Abs( fakeSlack ) <= 0.0001f )
			return [.. controlPoints];

		var minStepsForSlack = MathF.Abs( fakeSlack ) > 0.0001f ? 2 : 1;
		var steps = Math.Max( minStepsForSlack, pathDetail + 1 );
		var points = new List<CablePathSample>( (controlPoints.Count - 1) * steps + 1 );

		for ( int i = 0; i < controlPoints.Count - 1; i++ )
		{
			var p0 = controlPoints[Math.Max( i - 1, 0 )];
			var p1 = controlPoints[i];
			var p2 = controlPoints[i + 1];
			var p3 = controlPoints[Math.Min( i + 2, controlPoints.Count - 1 )];

			for ( int s = 0; s < steps; s++ )
			{
				var t = s / (float)steps;
				var sag = 4.0f * t * (1.0f - t);
				points.Add( new CablePathSample
				{
					Position = CatmullRom( p0.Position, p1.Position, p2.Position, p3.Position, t ) + (Vector3.Down * (sag * fakeSlack)),
					RadiusScale = CatmullRom( p0.RadiusScale, p1.RadiusScale, p2.RadiusScale, p3.RadiusScale, t ),
					Roll = CatmullRom( p0.Roll, p1.Roll, p2.Roll, p3.Roll, t )
				} );
			}
		}

		points.Add( controlPoints[^1] );
		return points;
	}

	static Vector3 CatmullRom( Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * (
			(2.0f * p1) +
			(-p0 + p2) * t +
			(2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
			(-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
		);
	}

	static float CatmullRom( float p0, float p1, float p2, float p3, float t )
	{
		var t2 = t * t;
		var t3 = t2 * t;
		return 0.5f * (
			(2.0f * p1) +
			(-p0 + p2) * t +
			(2.0f * p0 - 5.0f * p1 + 4.0f * p2 - p3) * t2 +
			(-p0 + 3.0f * p1 - 3.0f * p2 + p3) * t3
		);
	}

	static Vector3 BuildTangent( IReadOnlyList<CablePathSample> points, int i )
	{
		if ( points.Count < 2 ) return Vector3.Forward;
		if ( i == 0 ) return (points[1].Position - points[0].Position).Normal;
		if ( i == points.Count - 1 ) return (points[^1].Position - points[^2].Position).Normal;

		var a = (points[i].Position - points[i - 1].Position).Normal;
		var b = (points[i + 1].Position - points[i].Position).Normal;
		var tangent = (a + b).Normal;
		return tangent.LengthSquared > 0.0001f ? tangent : b;
	}

	static Vector3 BuildInitialNormal( Vector3 tangent )
	{
		var up = MathF.Abs( tangent.Dot( Vector3.Up ) ) > 0.98f ? Vector3.Right : Vector3.Up;
		return tangent.Cross( up ).Normal;
	}

	static Vector3 BuildNormalFromPrevious( Vector3 tangent, Vector3 previousNormal )
	{
		var projected = previousNormal - tangent * previousNormal.Dot( tangent );
		if ( projected.LengthSquared > 0.0001f )
			return projected.Normal;

		return BuildInitialNormal( tangent );
	}
}
