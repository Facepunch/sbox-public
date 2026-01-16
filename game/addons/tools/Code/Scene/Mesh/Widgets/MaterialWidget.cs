namespace Editor.MeshEditor;

class MaterialWidget : Widget
{
	public bool ShowFilename { get; set; } = true;

	readonly ActiveMaterialWidget _parent;

	protected bool _isDownloading;
	protected bool _isValidDropHover;

	public Material Material
	{
		get => field;

		set
		{
			if ( field == value ) return;

			field = value;
			Update();
		}
	}

	public MaterialWidget( ActiveMaterialWidget parent )
	{
		_parent = parent;

		IsDraggable = true;
		AcceptDrops = true;
		Cursor = CursorShape.Finger;
	}

	protected override void OnPaint()
	{
		var material = Material;
		var asset = material != null ? AssetSystem.FindByPath( material.ResourcePath ) : null;
		var icon = AssetType.Material?.Icon64;

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( asset is not null && !asset.IsDeleted )
		{
			icon = asset.GetAssetThumb( true );
		}

		if ( icon is not null )
		{
			Paint.Draw( LocalRect, icon );
		}

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrushAndPen( Color.Transparent, Color.White );
			Paint.DrawRect( LocalRect, 0 );
		}

		if ( ShowFilename && asset is not null )
		{
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( LocalRect, asset.RelativePath, TextFlag.LeftBottom, Color.White );
		}
	}

	protected virtual void OnMaterialDropped( Material material )
	{
		_parent?.UpdateFromMaterial( material );
	}

	protected override void OnDragStart()
	{
		if ( Material is null )
			return;

		var asset = AssetSystem.FindByPath( Material.ResourcePath );
		if ( asset == null )
			return;

		var drag = new Drag( this );
		drag.Data.Object = asset;
		drag.Data.Url = new Uri( $"file://{asset.AbsolutePath}" );
		drag.Execute();
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();

		_isValidDropHover = false;
	}

	public override void OnDragHover( DragEvent ev )
	{
		if ( ev.Data.Url?.Scheme == "https" )
		{
			ev.Action = DropAction.Link;
			_isValidDropHover = true;
			return;
		}

		if ( ev.Data.HasFileOrFolder )
		{
			var assetFromPath = AssetSystem.FindByPath( ev.Data.FileOrFolder );
			if ( assetFromPath is not null && assetFromPath.AssetType == AssetType.Material )
			{
				ev.Action = DropAction.Link;
				_isValidDropHover = true;
				return;
			}
		}

		if ( ev.Data.Object is Asset asset && asset.AssetType == AssetType.Material )
		{
			ev.Action = DropAction.Link;
			_isValidDropHover = true;
			return;
		}

		if ( ev.Data.Object is Material )
		{
			ev.Action = DropAction.Link;
			_isValidDropHover = true;
		}
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		if ( ev.Data.Url?.Scheme == "https" )
		{
			_ = AssignFromUrlAsync( ev.Data.Text );
			ev.Action = DropAction.Link;
			return;
		}

		Material droppedMaterial = null;

		if ( ev.Data.HasFileOrFolder )
		{
			var assetFromPath = AssetSystem.FindByPath( ev.Data.FileOrFolder );
			if ( assetFromPath is not null && assetFromPath.AssetType == AssetType.Material )
			{
				droppedMaterial = assetFromPath.LoadResource( typeof( Material ) ) as Material;
			}
		}
		else if ( ev.Data.Object is Asset asset && asset.AssetType == AssetType.Material )
		{
			droppedMaterial = asset.LoadResource( typeof( Material ) ) as Material;
		}
		else if ( ev.Data.Object is Material material )
		{
			droppedMaterial = material;
		}

		if ( droppedMaterial is null )
			return;

		OnMaterialDropped( droppedMaterial );
		ev.Action = DropAction.Link;

		_isValidDropHover = false;
		Update();
	}

	async Task AssignFromUrlAsync( string identUrl )
	{
		try
		{
			_isDownloading = true;
			Update();

			var asset = await AssetSystem.InstallAsync( identUrl );
			if ( asset is null || asset.AssetType != AssetType.Material )
				return;

			if ( asset.LoadResource<Material>() is not Material mat )
				return;

			OnMaterialDropped( mat );
		}
		finally
		{
			_isDownloading = false;
			_isValidDropHover = false;
			Update();
		}
	}
}
