namespace Editor.Assets;

[AssetPreview( "vmdl" )]
class PreviewModel : AssetPreview
{
	public override bool IsAnimatedPreview => true;
	public override float PreviewWidgetCycleSpeed => 0.2f;

	SkinnedModelRenderer modelRenderer;
	SkinnedModelRenderer arms;

	public PreviewModel( Asset asset ) : base( asset )
	{

	}

	/// <summary>
	/// Create the model or whatever needs to be viewed
	/// </summary>
	public override async Task InitializeAsset()
	{
		var model = await Model.LoadAsync( Asset.Path );
		if ( model is null ) return;

		using ( EditorUtility.DisableTextureStreaming() )
		{
			using ( Scene.Push() )
			{
				SceneCenter = model.RenderBounds.Center;
				SceneSize = Vector3.Zero;

				if ( model.MeshCount == 0 )
					return;

				PrimaryObject = new GameObject( true, "preview model" );
				PrimaryObject.WorldTransform = Transform.Zero;

				modelRenderer = PrimaryObject.AddComponent<SkinnedModelRenderer>();
				modelRenderer.PlayAnimationsInEditorScene = true;
				modelRenderer.Model = model;

				bool isViewModel = System.IO.Path.GetFileName( model.Name ).StartsWith( "v_" );

				if ( isViewModel )
				{
					var armsgo = new GameObject();
					armsgo.Parent = PrimaryObject;

					arms = armsgo.AddComponent<SkinnedModelRenderer>();
					arms.Model = Model.Load( "models/first_person/first_person_arms_preview.vmdl" );
					arms.BoneMergeTarget = modelRenderer;
				}

				SceneSize = model.Bounds.Size;
				SceneCenter = model.Bounds.Center;
			}
		}
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		base.UpdateScene( cycle, timeStep );

		UpdateViewModelScene( timeStep );
	}

	private bool UpdateViewModelScene( float timeStep )
	{
		if ( !arms.IsValid() )
			return false;

		modelRenderer.WorldTransform = Transform.Zero;

		var r_upperarm = modelRenderer.Model.Bones.GetBone( "r_upperarm" );
		var l_upperarm = modelRenderer.Model.Bones.GetBone( "l_upperarm" );

		var camera = modelRenderer.Model.Bones.GetBone( "camera" );
		if ( camera is not null && modelRenderer.TryGetBoneTransform( "camera", out var tx ) )
		{
			Camera.WorldPosition = tx.Position;
			Camera.WorldRotation = tx.Rotation;
			Camera.FieldOfView = 85;
			Camera.ZNear = 0.1f;
			Camera.ZFar = 2000;
		}

		return true;
	}

	public override Widget CreateToolbar()
	{
		if ( !modelRenderer.IsValid() )
			return null;

		return new ModelToolbar( this );
	}

	internal Model Model => modelRenderer.IsValid() ? modelRenderer.Model : null;

	internal int LodCount => Model?.MeshInfo.LodCount ?? 1;

	internal void SetLod( int? lod )
	{
		if ( modelRenderer.IsValid() )
			modelRenderer.LodOverride = lod;
	}

	internal void SetMaterialGroup( string name )
	{
		if ( modelRenderer.IsValid() )
			modelRenderer.MaterialGroup = name;
	}

	internal int TriangleCountForLod( int lod )
	{
		if ( Model?.MeshInfo is not { } info )
			return 0;

		var bit = 1 << lod;
		var triangles = 0;
		foreach ( var mesh in info.Meshes )
		{
			if ( (mesh.LodMask & bit) != 0 )
				triangles += mesh.Triangles;
		}

		return triangles;
	}

	public void OpenSettings( Widget parent )
	{
		var popup = new PopupWidget( parent );
		popup.IsPopup = true;


		popup.Layout = Layout.Column();
		popup.Layout.Margin = 16;

		var ps = new ControlSheet();

		ps.AddProperty( this, x => x.BackgroundColor );
		//	ps.AddProperty( Camera, x => x.EnablePostProcessing );

		popup.Layout.Add( ps );
		popup.MaximumWidth = 300;
		popup.Show();
		popup.Position = parent.ScreenRect.TopRight - popup.Size;
		popup.ConstrainToScreen();

	}

}

file sealed class ModelToolbar : Widget
{
	readonly PreviewModel _preview;
	readonly Label _stats;

	public ModelToolbar( PreviewModel preview ) : base( null )
	{
		_preview = preview;

		var controlHeight = Theme.RowHeight + 8;
		FixedHeight = controlHeight + 12;

		Layout = Layout.Row();
		Layout.Margin = new Margin( 12, 6, 12, 6 );
		Layout.Spacing = 8;

		var model = preview.Model;

		if ( model?.MaterialGroupCount > 1 )
		{
			var skins = AddCombo( controlHeight, "Skin" );
			foreach ( var i in Enumerable.Range( 0, model.MaterialGroupCount ) )
			{
				var name = model.GetMaterialGroupName( i );
				skins.AddItem( name, "palette", () => preview.SetMaterialGroup( name ), selected: i == 0 );
			}
		}

		if ( preview.LodCount > 1 )
		{
			var lods = AddCombo( controlHeight, "Level of Detail" );
			foreach ( var lod in Enumerable.Range( 0, preview.LodCount ) )
				lods.AddItem( $"LOD {lod}", "layers", () => SelectLod( lod ), selected: lod == 0 );

			preview.SetLod( 0 );
		}

		Layout.AddStretchCell();

		_stats = new Label( this );
		_stats.Color = Theme.TextLight;
		Layout.Add( _stats );
		UpdateStats( 0 );

		var settings = new IconButton( "settings" );
		settings.FixedSize = controlHeight;
		settings.MouseLeftPress = () => preview.OpenSettings( settings );
		Layout.Add( settings );
	}

	void SelectLod( int lod )
	{
		_preview.SetLod( lod );
		UpdateStats( lod );
	}

	void UpdateStats( int lod )
	{
		if ( _preview.Model?.MeshInfo is not { } info )
			return;

		_stats.Text = $"{_preview.TriangleCountForLod( lod ):n0} tris";
		_stats.ToolTip = $"{info.TotalVertices:n0} verts, {info.TotalDrawCalls:n0} draw calls";
	}

	ComboBox AddCombo( float height, string tooltip )
	{
		var combo = new ComboBox( this );
		combo.FixedHeight = height;
		combo.ToolTip = tooltip;
		Layout.Add( combo );
		return combo;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.5f ) );
		Paint.DrawRect( LocalRect );

		Paint.SetPen( Theme.WidgetBackground );
		Paint.DrawLine( new Vector2( 0, Height - 1 ), new Vector2( Width, Height - 1 ) );
	}
}
