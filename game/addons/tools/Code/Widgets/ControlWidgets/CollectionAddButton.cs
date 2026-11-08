namespace Editor;

/// <summary>
/// Reusable add button for collection-based controls
/// </summary>
public class CollectionAddButton : IconButton
{
	private SerializedCollection Collection;

	public CollectionAddButton( SerializedCollection collection ) : base( "add" )
	{
		Collection = collection;

		Background = Theme.ControlBackground;
		ToolTip = "Add Item";
		FixedWidth = Theme.RowHeight;
		FixedHeight = Theme.RowHeight;
		AcceptDrops = true;
	}

	public override void OnDragDrop( DragEvent ev )
	{
		base.OnDragDrop( ev );

		var dataObj = ev.Data.Object;
		var parentType = Collection.ValueType;

		// Allow dragging objects onto the add button
		if ( dataObj is object[] dataArray )
		{
			foreach ( var obj in dataArray )
			{
				var objType = obj.GetType();
				if ( objType != parentType )
				{
					if ( obj is GameObject gameObject && parentType.IsAssignableTo( typeof( Component ) ) )
					{
						// Look for enabled components first, since we usually want those
						var comp = gameObject.Components.Get( parentType );
						// Then fallback on disabled components
						comp ??= gameObject.Components.Get( parentType, FindMode.DisabledInSelf );
						if ( comp is not null )
						{
							Collection.Add( comp );
						}
					}
					continue;
				}
				Collection.Add( obj );
			}
		}
		else if ( ev.Data.Assets.Count > 0 )
		{
			DropAssets( ev );
		}
		else if ( dataObj?.GetType() == parentType )
		{
			Collection.Add( dataObj );
		}
	}

	private async void DropAssets( DragEvent ev )
	{
		var parentType = Collection.ValueType;

		// Special case for SoundFile
		if ( parentType == typeof( SoundFile ) )
		{
			await DropSoundFiles( ev );
			return;
		}

		foreach ( var dataAsset in ev.Data.Assets )
		{
			if ( dataAsset is null ) continue;
			var asset = await dataAsset.GetAssetAsync();
			if ( asset is null ) continue;
			var resource = asset.LoadResource();
			if ( resource is null ) continue;
			Collection.Add( resource );
		}
	}

	private async Task DropSoundFiles( DragEvent ev )
	{
		foreach ( var dataAsset in ev.Data.Assets )
		{
			if ( dataAsset is null ) continue;
			if ( dataAsset.AssetPath.EndsWith( ".sound" ) ) continue;
			if ( !dataAsset.IsInstalled )
			{
				if ( await dataAsset.GetAssetAsync() is null ) continue;
			}
			var sound = SoundFile.Load( dataAsset.AssetPath );
			if ( sound is null ) continue;
			Collection.Add( sound );
		}
	}
}
