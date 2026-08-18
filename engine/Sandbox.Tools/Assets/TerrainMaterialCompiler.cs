using Sandbox.Resources;
using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Sandbox;

[Expose]
[ResourceIdentity( "tmat" )]
internal class TerrainMaterialCompiler : ResourceCompiler
{
	/// <summary>
	/// The two textures the terrain samples. Every source image is baked - with whatever
	/// adjustments it was set up with - into one of these on the CPU, then handed to the
	/// texture compiler for mips and block compression.
	/// </summary>
	const string BCRSuffix = "_tmat_bcr.generated";
	const string NHOSuffix = "_tmat_nho.generated";

	protected override async Task<bool> Compile()
	{
		var filename = Context.AbsolutePath;
		var jsonString = File.ReadAllText( filename );

		var docOptions = new JsonDocumentOptions();
		docOptions.MaxDepth = 512;

		using var doc = JsonDocument.Parse( jsonString, docOptions );

		var path = Path.GetDirectoryName( filename );
		var file = Path.GetFileNameWithoutExtension( filename );

		var root = doc.RootElement;

		using var albedo = await LoadSourceImage( root, nameof( TerrainMaterial.AlbedoImage ), TerrainMaterial.DefaultAlbedoImage );
		using var roughness = await LoadSourceImage( root, nameof( TerrainMaterial.RoughnessImage ), TerrainMaterial.DefaultRoughnessImage );
		using var normal = await LoadSourceImage( root, nameof( TerrainMaterial.NormalImage ), TerrainMaterial.DefaultNormalImage );
		using var height = await LoadSourceImage( root, nameof( TerrainMaterial.HeightImage ), TerrainMaterial.DefaultHeightImage );
		using var ao = await LoadSourceImage( root, nameof( TerrainMaterial.AOImage ), TerrainMaterial.DefaultAOImage );

		//
		// Albedo in rgb, roughness in alpha
		//
		{
			using var packed = Bitmap.PackChannels(
				(albedo, Bitmap.ColorChannel.Red, Bitmap.ColorChannel.Red),
				(albedo, Bitmap.ColorChannel.Green, Bitmap.ColorChannel.Green),
				(albedo, Bitmap.ColorChannel.Blue, Bitmap.ColorChannel.Blue),
				(roughness, Bitmap.ColorChannel.Red, Bitmap.ColorChannel.Alpha) );

			WritePacked( path, file, BCRSuffix, packed );

			var childContext = Context.CreateChild( $"{path}/{file}{BCRSuffix}.vtex" );
			childContext.SetInputData( BuildTextureDefinition( RelativeGeneratedPath( file, BCRSuffix ),
				[
					( "color", "srgb", "rgb", "rgb" ),
					( "roughness", "linear", "a", "a" ),
				] ) );
			childContext.Compile();
		}

		//
		// Normal in rg, height in b, ambient occlusion in alpha
		//
		{
			using var packed = Bitmap.PackChannels(
				(normal, Bitmap.ColorChannel.Red, Bitmap.ColorChannel.Red),
				(normal, Bitmap.ColorChannel.Green, Bitmap.ColorChannel.Green),
				(height, Bitmap.ColorChannel.Red, Bitmap.ColorChannel.Blue),
				(ao, Bitmap.ColorChannel.Red, Bitmap.ColorChannel.Alpha) );

			WritePacked( path, file, NHOSuffix, packed );

			var childContext = Context.CreateChild( $"{path}/{file}{NHOSuffix}.vtex" );
			childContext.SetInputData( BuildTextureDefinition( RelativeGeneratedPath( file, NHOSuffix ),
				[
					( "nho", "linear", "rgba", "rgba" ),
				] ) );
			childContext.Compile();
		}

		Context.Data.Write( jsonString );
		return true;
	}

	/// <summary>
	/// Run a source image slot through its own image generator, so what gets packed is what the
	/// material was set up with - adjustments, cropping, height to normal and all.
	/// </summary>
	async Task<Bitmap> LoadSourceImage( JsonElement root, string name, string defaultPath )
	{
		var generator = ReadGenerator( root, name, defaultPath );

		Context.AddCompileReference( generator.FilePath );

		var bitmap = await generator.CreateBitmap( ResourceGenerator.Options.Default, default );
		if ( bitmap is null )
			throw new Exception( $"{name}: couldn't load the image '{generator.FilePath}'" );

		return bitmap;
	}

	/// <summary>
	/// The image generator behind a source image slot. An empty slot falls back to its default
	/// image, and anything that isn't an image file can't be packed.
	/// </summary>
	static ImageFileGenerator ReadGenerator( JsonElement root, string name, string defaultPath )
	{
		if ( !root.TryGetProperty( name, out var value ) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined )
			return new ImageFileGenerator { FilePath = defaultPath };

		// Pre-v1 materials stored a bare image path
		if ( value.ValueKind == JsonValueKind.String )
		{
			var legacyPath = value.GetString();
			return new ImageFileGenerator { FilePath = string.IsNullOrWhiteSpace( legacyPath ) ? defaultPath : legacyPath };
		}

		if ( value.ValueKind != JsonValueKind.Object )
			throw new Exception( $"{name} should be a texture, but it's a {value.ValueKind}" );

		var source = value.TryGetProperty( "$source", out var sourceValue ) ? sourceValue.GetString() : null;
		if ( source != "imagefile" )
			throw new Exception( $"{name} is a '{source}' texture. A terrain material bakes its source images into its own packed textures, so this slot has to be an image file." );

		if ( !value.TryGetProperty( "data", out var data ) )
			return new ImageFileGenerator { FilePath = defaultPath };

		// Use the engine serializer, the generator's properties need its converters
		var generator = Json.Deserialize<ImageFileGenerator>( data.GetRawText() ) ?? new ImageFileGenerator();

		if ( string.IsNullOrWhiteSpace( generator.FilePath ) )
			return new ImageFileGenerator { FilePath = defaultPath };

		if ( generator.FilePath.EndsWith( ".vtex", StringComparison.OrdinalIgnoreCase ) || generator.FilePath.EndsWith( ".vtex_c", StringComparison.OrdinalIgnoreCase ) )
			throw new Exception( $"{name} points at a compiled texture ({generator.FilePath}). Point it at the source image instead - it gets baked into the material's own packed texture." );

		return generator;
	}

	/// <summary>
	/// The packed image goes next to the material as a png - the texture compiler works from
	/// files, and this is also the thing to look at when a material doesn't look right.
	/// </summary>
	static void WritePacked( string path, string file, string suffix, Bitmap packed )
	{
		File.WriteAllBytes( $"{path}/{file}{suffix}.png", packed.ToPng() );
	}

	string RelativeGeneratedPath( string file, string suffix )
	{
		var directory = Path.GetDirectoryName( Context.RelativePath )?.Replace( "\\", "/" );

		return string.IsNullOrEmpty( directory )
			? $"{file}{suffix}.png"
			: $"{directory}/{file}{suffix}.png";
	}

	//
	// Templates cause I don't want to spend hours binding dmx for sometihng we might hate
	//

	/// <summary>
	/// Build the vtex definition for one packed image. Every channel set reads the same file,
	/// they just differ in which channels they take and what color space those are in.
	/// </summary>
	static string BuildTextureDefinition( string filePath, (string Name, string ColorSpace, string SrcChannels, string DstChannels)[] channels )
	{
		var builder = new StringBuilder();

		builder.AppendLine( "<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->" );
		builder.AppendLine( "\"CDmeVtex\"" );
		builder.AppendLine( "{" );

		builder.AppendLine( "\t\"m_inputTextureArray\" \"element_array\"" );
		builder.AppendLine( "\t[" );

		for ( int i = 0; i < channels.Length; i++ )
		{
			builder.AppendLine( "\t\t\"CDmeInputTexture\"" );
			builder.AppendLine( "\t\t{" );
			builder.AppendLine( $"\t\t\t\"m_name\" \"string\" \"{channels[i].Name}\"" );
			builder.AppendLine( $"\t\t\t\"m_fileName\" \"string\" \"{filePath}\"" );
			builder.AppendLine( $"\t\t\t\"m_colorSpace\" \"string\" \"{channels[i].ColorSpace}\"" );
			builder.AppendLine( "\t\t\t\"m_typeString\" \"string\" \"2D\"" );
			builder.AppendLine( "\t\t\t\"m_imageProcessorArray\" \"element_array\"" );
			builder.AppendLine( "\t\t\t[" );
			builder.AppendLine( "\t\t\t]" );
			builder.AppendLine( i == channels.Length - 1 ? "\t\t}" : "\t\t}," );
		}

		builder.AppendLine( "\t]" );

		builder.AppendLine( "\t\"m_outputTypeString\" \"string\" \"2D\"" );
		builder.AppendLine( "\t\"m_outputFormat\" \"string\" \"BC7\"" );
		builder.AppendLine( "\t\"m_outputClearColor\" \"vector4\" \"0 0 0 0\"" );
		builder.AppendLine( "\t\"m_nOutputMinDimension\" \"int\" \"0\"" );
		builder.AppendLine( "\t\"m_nOutputMaxDimension\" \"int\" \"0\"" );

		builder.AppendLine( "\t\"m_textureOutputChannelArray\" \"element_array\"" );
		builder.AppendLine( "\t[" );

		for ( int i = 0; i < channels.Length; i++ )
		{
			var (name, colorSpace, srcChannels, dstChannels) = channels[i];

			builder.AppendLine( "\t\t\"CDmeTextureOutputChannel\"" );
			builder.AppendLine( "\t\t{" );
			builder.AppendLine( "\t\t\t\"m_inputTextureArray\" \"string_array\"" );
			builder.AppendLine( "\t\t\t[" );
			builder.AppendLine( $"\t\t\t\t\"{name}\"" );
			builder.AppendLine( "\t\t\t]" );
			builder.AppendLine( $"\t\t\t\"m_srcChannels\" \"string\" \"{srcChannels}\"" );
			builder.AppendLine( $"\t\t\t\"m_dstChannels\" \"string\" \"{dstChannels}\"" );
			builder.AppendLine( "\t\t\t\"m_mipAlgorithm\" \"CDmeImageProcessor\"" );
			builder.AppendLine( "\t\t\t{" );
			builder.AppendLine( "\t\t\t\t\"m_algorithm\" \"string\" \"Box\"" );
			builder.AppendLine( "\t\t\t\t\"m_stringArg\" \"string\" \"\"" );
			builder.AppendLine( "\t\t\t\t\"m_vFloat4Arg\" \"vector4\" \"0 0 0 0\"" );
			builder.AppendLine( "\t\t\t}" );
			builder.AppendLine( $"\t\t\t\"m_outputColorSpace\" \"string\" \"{colorSpace}\"" );
			builder.AppendLine( i == channels.Length - 1 ? "\t\t}" : "\t\t}," );
		}

		builder.AppendLine( "\t]" );
		builder.AppendLine( "\t\"m_vClamp\" \"vector3\" \"0 0 0\"" );
		builder.AppendLine( "\t\"m_bNoLod\" \"bool\" \"0\"" );
		builder.Append( "}" );

		return builder.ToString();
	}
}
