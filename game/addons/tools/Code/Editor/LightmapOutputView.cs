using System.IO;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Inspects the scene's baked lighting and the texture files in its lightmap folder.
/// </summary>
sealed class LightmapOutputView : Widget
{
	readonly Widget image;
	readonly Label details;
	readonly Widget sliceRow;
	readonly Label sliceLabel;
	readonly FloatSlider slice;
	readonly ComboBox channels;

	Texture texture;
	Pixmap pixmap;
	Color32[] pixels;
	Vector2Int previewSize;
	bool reading;
	string status;

	record Output( string Name, Texture Texture, string Path )
	{
		internal Output( string name, Texture texture ) : this( name, texture, texture?.ResourcePath ) { }
	}

	internal LightmapOutputView( Scene scene, BakedLightmap lightmap )
	{
		Layout = Layout.Column();
		Layout.Spacing = 6;

		var list = new List<Output>();
		if ( lightmap.Textures is { } atlas )
		{
			list.Add( new( "Indirect light", atlas.Irradiance ) );
			list.Add( new( "Direction & occlusion", atlas.Direction ) );
			list.Add( new( "Light strengths", atlas.Strengths ) );
			list.Add( new( "Light indices", atlas.Indices ) );
		}

		long probes = 0;
		int volumeIndex = 0;
		foreach ( var volume in scene.GetAll<LightProbeVolume>() )
		{
			if ( volume.Texture is null && volume.IndicesTexture is null && volume.ScalarsTexture is null ) continue;

			var name = $"Volume {++volumeIndex} · {volume.GameObject.Name}";
			list.Add( new( $"{name} / Ambient", volume.Texture ) );
			list.Add( new( $"{name} / Indices", volume.IndicesTexture ) );
			list.Add( new( $"{name} / Strengths", volume.ScalarsTexture ) );
			if ( volume.IndicesTexture is { IsValid: true, IsError: false } grid )
				probes += (long)grid.Width * grid.Height * grid.Depth;
		}

		if ( scene.Get<VolumetricFogController>()?.BakedFogTexture is { } fog )
			list.Add( new( "Volumetric fog", fog ) );

		var attached = list.Count;
		var paths = list.Select( x => x.Path ).Where( x => !string.IsNullOrEmpty( x ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		if ( scene.Source?.ResourcePath is { Length: > 0 } scenePath && !scenePath.Contains( "://" ) )
		{
			var folder = $"{Path.ChangeExtension( scenePath, null )}_scene_data/lightmaps";
			// A cleared or failed bake can leave useful files behind. Load those only when selected.
			var files = FileSystem.Mounted.FindFile( folder, "*.vtex_c" );
			foreach ( var file in files.OrderByDescending( x => x == "irradiance.vtex_c" ).ThenBy( x => x ) )
			{
				var path = $"{folder}/{file[..^2]}";
				if ( paths.Add( path ) )
					list.Add( new( $"On disk / {Path.GetFileNameWithoutExtension( file )}", null, path ) );
			}
		}

		if ( attached == 0 )
		{
			Layout.Add( new Label( list.Count == 0 ? "No baked textures in this scene." : "The scene has no bake applied. These textures are still on disk." ) { WordWrap = true } );
		}

		if ( list.Count == 0 ) return;

		var sizes = paths.Select( FileSize ).OfType<long>().ToArray();
		AddSummary( scene, lightmap, probes, sizes );

		var selectors = Layout.AddRow();
		selectors.Spacing = 4;
		var outputs = selectors.Add( new ComboBox { ToolTip = "Baked texture", HorizontalSizeMode = SizeMode.Flexible | SizeMode.Ignore }, 1 );
		channels = selectors.Add( new ComboBox { ToolTip = "Preview channel", FixedWidth = 80 } );
		foreach ( var name in new[] { "RGB", "R", "G", "B", "A" } )
			channels.AddItem( name );

		image = Layout.Add( new Widget { FixedHeight = 188, MouseTracking = true } );
		image.OnPaintOverride = PaintImage;
		image.MouseMove += InspectPixel;
		sliceRow = Layout.Add( new Widget { Layout = Layout.Row() } );
		sliceRow.Layout.Spacing = 8;
		sliceLabel = sliceRow.Layout.Add( new Label() );
		slice = sliceRow.Layout.Add( new FloatSlider( sliceRow ) { Minimum = 0, Step = 1 }, 1 );
		details = Layout.Add( new Label { Color = Theme.TextControl, WordWrap = true } );

		foreach ( var entry in list )
			outputs.AddItem( entry.Name );

		void SelectOutput()
		{
			var output = list[outputs.CurrentIndex];
			texture = output.Texture ?? (output.Path is null ? null : Texture.Load( output.Path ));
			var valid = texture.IsValid() && !texture.IsError;
			var depth = valid ? texture.Depth : 1;
			sliceRow.Visible = depth > 1;
			slice.Maximum = depth - 1;
			slice.Value = depth / 2;
			channels.Enabled = valid;
			details.Text = "Missing texture";
			if ( valid )
			{
				var dimensions = $"{texture.Width} × {texture.Height}";
				if ( depth > 1 ) dimensions += $" × {depth}";
				details.Text = $"{dimensions} · {texture.ImageFormat}";
			}

			if ( FileSize( output.Path ) is long bytes )
				details.Text += $" · {bytes.FormatBytes()} on disk";
			details.ToolTip = output.Path;
			outputs.ToolTip = outputs.CurrentText;
			SelectSlice();
		}

		outputs.ItemChanged += SelectOutput;
		slice.OnValueEdited = SelectSlice;
		channels.ItemChanged += UpdateChannel;
		SelectOutput();
	}

	void AddSummary( Scene scene, BakedLightmap lightmap, long probes, long[] sizes )
	{
		var metadata = new List<string>();
		if ( lightmap.Textures is not null )
		{
			var meshes = scene.GetAll<MeshComponent>().Count( x => x.LightmapOffset.HasValue );
			var models = scene.GetAll<ModelRenderer>().Count( x => x.LightmapOffset.HasValue );
			var lights = scene.GetAll<Light>().Count( x => x.BakeLightIndex >= 0 );

			var counts = $"{meshes:N0} meshes · {models:N0} models · {lights:N0} lights · {probes:N0} probes";
			Layout.Add( new Label( counts ) { ToolTip = counts, HorizontalSizeMode = SizeMode.Ignore } );
			metadata.Add( $"{lightmap.LuxelSize:0.##} units/luxel" );
		}

		if ( sizes.Length > 0 )
		{
			var files = sizes.Length == 1 ? "1 file" : $"{sizes.Length:N0} files";
			metadata.Add( $"{files} · {sizes.Sum().FormatBytes()} on disk" );
		}

		if ( metadata.Count == 0 ) return;

		var summary = string.Join( " · ", metadata );
		Layout.Add( new Label( summary ) { Color = Theme.TextLight, ToolTip = summary, HorizontalSizeMode = SizeMode.Ignore } );
	}

	static long? FileSize( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		var file = path + "_c";
		return FileSystem.Mounted.FileExists( file ) ? FileSystem.Mounted.FileSize( file ) : null;
	}

	void SelectSlice()
	{
		sliceLabel.Text = $"Slice {(int)slice.Value + 1} / {(int)slice.Maximum + 1}";
		pixmap = null;
		pixels = null;
		var valid = texture.IsValid() && !texture.IsError;
		status = valid ? "Loading preview…" : "Preview unavailable";
		image.ToolTip = details.ToolTip;
		image.Update();

		if ( valid && !reading )
			_ = ReadSlice();
	}

	// Only one readback at a time. If the user scrubs the volume, skip intermediate slices.
	async Task ReadSlice()
	{
		reading = true;
		try
		{
			while ( IsValid && texture.IsValid() && !texture.IsError )
			{
				var source = texture;
				var z = (int)slice.Value;
				try
				{
					var result = await Task.Run( () => ReadPreview( source, z ) );
					if ( !IsValid ) return;
					if ( texture != source || (int)slice.Value != z ) continue;

					(previewSize, pixels) = result;
					UpdateChannel();
				}
				catch ( Exception e )
				{
					if ( !IsValid ) return;
					if ( texture != source || (int)slice.Value != z ) continue;

					status = "Preview unavailable";
					image.ToolTip = e.Message;
					image.Update();
				}
				break;
			}
		}
		finally
		{
			reading = false;
		}
	}

	static (Vector2Int Size, Color32[] Pixels) ReadPreview( Texture source, int z )
	{
		var width = source.Width;
		var height = source.Height;
		var scale = Math.Min( 1f, 512f / Math.Max( width, height ) );
		var size = new Vector2Int( Math.Max( 1, (int)(width * scale) ), Math.Max( 1, (int)(height * scale) ) );

		// GetPixels converts the format but doesn't rescale. Sample the full slice so light indices stay exact.
		var data = new Color32[width * height];
		source.GetPixels( (0, 0, width, height), z, 0, data.AsSpan(), ImageFormat.RGBA8888 );
		if ( size.x == width && size.y == height ) return (size, data);

		var small = new Color32[size.x * size.y];
		for ( int y = 0; y < size.y; y++ )
		{
			int row = y * height / size.y * width;
			for ( int x = 0; x < size.x; x++ )
				small[y * size.x + x] = data[row + x * width / size.x];
		}

		return (size, small);
	}

	void UpdateChannel()
	{
		if ( pixels is null ) return;

		var channel = channels.CurrentIndex;
		var display = new Color32[pixels.Length];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			var p = pixels[i];
			var value = channel switch { 1 => p.r, 2 => p.g, 3 => p.b, _ => p.a };
			display[i] = channel == 0 ? new Color32( p.r, p.g, p.b ) : new Color32( value, value, value );
		}

		pixmap ??= new Pixmap( previewSize.x, previewSize.y );
		if ( !pixmap.UpdateFromPixels( MemoryMarshal.AsBytes( display.AsSpan() ), previewSize, ImageFormat.RGBA8888 ) )
		{
			pixmap = null;
			status = "Preview unavailable";
		}

		image.ToolTip = texture.ResourcePath;
		image.Update();
	}

	Rect ImageRect()
	{
		var rect = image.LocalRect.Shrink( 4 );
		var scale = Math.Min( rect.Width / previewSize.x, rect.Height / previewSize.y );
		return rect.Align( new Vector2( previewSize.x, previewSize.y ) * scale, TextFlag.Center );
	}

	void InspectPixel( Vector2 position )
	{
		if ( pixmap is null ) return;

		var rect = ImageRect();
		if ( !rect.IsInside( position ) )
		{
			image.ToolTip = texture.ResourcePath;
			return;
		}

		var x = Math.Clamp( (int)((position.x - rect.Left) * previewSize.x / rect.Width), 0, previewSize.x - 1 );
		var y = Math.Clamp( (int)((position.y - rect.Top) * previewSize.y / rect.Height), 0, previewSize.y - 1 );
		var p = pixels[y * previewSize.x + x];
		image.ToolTip = $"Texel ({x * texture.Width / previewSize.x}, {y * texture.Height / previewSize.y})\nPreview RGBA8   {p.r}, {p.g}, {p.b}, {p.a}";
	}

	bool PaintImage()
	{
		var rect = image.LocalRect;
		Paint.SetBrushAndPen( Theme.ControlBackground );
		Paint.DrawRect( rect, Theme.ControlRadius );
		if ( pixmap is null )
		{
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( rect, status );
			return true;
		}

		Paint.Draw( ImageRect(), pixmap );
		return true;
	}
}
