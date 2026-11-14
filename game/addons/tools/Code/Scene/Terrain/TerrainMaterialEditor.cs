using System.IO;
using System.Text.Json;

namespace Editor.TerrainEditor;

class TerrainMaterialEditor : BaseResourceEditor<TerrainMaterial>
{
	SerializedObject Object;
	TerrainMaterial _material;
	Asset _asset;
	RealTimeSince _sinceChange;
	bool _thumbDirty;
	Label _stats;
	Texture _sizeBcr, _sizeNho;

	// Size and format of the generated textures, under the thumbnail of the section each one is built from
	string _bcrInfo = "", _nhoInfo = "";
	Label _bcrLabel, _nhoLabel;
	Widget _settings;

	// The editor's sections: title and properties
	static readonly (string Title, string[] Properties)[] Sections =
	[
		("Color", [nameof( TerrainMaterial.AlbedoImage ), nameof( TerrainMaterial.Metalness )]),
		("Normal", [nameof( TerrainMaterial.NormalImage ), nameof( TerrainMaterial.NormalStrength )]),
		("Roughness", [nameof( TerrainMaterial.RoughnessImage ), nameof( TerrainMaterial.Roughness )]),
		("Height", [nameof( TerrainMaterial.HeightImage ), nameof( TerrainMaterial.HeightBlendStrength ), nameof( TerrainMaterial.DisplacementScale )]),
		("Ambient Occlusion", [nameof( TerrainMaterial.AOImage )]),
		("Tiling", [nameof( TerrainMaterial.UVScale ), nameof( TerrainMaterial.NoTiling ), nameof( TerrainMaterial.Surface )]),
	];

	public TerrainMaterialEditor()
	{
		Layout = Layout.Column();
	}

	protected override void Initialize( Asset asset, TerrainMaterial resource )
	{
		Layout.Clear( true );

		Object = resource.GetSerialized();

		_material = resource;
		_asset = asset;

		// GPU memory of the generated textures, so a heavy material stands out. The lit sphere is the inspector's
		// own Preview tab below, which follows unsaved edits.
		_stats = new Label( null ) { Alignment = TextFlag.Center, Color = Theme.TextControl };
		_stats.SetStyles( "padding: 2px;" );
		Layout.Add( _stats );
		_sizeBcr = _sizeNho = null;
		UpdateSize();

		// No scroll area of its own: the inspector already scrolls the whole editor, and a nested one only got its minimum height
		_settings = new Widget( this ) { Layout = Layout.Column() };
		_settings.Layout.Margin = new Sandbox.UI.Margin( 8, 4, 20, 8 );
		_settings.Layout.Spacing = 4;
		Layout.Add( _settings );
		BuildSections();

		Object.OnPropertyChanged += ( p ) =>
		{
			TryAutoFill( p );
			NoteChanged( p );

			// Height's blend and displacement only apply with a height image
			if ( p.Name == nameof( TerrainMaterial.HeightImage ) )
				BuildSections();

			if ( p.Name == nameof( TerrainMaterial.NoTiling ) )
				UpdateTilingLabel();

			// The thumbnail is the 3D preview the paint palette shows: rebuild it once the slider settles
			_thumbDirty = true;
			_sinceChange = 0;

			// Cascade to all terrains, feel free to code a TerrainMaterial modified -> TerrainStorage -> Terrain event chain
			UpdateSceneTerrain();
		};
	}

	/// <summary>
	/// Every setting, grouped by the channel it acts on.
	/// </summary>
	void BuildSections()
	{
		var canvas = _settings;
		canvas.Layout.Clear( true );

		foreach ( var (title, properties) in Sections )
		{
			var section = new SectionCard( canvas );

			var sheet = new ControlSheet();
			string image = null;
			foreach ( var name in properties )
			{
				if ( title == "Height" && name != nameof( TerrainMaterial.HeightImage ) && !_material.HasHeightTexture )
					continue;

				if ( Object.GetProperty( name ) is not { } property )
					continue;

				if ( name.EndsWith( "Image" ) ) image = name;
				sheet.AddRow( property );
			}

			// Thumbnail of the section's image on the left: the picker's own swatch is tiny.
			// Tiling has no image and gets its icon, so every section's labels line up.
			var body = section.Layout.AddRow();
			body.Spacing = 10;

			var left = body.AddColumn();
			left.Add( image is not null
				? new ImageThumb( Object.GetProperty( image ) )
				: new ImageThumb( null, () => _material.NoTiling ? "grid_off" : "grid_on" ) );
			left.AddStretchCell();

			// Thin divider between the image and its settings
			var divider = body.Add( new Widget( section ) { FixedWidth = 1 } );
			divider.SetStyles( $"background-color: {Theme.WindowBackground.Hex};" );

			// Title beside the thumbnail rather than on a row of its own, settings straight under it
			var right = body.AddColumn( 1 );
			right.Spacing = 2;

			// Same heading as AnimGraph's property cards: title left, detail right. The detail is the generated
			// texture's format and size, for the sections that make one
			var headerRow = right.AddRow();
			headerRow.Spacing = 8;
			headerRow.Add( new Label.Header( title, section ), 1 );

			if ( title is "Color" or "Normal" )
			{
				var info = headerRow.Add( new Label( title == "Color" ? _bcrInfo : _nhoInfo, section ) );
				info.Alignment = TextFlag.RightCenter;
				info.Color = Theme.TextControl;
				if ( title == "Color" ) _bcrLabel = info; else _nhoLabel = info;
			}
			else if ( title switch { "Roughness" => ("Color", "A"), "Height" => ("Normal", "B"), "Ambient Occlusion" => ("Normal", "A"), _ => default } is (string texture, string channel ) )
			{
				// Packed into a channel of a generated texture: no size of its own, and leaving it empty saves nothing
				var info = headerRow.Add( new Label( $"in {texture} · {channel}", section ) );
				info.Alignment = TextFlag.RightCenter;
				info.Color = Theme.TextControl.WithAlpha( 0.6f );
				info.ToolTip = $"Stored in the {texture} texture's {channel} channel, so it adds no memory of its own.";
			}
			else if ( title == "Tiling" )
			{
				// Which mode the material is in, as the old card title showed
				_tilingLabel = headerRow.Add( new Label( "", section ) );
				_tilingLabel.Alignment = TextFlag.RightCenter;
				UpdateTilingLabel();
			}

			// Rows keep their natural height instead of spreading to the thumbnail's
			right.Add( sheet );
			right.AddStretchCell();
			canvas.Layout.Add( section );
		}
	}

	Label _tilingLabel;

	void UpdateTilingLabel()
	{
		if ( !_tilingLabel.IsValid() ) return;

		_tilingLabel.Text = _material.NoTiling ? "No Tiling" : "Repeating";
		_tilingLabel.Color = _material.NoTiling ? Theme.Primary : Theme.TextControl;
	}

	/// <summary>
	/// The generated textures are replaced when the material recompiles after an image change: refresh then.
	/// </summary>
	[EditorEvent.Frame]
	void UpdateSize()
	{
		if ( !_stats.IsValid() || _material is null ) return;

		if ( _thumbDirty && _sinceChange > 0.5f )
		{
			_thumbDirty = false;
			_asset?.RebuildThumbnail();
		}

		if ( _sizeBcr == _material.BCRTexture && _sizeNho == _material.NHOTexture ) return;

		_sizeBcr = _material.BCRTexture;
		_sizeNho = _material.NHOTexture;

		static long Bytes( Texture t ) => TextureResidencyInfo.Get( t ) is { } info ? Math.Max( info.Disk.MemorySize, info.Loaded.MemorySize ) : 0;
		static string Describe( Texture t ) => t is null || t.IsError ? "missing" : $"{t.ImageFormat} ({t.Width}×{t.Height}) · {Bytes( t ) / (1024.0 * 1024.0):0.#} MB";

		_bcrInfo = Describe( _sizeBcr );
		_nhoInfo = Describe( _sizeNho );
		if ( _bcrLabel.IsValid() ) _bcrLabel.Text = _bcrInfo;
		if ( _nhoLabel.IsValid() ) _nhoLabel.Text = _nhoInfo;

		var total = Bytes( _sizeBcr ) + Bytes( _sizeNho );
		_stats.Text = $"{total / (1024.0 * 1024.0):0.#} MB VRAM";
	}

	void TryAutoFill( SerializedProperty p )
	{
		// Only work from albedo for now
		if ( p.Name != nameof( TerrainMaterial.AlbedoImage ) ) return;

		string albedoPath = p.GetValue<string>();

		var directoryname = Path.GetDirectoryName( albedoPath );
		var filename = Path.GetFileNameWithoutExtension( albedoPath );
		var extension = Path.GetExtension( albedoPath );

		if ( !filename.EndsWith( "_color" ) ) return;

		string baseName = filename.Remove( filename.Length - "_color".Length );

		var tries = new List<(string, string)>()
		{
			( "RoughnessImage", "_rough" ),
			( "RoughnessImage", "_roughness" ),
			( "NormalImage", "_normal" ),
			( "NormalImage", "_normalgl" ),
			( "HeightImage", "_displacement" ),
			( "HeightImage", "_height" ),
			( "AOImage", "_ao" ),
			( "AOImage", "_ambientocclusion" ),
		};

		foreach ( var a in tries )
		{
			var name = $"{directoryname}/{baseName}{a.Item2}{extension}".Replace( "\\", "/" );

			if ( !FileSystem.Content.FileExists( name ) ) continue;

			var property = Object.GetProperty( a.Item1 );
			property.SetValue<string>( name );
		}
	}

	void UpdateSceneTerrain()
	{
		var terrains = SceneEditorSession.All
			.SelectMany( scene => scene.Scene.GetAllComponents<Terrain>() );

		foreach ( var terrain in terrains )
		{
			terrain.UpdateMaterialsBuffer();
		}
	}
}

/// <summary>
/// A section of the material editor, drawn like AnimGraph's property cards.
/// </summary>
class SectionCard : Widget
{
	public SectionCard( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();
		// A step lighter than the inspector, so the dark control fields stand out on it
		Paint.SetBrush( Theme.SurfaceBackground.Lighten( 0.12f ) );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );
	}
}

/// <summary>
/// The texture a material image property points at, drawn large and following the property as it changes.
/// Without a property it draws an icon instead.
/// </summary>
class ImageThumb : Widget
{
	const float ThumbSize = 56;

	readonly SerializedProperty _property;
	readonly Func<string> _icon;

	public ImageThumb( SerializedProperty property, Func<string> icon = null ) : base( null )
	{
		_property = property;
		_icon = icon;
		FixedWidth = FixedHeight = ThumbSize;
		ToolTip = property?.Description;
	}

	// Empty, or the placeholder an optional image falls back to
	static bool IsBound( string path ) => !string.IsNullOrEmpty( path ) && path != "materials/default/default_ao.tga";

	protected override void OnPaint()
	{
		const float Border = 1, Radius = 6;
		var rect = LocalRect;

		Paint.Antialiasing = true;
		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( rect, Radius );

		var path = _property?.GetValue<string>();
		if ( _property is null )
		{
			Paint.SetPen( Theme.TextLight );
			Paint.DrawIcon( rect, _icon?.Invoke() ?? "image", 28, TextFlag.Center );
		}
		else if ( IsBound( path ) && AssetSystem.FindByPath( path )?.GetAssetThumb() is { } pixmap )
		{
			// Inset by the border and rounded to match it, so the image's corners don't poke past
			Paint.Draw( rect.Shrink( Border ), pixmap, 1.0f, Radius - Border );
		}
		else
		{
			// Optional slot left empty, or an image that isn't an asset (yet)
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.4f ) );
			Paint.DrawIcon( rect, IsBound( path ) ? "image" : "block", 28, TextFlag.Center );
		}

		// Border last, over the image's edge, so a light texture doesn't bleed into the card
		Paint.ClearBrush();
		Paint.SetPen( Theme.WindowBackground, Border );
		Paint.DrawRect( rect.Shrink( Border / 2 ), Radius - Border / 2 );
	}

	// Picks up a new image and thumbnails that finish loading
	[EditorEvent.Frame]
	void Refresh() => Update();
}
