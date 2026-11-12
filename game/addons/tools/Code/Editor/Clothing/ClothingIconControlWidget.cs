namespace Editor;

[CustomEditor( typeof( Clothing.IconSetup ) )]
public class ClothingIconControlWidget : ControlWidget
{
	public Color HighlightColor = Theme.Yellow;
	public override bool IsWideMode => true;

	ClothingIconPreviewWidget preview;
	Widget settings;
	Layout rightLayout;

	public ClothingIconControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 16;
		preview = Layout.Add( new ClothingIconPreviewWidget() );

		rightLayout = Layout.AddColumn( 1 );
		rightLayout.Spacing = 4;

		if ( property.TryGetAsObject( out var so ) )
		{
			settings = rightLayout.Add( new Widget( this ) );
			settings.Layout = Layout.Column();
			settings.Layout.Add( ControlSheet.Create( so ) );
			so.OnPropertyChanged += p => UpdatePreview();
		}


		rightLayout.AddStretchCell();
		rightLayout.Add( new Button( "Save Icon To Disk" ) { Clicked = SaveIcon } );

		UpdatePreview();
		Think();
	}

	/// <summary>
	/// Player models share a fixed portrait; only garments expose custom framing controls.
	/// </summary>
	public override void Think()
	{
		base.Think();

		if ( settings.IsValid() )
			settings.Visible = SerializedProperty.Parent.Targets.FirstOrDefault() is Clothing { ReplacesBody: false };
	}

	void UpdatePreview()
	{
		var so = SerializedProperty.Parent;
		Clothing clothing = so.Targets.First() as Clothing;

		preview.Scene.InstallClothing( clothing );
		preview.Clothing = clothing;
	}

	protected override void OnPaint()
	{
		// nout
	}

	void SaveIcon()
	{
		var so = SerializedProperty.Parent;
		Clothing clothing = so.Targets.First() as Clothing;
		if ( clothing is null ) return;

		var asset = AssetSystem.FindByPath( clothing.ResourcePath );
		ClothingIconPreviewWidget.RenderIcon( asset, clothing );
	}

	public static async Task SaveAllIcons()
	{
		using var progress = Application.Editor.ProgressSection();
		progress.Title = "Rendering Icons";

		var token = progress.GetCancel();
		var allClothes = AssetSystem.All.Where( x => x.AssetType.FileExtension == "clothing" ).ToArray();

		int i = 0;
		foreach ( var asset in allClothes )
		{
			progress.Title = asset.Name;
			progress.Current = ++i;
			progress.TotalCount = allClothes.Length;

			var resource = asset.LoadResource<Clothing>();

			await Task.Delay( 100 );

			ClothingIconPreviewWidget.RenderIcon( asset, resource );

			if ( token.IsCancellationRequested )
				return;
		}
	}
}
