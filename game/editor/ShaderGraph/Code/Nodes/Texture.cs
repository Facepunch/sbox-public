namespace Editor.ShaderGraph.Nodes;

public abstract class TextureSamplerBase : ShaderNode, ITextureParameterNode, IErroringNode
{
	[Hide]
	protected bool IsSubgraph => Graph is ShaderGraph graph && graph.IsSubgraph;

	private bool _showDefaults;
	[JsonIgnore, Hide]
	protected bool ShowDefaults
	{
		get => _showDefaults;
		set
		{

			if ( IsSubgraph )
			{
				_showDefaults = false;
			}
			else
			{
				_showDefaults = value;
			}
		}
	}

	[ShowIf( nameof( ShowDefaults ), true )]
	public string Name { get; set; }

	/// <summary>
	/// Texture to sample in preview
	/// </summary>
	[ImageAssetPath]
	[ShowIf( nameof( ShowDefaults ), true )]
	public string Image
	{
		get => _image;
		set
		{
			_image = value;
			_asset = AssetSystem.FindByPath( _image );

			if ( _asset == null )
				return;

			CompileTexture();
		}
	}

	private Asset _asset;
	[JsonIgnore, Hide]
	protected Asset Asset
	{
		get => _asset;
		set
		{
			_asset = value;
		}
	}

	private string _texture;
	private string _image;
	private string _resourceText;

	[JsonIgnore, Hide]
	protected string TexturePath => _texture;

	/// <summary>
	/// How the texture is filtered and wrapped when sampled
	/// </summary>
	[InlineEditor( Label = false ), Group( "Sampler" )]
	public Sampler Sampler { get; set; }

	protected void CompileTexture()
	{
		if ( _asset == null )
			return;

		if ( string.IsNullOrWhiteSpace( _image ) )
			return;

		var ui = UI;
		ui.DefaultTexture = _image;
		UI = ui;

		var resourceText = string.Format( ShaderTemplate.TextureDefinition,
			_image,
			UI.ColorSpace,
			UI.ImageFormat,
			UI.Processor );

		if ( _resourceText == resourceText )
			return;

		_resourceText = resourceText;

		var assetPath = $"shadergraph/{_image.Replace( ".", "_" )}_shadergraph.generated.vtex";
		var resourcePath = FileSystem.Root.GetFullPath( "/.source2/temp" );
		resourcePath = System.IO.Path.Combine( resourcePath, assetPath );

		if ( AssetSystem.CompileResource( resourcePath, resourceText ) )
		{
			_texture = assetPath;
		}
		else
		{
			Log.Warning( $"Failed to compile {_image}" );
		}
	}

	/// <summary>
	/// Settings for how this texture shows up in material editor
	/// </summary>
	[InlineEditor( Label = false ), Group( "UI" )]
	[ShowIf( nameof( ShowDefaults ), true )]
	public TextureInput UI { get; set; } = new TextureInput
	{
		ImageFormat = TextureFormat.DXT5,
		SrgbRead = true,
		Default = Color.White,
	};

	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( UI.Name ) ? null :
		$"{DisplayInfo.For( this ).Name} {(ShowDefaults ? $"( {UI.Name} )" : Name)}";

	protected TextureSamplerBase() : base()
	{
		Image = "materials/dev/white_color.tga";
		ExpandSize = new Vector2( 8, 8 + Inputs.Count() * 24 );
	}

	protected void SetNodeWidth( float width )
	{
		ExpandSize = ExpandSize.WithX( width );
		Update();
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Align( 130, TextFlag.LeftBottom ).Shrink( 3 );

		Paint.SetBrush( "/image/transparent-small.png" );
		Paint.DrawRect( rect.Shrink( 2 ), 2 );

		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.7f ) );
		Paint.DrawRect( rect, 2 );

		if ( Asset != null )
		{
			Paint.Draw( rect.Shrink( 2 ), Asset.GetAssetThumb( true ) );
		}
	}

	protected NodeResult Component( string component, GraphCompiler compiler )
	{
		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return result.IsValid ? new( NodeResultType.Float, $"{result}.{component}", true ) : new( NodeResultType.Float, "0.0f", true );
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		if ( Graph is ShaderGraph sg && sg.IsSubgraph )
		{
		}

		return errors;
	}

	protected bool ProcessTexture2DInput( GraphCompiler compiler, NodeInput texture2D, ref TextureInput input, out NodeResult errorResult )
	{
		errorResult = default;
		var texture2DInput = compiler.Result( texture2D );
		if ( texture2DInput.IsValid )
		{
			if ( texture2DInput.ResultType != NodeResultType.Texture2D )
			{
				ErrorMessage = $"Input of `{nameof( texture2D )}` must be of ResultType `{NodeResultType.Texture2D}`";
				errorResult = NodeResult.Error( ErrorMessage );

				return false;
			}

			ClearError();

			if ( compiler.TryGetPreviewImage( texture2DInput.Code, out var imagePath ) )
			{

				Image = imagePath;
				Name = "";
				input.Name = texture2DInput.Code.TrimStart( "g_t" ).ToString();
				ShowDefaults = false;

				SetNodeWidth( 8 );

				return true;
			}
			else
			{
				throw new Exception( $"Cannot find PreviewImage for texture input : {texture2DInput.Code}" );
			}
		}
		else
		{
			if ( !IsSubgraph )
			{
				ClearError();

				Image = "materials/dev/white_color.tga";
				input.Name = Name;
				ShowDefaults = true;

				SetNodeWidth( 64 );
				UI = UI with { Name = Name };

				return true;
			}
			else if ( IsSubgraph )
			{
				Image = "materials/dev/white_color.tga";
				ErrorMessage = $"Missing required input '{nameof( texture2D )}'.";
				errorResult = NodeResult.MissingInput( nameof( texture2D ) );

				return false;
			}
		}

		return false;
	}
}

/// <summary>
/// Sample a 2D Texture
/// </summary>
[Title( "Texture 2D Sampler" ), Category( "Textures" ), Icon( "image" )]
public sealed class TextureSampler : TextureSamplerBase
{
	/// <summary>
	/// Texture2D to sample from.
	/// </summary>
	[Title( "Texture2D" )]
	[Input( typeof( Texture ) )]
	[Hide]
	public NodeInput Texture2D { get; set; }

	/// <summary>
	/// Coordinates to sample this texture (Defaults to vertex coordinates)
	/// </summary>
	[Title( "Coordinates" )]
	[Input( typeof( Vector2 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	/// <summary>
	/// RGBA color result
	/// </summary>
	[Hide]
	[Output( typeof( Color ) ), Title( "RGBA" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.Tex2D;

		if ( !ProcessTexture2DInput( compiler, Texture2D, ref input, out var errorResult ) )
		{
			return errorResult;
		}

		CompileTexture();

		var texture = string.IsNullOrWhiteSpace( TexturePath ) ? null : Texture.Load( TexturePath );
		texture ??= Texture.White;

		var sampler = compiler.ResultSampler( Sampler );
		var textureGlobal = compiler.ResultTexture( input, texture );
		var coords = compiler.Result( Coords );

		if ( compiler.Stage == GraphCompiler.ShaderStage.Vertex )
		{
			return new NodeResult( NodeResultType.Color, $"{textureGlobal}.SampleLevel(" +
				$" g_sSampler{sampler}," +
				$" {(coords.IsValid ? $"{coords.Cast( 2 )}" : "i.vTextureCoords.xy")}, 0 )" );
		}
		else
		{
			return new NodeResult( NodeResultType.Color, $"Tex2DS( {textureGlobal}," +
				$" g_sSampler{sampler}," +
				$" {(coords.IsValid ? $"{coords.Cast( 2 )}" : "i.vTextureCoords.xy")} )" );
		}
	};

	/// <summary>
	/// Red component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "R" )]
	public NodeResult.Func R => ( GraphCompiler compiler ) => Component( "r", compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "G" )]
	public NodeResult.Func G => ( GraphCompiler compiler ) => Component( "g", compiler );

	/// <summary>
	/// Blue component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "B" )]
	public NodeResult.Func B => ( GraphCompiler compiler ) => Component( "b", compiler );

	/// <summary>
	/// Alpha (Opacity) component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "A" )]
	public NodeResult.Func A => ( GraphCompiler compiler ) => Component( "a", compiler );
}

/// <summary>
/// Sample a Cube Texture
/// </summary>
[Title( "Texture Cube Sampler" ), Category( "Textures" ), Icon( "view_in_ar" )]
public sealed class TextureCube : ShaderNode
{
	/// <summary>
	/// Coordinates to sample this cubemap
	/// </summary>
	[Title( "Coordinates" )]
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	/// <summary>
	/// Texture to sample in preview
	/// </summary>
	[ImageAssetPath]
	public string Texture { get; set; }

	/// <summary>
	/// How the texture is filtered and wrapped when sampled
	/// </summary>
	[InlineEditor( Label = false ), Group( "Sampler" )]
	public Sampler Sampler { get; set; }

	/// <summary>
	/// Settings for how this texture shows up in material editor
	/// </summary>
	[InlineEditor( Label = false ), Group( "UI" )]
	public TextureInput UI { get; set; } = new TextureInput
	{
		ImageFormat = TextureFormat.DXT5,
		SrgbRead = true,
		Default = Color.White,
	};

	public TextureCube() : base()
	{
		Texture = "materials/skybox/skybox_workshop.vtex";
		ExpandSize = new Vector2( 0, 8 + Inputs.Count() * 24 );
	}

	public override void OnPaint( Rect rect )
	{
		rect = rect.Align( 130, TextFlag.LeftBottom ).Shrink( 3 );

		Paint.SetBrush( "/image/transparent-small.png" );
		Paint.DrawRect( rect.Shrink( 2 ), 2 );

		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.7f ) );
		Paint.DrawRect( rect, 2 );

		if ( !string.IsNullOrEmpty( Texture ) )
		{
			var tex = Sandbox.Texture.Find( Texture );
			if ( tex is null ) return;
			var pixmap = Pixmap.FromTexture( tex );
			Paint.Draw( rect.Shrink( 2 ), pixmap );
		}
	}

	/// <summary>
	/// RGBA color result
	/// </summary>
	[Hide]
	[Output( typeof( Color ) ), Title( "RGBA" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.TexCube;

		var sampler = compiler.ResultSampler( Sampler );
		var textureGlobal = compiler.ResultTexture( input, Sandbox.Texture.Load( Texture ) );
		var coords = compiler.Result( Coords );

		return new NodeResult( NodeResultType.Color, $"TexCubeS( {textureGlobal}," +
			$" g_sSampler{sampler}," +
			$" {(coords.IsValid ? $"{coords.Cast( 3 )}" : ViewDirection.Result.Invoke( compiler ))} )" );
	};

	private NodeResult Component( string component, GraphCompiler compiler )
	{
		var result = compiler.Result( new NodeInput { Identifier = Identifier, Output = nameof( Result ) } );
		return result.IsValid ? new( NodeResultType.Float, $"{result}.{component}", true ) : new( NodeResultType.Float, "0.0f", true );
	}

	/// <summary>
	/// Red component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "R" )]
	public NodeResult.Func R => ( GraphCompiler compiler ) => Component( "r", compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "G" )]
	public NodeResult.Func G => ( GraphCompiler compiler ) => Component( "g", compiler );

	/// <summary>
	/// Blue component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "B" )]
	public NodeResult.Func B => ( GraphCompiler compiler ) => Component( "b", compiler );

	/// <summary>
	/// Alpha (Opacity) component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "A" )]
	public NodeResult.Func A => ( GraphCompiler compiler ) => Component( "a", compiler );
}

/// <summary>
/// Sample a 2D texture from 3 directions, then blend based on a normal vector.
/// </summary>
[Title( "Texture 2D Triplanar Sampler" ), Category( "Textures" ), Icon( "photo_library" )]
public sealed class TextureTriplanar : TextureSamplerBase
{
	/// <summary>
	/// Texture2D to sample from.
	/// </summary>
	[Title( "Texture2D" )]
	[Input( typeof( Texture ) )]
	[Hide]
	public NodeInput Texture2D { get; set; }

	/// <summary>
	/// Coordinates to sample this texture (Defaults to vertex position)
	/// </summary>
	[Title( "Coordinates" )]
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	/// <summary>
	/// Normal to use when blending between each sampled direction (Defaults to vertex normal)
	/// </summary>
	[Title( "Normal" )]
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Normal { get; set; }

	/// <summary>
	/// RGBA color result
	/// </summary>
	[Hide]
	[Output( typeof( Color ) ), Title( "RGBA" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.Tex2D;

		if ( !ProcessTexture2DInput( compiler, Texture2D, ref input, out var errorResult ) )
		{
			return errorResult;
		}

		CompileTexture();

		var texture = string.IsNullOrWhiteSpace( TexturePath ) ? null : Texture.Load( TexturePath );
		texture ??= Texture.White;

		var sampler = compiler.ResultSampler( Sampler );
		var textureGlobal = compiler.ResultTexture( input, texture );
		var coords = compiler.Result( Coords );
		var normal = compiler.Result( Normal );

		var result = compiler.ResultFunction( "TexTriplanar_Color",
			textureGlobal,
			$"g_sSampler{sampler}",
			coords.IsValid ? coords.Cast( 3 ) : "(i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz) / 39.3701",
			normal.IsValid ? normal.Cast( 3 ) : "normalize( i.vNormalWs.xyz )" );

		return new NodeResult( NodeResultType.Color, result );
	};

	/// <summary>
	/// Red component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "R" )]
	public NodeResult.Func R => ( GraphCompiler compiler ) => Component( "r", compiler );

	/// <summary>
	/// Green component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "G" )]
	public NodeResult.Func G => ( GraphCompiler compiler ) => Component( "g", compiler );

	/// <summary>
	/// Blue component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "B" )]
	public NodeResult.Func B => ( GraphCompiler compiler ) => Component( "b", compiler );

	/// <summary>
	/// Alpha (Opacity) component of result
	/// </summary>
	[Output( typeof( float ) ), Hide, Title( "A" )]
	public NodeResult.Func A => ( GraphCompiler compiler ) => Component( "a", compiler );
}

/// <summary>
/// Sample a 2D texture from 3 directions, then blend based on a normal vector.
/// </summary>
[Title( "Texture 2D Normal Map Triplanar Sampler" ), Category( "Textures" ), Icon( "texture" )]
public sealed class NormapMapTriplanar : TextureSamplerBase
{
	/// <summary>
	/// Texture2D to sample from.
	/// </summary>
	[Title( "Texture2D" )]
	[Input( typeof( Texture ) )]
	[Hide]
	public NodeInput Texture2D { get; set; }

	/// <summary>
	/// Coordinates to sample this texture (Defaults to vertex position)
	/// </summary>
	[Title( "Coordinates" )]
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Coords { get; set; }

	/// <summary>
	/// Normal to use when blending between each sampled direction (Defaults to vertex normal)
	/// </summary>
	[Title( "Normal" )]
	[Input( typeof( Vector3 ) )]
	[Hide]
	public NodeInput Normal { get; set; }

	public NormapMapTriplanar()
	{
		ExpandSize = new Vector2( 56f, 128f );

		UI = new TextureInput
		{
			ImageFormat = TextureFormat.DXT5,
			SrgbRead = false,
			ColorSpace = TextureColorSpace.Linear,
			Extension = TextureExtension.Normal,
			Processor = TextureProcessor.NormalizeNormals,
			Default = new Color( 0.5f, 0.5f, 1f, 1f )
		};
	}

	/// <summary>
	/// RGBA color result
	/// </summary>
	[Hide]
	[Output( typeof( Vector3 ) ), Title( "XYZ" )]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		var input = UI;
		input.Type = TextureType.Tex2D;

		if ( !ProcessTexture2DInput( compiler, Texture2D, ref input, out var errorResult ) )
		{
			return errorResult;
		}

		CompileTexture();

		var texture = string.IsNullOrWhiteSpace( TexturePath ) ? null : Texture.Load( TexturePath );
		texture ??= Texture.White;

		var sampler = compiler.ResultSampler( Sampler );
		var textureGlobal = compiler.ResultTexture( input, texture );
		var coords = compiler.Result( Coords );
		var normal = compiler.Result( Normal );

		var result = compiler.ResultFunction( "TexTriplanar_Normal",
			textureGlobal,
			$"g_sSampler{sampler}",
			coords.IsValid ? coords.Cast( 3 ) : "(i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz) / 39.3701",
			normal.IsValid ? normal.Cast( 3 ) : "normalize( i.vNormalWs.xyz )" );

		return new NodeResult( NodeResultType.Vector3, result );
	};
}

/// <summary>
/// Texture Coordinate from vertex data
/// </summary>
[Title( "Texture Coordinate" ), Category( "Variables" ), Icon( "texture" )]
public sealed class TextureCoord : ShaderNode
{
	/// <summary>
	/// Use the secondary vertex coordinate
	/// </summary>
	public bool UseSecondaryCoord { get; set; } = false;

	/// <summary>
	/// How many times this coordinate repeats itself to give a tiled effect
	/// </summary>
	public Vector2 Tiling { get; set; } = 1;

	[Hide]
	public override string Title => $"{DisplayInfo.For( this ).Name}{(UseSecondaryCoord ? " 2" : "")}";

	/// <summary>
	/// Coordinate result
	/// </summary>
	[Output( typeof( Vector2 ) )]
	[Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		if ( compiler.IsPreview )
		{
			var result = $"{compiler.ResultValue( UseSecondaryCoord )} ? i.vTextureCoords.zw : i.vTextureCoords.xy";
			return new( NodeResultType.Vector2, $"{compiler.ResultValue( Tiling.IsNearZeroLength )} ? {result} : ({result}) * {compiler.ResultValue( Tiling )}" );
		}
		else
		{
			var result = UseSecondaryCoord ? "i.vTextureCoords.zw" : "i.vTextureCoords.xy";
			return Tiling.IsNearZeroLength ? new( NodeResultType.Vector2, result ) : new( NodeResultType.Vector2, $"{result} * {compiler.ResultValue( Tiling )}" );
		}
	};
}
