namespace Editor.MeshEditor;

[Alias( "tools.boolean-tool" )]
public partial class BooleanTool( string returnTool = nameof( ObjectSelection ) ) : EditorTool
{
	public enum BooleanMode
	{
		Union,
		Subtract,
		Intersect
	}

	public BooleanMode Mode
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			EditorCookie.Set( "BooleanTool.Mode", value );
			UpdatePreview();
		}
	}

	public bool DeleteOtherMesh
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			EditorCookie.Set( "BooleanTool.DeleteOther", value );
		}
	}

	MeshComponent _meshA;
	MeshComponent _meshB;

	PolygonMesh _originalMeshA;
	PolygonMesh _originalMeshB;
	bool _previewActive;

	public override void OnEnabled()
	{
		Mode = EditorCookie.Get( "BooleanTool.Mode", BooleanMode.Union );
		DeleteOtherMesh = EditorCookie.Get( "BooleanTool.DeleteOther", true );
		CacheSelection();
		UpdatePreview();
	}

	public override void OnDisabled()
	{
		RestoreOriginals();
		_meshA = null;
		_meshB = null;
	}

	public override void OnSelectionChanged()
	{
		RestoreOriginals();
		CacheSelection();
		UpdatePreview();
	}

	void CacheSelection()
	{
		_meshA = null;
		_meshB = null;
		_originalMeshA = null;
		_originalMeshB = null;

		var meshes = Selection.OfType<GameObject>()
			.Select( go => go.GetComponent<MeshComponent>() )
			.Where( mc => mc.IsValid() )
			.ToArray();

		if ( meshes.Length >= 2 )
		{
			_meshA = meshes[0];
			_meshB = meshes[1];
			_originalMeshA = _meshA.Mesh;
			_originalMeshB = _meshB.Mesh;
		}
	}

	public bool CanApply => _meshA.IsValid() && _meshB.IsValid();

	public void SwapSelection()
	{
		RestoreOriginals();
		(_meshA, _meshB) = (_meshB, _meshA);
		(_originalMeshA, _originalMeshB) = (_originalMeshB, _originalMeshA);
		UpdatePreview();
	}

	void UpdatePreview()
	{
		if ( !CanApply )
		{
			RestoreOriginals();
			return;
		}

		var relativeTransform = _meshA.WorldTransform.ToLocal( _meshB.WorldTransform );

		var operation = Mode switch
		{
			BooleanMode.Union => PolygonMesh.BooleanOperation.Union,
			BooleanMode.Subtract => PolygonMesh.BooleanOperation.Subtract,
			BooleanMode.Intersect => PolygonMesh.BooleanOperation.Intersect,
			_ => PolygonMesh.BooleanOperation.Union
		};

		var previewMesh = new PolygonMesh();
		previewMesh.SetTransform( _originalMeshA.Transform );
		previewMesh.MergeMesh( _originalMeshA, Transform.Zero, out _, out _, out _ );
		previewMesh.PerformBoolean( _originalMeshB, relativeTransform, operation );
		previewMesh.ComputeFaceTextureCoordinatesFromParameters();

		_meshA.Mesh = previewMesh;
		_meshA.RebuildMesh();

		var emptyMesh = new PolygonMesh();
		_meshB.Mesh = emptyMesh;
		_meshB.RebuildMesh();

		_previewActive = true;
	}

	void RestoreOriginals()
	{
		if ( !_previewActive ) return;

		if ( _meshA.IsValid() && _originalMeshA is not null )
		{
			_meshA.Mesh = _originalMeshA;
			_meshA.RebuildMesh();
		}

		if ( _meshB.IsValid() && _originalMeshB is not null )
		{
			_meshB.Mesh = _originalMeshB;
			_meshB.RebuildMesh();
		}

		_previewActive = false;
	}

	public override void OnUpdate()
	{
		if ( !CanApply ) return;

		Gizmo.Draw.IgnoreDepth = true;

		Gizmo.Draw.Color = new Color( 0.2f, 0.8f, 1.0f, 0.3f );
		Gizmo.Draw.LineBBox( _meshA.GameObject.GetBounds() );
	}

	void Apply()
	{
		if ( !CanApply ) return;

		var relativeTransform = _meshA.WorldTransform.ToLocal( _meshB.WorldTransform );
		var operation = Mode switch
		{
			BooleanMode.Union => PolygonMesh.BooleanOperation.Union,
			BooleanMode.Subtract => PolygonMesh.BooleanOperation.Subtract,
			BooleanMode.Intersect => PolygonMesh.BooleanOperation.Intersect,
			_ => PolygonMesh.BooleanOperation.Union
		};

		var resultMesh = new PolygonMesh();
		resultMesh.SetTransform( _originalMeshA.Transform );
		resultMesh.MergeMesh( _originalMeshA, Transform.Zero, out _, out _, out _ );
		resultMesh.PerformBoolean( _originalMeshB, relativeTransform, operation );
		resultMesh.ComputeFaceTextureCoordinatesFromParameters();

		RestoreOriginals();

		using var scope = SceneEditorSession.Scope();

		var undoScope = SceneEditorSession.Active.UndoScope( $"Boolean {Mode}" )
			.WithComponentChanges( _meshA );

		if ( DeleteOtherMesh )
			undoScope = undoScope.WithGameObjectDestructions( [_meshB.GameObject] );

		using ( undoScope.Push() )
		{
			_meshA.Mesh = resultMesh;
			_meshA.RebuildMesh();

			if ( DeleteOtherMesh )
				_meshB.GameObject.Destroy();

			Selection.Clear();
			Selection.Add( _meshA.GameObject );
		}

		EditorToolManager.SetSubTool( returnTool );
	}

	void Cancel()
	{
		RestoreOriginals();
		EditorToolManager.SetSubTool( returnTool );
	}
}
