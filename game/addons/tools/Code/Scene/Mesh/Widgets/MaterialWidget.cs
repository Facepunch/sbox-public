namespace Editor.MeshEditor;

public class MaterialWidget : Widget
{
	public bool ShowFilename { get; set; } = true;

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
			Paint.Draw( LocalRect.Shrink( 2 ), icon );
		}

		if ( ShowFilename && asset is not null )
		{
			Paint.SetDefaultFont( 7 );
			Theme.DrawFilename( LocalRect.Shrink( 4 ), asset.RelativePath, TextFlag.LeftBottom, Color.White );
		}

	}
}
