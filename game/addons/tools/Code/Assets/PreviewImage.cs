using System;

namespace Editor.Assets;

[AssetPreview( "jpg" )]
[AssetPreview( "vtex" )]
class PreviewImage : AssetPreview
{
	internal Texture Texture { get; set; }

	public enum TextureChannel
	{
		[Title( "RGBA" ), Icon( "filter_none" )]
		Rgba,
		[Icon( "circle" )]
		Red,
		[Icon( "circle" )]
		Green,
		[Icon( "circle" )]
		Blue,
		[Icon( "opacity" )]
		Alpha
	}

	SpriteRenderer _sprite;
	readonly Texture[] _mipTextures;
	readonly Dictionary<(int Mip, TextureChannel Channel), Texture> _channelTextures = new();
	const float QuadSize = 16f;

	public override bool IsAnimatedPreview => false;

	public int MipCount => _mipTextures.Length;

	public int PreviewMip
	{
		get;
		set
		{
			field = Math.Clamp( value, 0, _mipTextures.Length - 1 );
			RefreshSprite();
		}
	}

	public TextureChannel PreviewChannel
	{
		get;
		set
		{
			field = value;
			RefreshSprite();
		}
	}

	public PreviewImage( Asset asset ) : base( asset )
	{
		Texture = Texture.Load( asset.AssetType == AssetType.ImageFile ? Asset.GetSourceFile() : Asset.Path );
		_mipTextures = new Texture[Math.Max( 1, Texture?.Mips ?? 1 )];
	}

	public override Task InitializeAsset()
	{
		using ( Scene.Push() )
		{
			if ( !IsRenderingThumbnail )
			{
				var background = new GameObject( true, "checkerboard" );
				background.WorldPosition = Vector3.Forward;
				AddSprite( background, GetCheckerboardTexture() );
			}

			PrimaryObject = new GameObject( true, "texture" );
			PrimaryObject.WorldTransform = Transform.Zero;
			_sprite = AddSprite( PrimaryObject, Texture );

			Camera.Orthographic = true;
			Camera.OrthographicHeight = QuadSize;
		}

		return Task.CompletedTask;
	}

	static SpriteRenderer AddSprite( GameObject go, Texture texture )
	{
		var sprite = go.AddComponent<SpriteRenderer>();
		sprite.Sprite = MakeSprite( texture );
		sprite.Size = new Vector2( QuadSize, QuadSize );
		return sprite;
	}

	static Sprite MakeSprite( Texture texture ) => new()
	{
		Animations =
		[
			new() { Name = "Default", Frames = [ new Sprite.Frame { Texture = texture } ] }
		]
	};

	public override void Dispose()
	{
		foreach ( var texture in _mipTextures )
			texture?.Dispose();

		foreach ( var texture in _channelTextures.Values )
			texture?.Dispose();

		base.Dispose();
	}

	void RefreshSprite()
	{
		if ( !_sprite.IsValid() )
			return;

		var texture = PreviewChannel == TextureChannel.Rgba
			? GetMipTexture( PreviewMip )
			: GetChannelTexture( PreviewMip, PreviewChannel );

		_sprite.Sprite = MakeSprite( texture );
	}

	Texture GetMipTexture( int mip )
	{
		if ( mip == 0 )
			return Texture;

		if ( _mipTextures[mip] is { } cached )
			return cached;

		using var bitmap = GetMipBitmap( mip );
		if ( bitmap is not null )
			_mipTextures[mip] = bitmap.ToTexture( false );

		return _mipTextures[mip] ?? Texture;
	}

	Texture GetChannelTexture( int mip, TextureChannel channel )
	{
		if ( _channelTextures.TryGetValue( (mip, channel), out var cached ) )
			return cached;

		using var bitmap = GetMipBitmap( mip );
		if ( bitmap is null )
			return GetMipTexture( mip );

		var pixels = bitmap.GetPixels();

		for ( var i = 0; i < pixels.Length; i++ )
		{
			var p = pixels[i];
			pixels[i] = channel switch
			{
				TextureChannel.Red => new Color( p.r, p.r, p.r, 1f ),
				TextureChannel.Green => new Color( p.g, p.g, p.g, 1f ),
				TextureChannel.Blue => new Color( p.b, p.b, p.b, 1f ),
				TextureChannel.Alpha => new Color( p.a, p.a, p.a, 1f ),
				_ => p
			};
		}

		bitmap.SetPixels( pixels );
		return _channelTextures[(mip, channel)] = bitmap.ToTexture( false );
	}

	Bitmap GetMipBitmap( int mip )
	{
		using ( EditorUtility.DisableTextureStreaming() )
		{
			Texture.MarkUsed();
			return Texture.GetBitmap( mip );
		}
	}

	public (int Width, int Height) MipSizeAt( int mip )
		=> (Math.Max( 1, Texture.Width >> mip ), Math.Max( 1, Texture.Height >> mip ));

	static Texture _checkerboard;

	static Texture GetCheckerboardTexture()
	{
		if ( _checkerboard is not null )
			return _checkerboard;

		const int cells = 16;
		const int cellSize = 8;
		const int size = cells * cellSize;

		var light = new Color32( 82, 82, 82 );
		var dark = new Color32( 52, 52, 52 );

		var data = new byte[size * size * 4];

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				var c = ((x / cellSize) + (y / cellSize)) % 2 == 0 ? light : dark;
				var i = (y * size + x) * 4;
				data[i + 0] = c.r;
				data[i + 1] = c.g;
				data[i + 2] = c.b;
				data[i + 3] = 255;
			}
		}

		_checkerboard = Texture.Create( size, size )
			.WithData( data )
			.WithName( "asset_preview_checkerboard" )
			.Finish();

		return _checkerboard;
	}

	public override void UpdateScene( float cycle, float timeStep )
	{
		base.UpdateScene( cycle, timeStep );

		Camera.Orthographic = true;
		Camera.OrthographicHeight = QuadSize;
		Camera.WorldPosition = Vector3.Forward * -200;
		Camera.WorldRotation = Rotation.LookAt( Vector3.Forward );
	}

	public override Widget CreateToolbar()
	{
		if ( Texture is null )
			return null;

		return new MipToolbar( this );
	}
}

file sealed class MipToolbar : Widget
{
	public MipToolbar( PreviewImage preview ) : base( null )
	{
		var controlHeight = Theme.RowHeight + 8;
		FixedHeight = controlHeight + 12;

		Layout = Layout.Row();
		Layout.Margin = new Margin( 12, 6, 12, 6 );
		Layout.Spacing = 8;

		var mips = new ComboBox( this );
		mips.FixedHeight = controlHeight;
		foreach ( var mip in Enumerable.Range( 0, preview.MipCount ) )
		{
			var (w, h) = preview.MipSizeAt( mip );
			mips.AddItem( $"Mip {mip} ({w}×{h})", "photo_size_select_large", () => preview.PreviewMip = mip, selected: mip == 0 );
		}
		Layout.Add( mips, 1 );

		var channel = ControlWidget.Create( preview.GetSerialized().GetProperty( nameof( PreviewImage.PreviewChannel ) ) );
		channel.MaximumWidth = 90;
		channel.FixedHeight = controlHeight;
		channel.ToolTip = "Channel";
		Layout.Add( channel );
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
