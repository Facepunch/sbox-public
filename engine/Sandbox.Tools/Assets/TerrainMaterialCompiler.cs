using Sandbox.Resources;
using System.IO;
using System.Text.Json;

namespace Sandbox;

[Expose]
[ResourceIdentity( "tmat" )]
internal class TerrainMaterialCompiler : ResourceCompiler
{
	protected override Task<bool> Compile()
	{
		var filename = Context.AbsolutePath;
		var jsonString = File.ReadAllText( filename );

		var docOptions = new JsonDocumentOptions();
		docOptions.MaxDepth = 512;

		using var doc = JsonDocument.Parse( jsonString, docOptions );

		var path = Path.GetDirectoryName( filename );
		var file = Path.GetFileNameWithoutExtension( filename );

		var albedo = doc.RootElement.TryGetProperty( "AlbedoImage", out var albedoValue ) ? albedoValue.GetString() : "";
		var roughness = doc.RootElement.TryGetProperty( "RoughnessImage", out var roughnessValue ) ? roughnessValue.GetString() : "";
		var normal = doc.RootElement.TryGetProperty( "NormalImage", out var normalValue ) ? normalValue.GetString() : "";
		var height = doc.RootElement.TryGetProperty( "HeightImage", out var heightValue ) ? heightValue.GetString() : "";
		var ao = doc.RootElement.TryGetProperty( "AOImage", out var aoValue ) ? aoValue.GetString() : "";

		var bcrPath = $"{path}/{file}_tmat_bcr.generated.vtex";
		var nhoPath = $"{path}/{file}_tmat_nho.generated.vtex";

		{
			var childContext = Context.CreateChild( bcrPath );
			childContext.SetInputData( string.Format( BCRTextureDefinition, albedo, roughness ) );
			childContext.Compile();
		}

		{
			var childContext = Context.CreateChild( nhoPath );
			childContext.SetInputData( string.Format( NHOTextureDefinition, normal, height, ao ) );
			childContext.Compile();
		}

		Context.Data.Write( jsonString );
		return Task.FromResult( true );

		/*
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter( stream );

		writer.WriteStartObject();
		foreach ( JsonProperty property in doc.RootElement.EnumerateObject() )
		{
			property.WriteTo( writer );
		}
		writer.WriteString( "BCRTexture", bcrPath );
		writer.WriteString( "NHOTexture", nhoPath );
		writer.WriteEndObject();
		writer.Flush();
		return Encoding.UTF8.GetString( stream.ToArray() );
		*/
	}

	//
	// Templates cause I don't want to spend hours binding dmx for sometihng we might hate
	//

	public static string BCRTextureDefinition => @"<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
""CDmeVtex""
{{
	""m_inputTextureArray"" ""element_array"" 
	[
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""color""
			""m_fileName"" ""string"" ""{0}""
			""m_colorSpace"" ""string"" ""srgb""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
			]
		}},
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""roughness""
			""m_fileName"" ""string"" ""{1}""
			""m_colorSpace"" ""string"" ""linear""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
			]
		}}
	]
	""m_outputTypeString"" ""string"" ""2D""
	""m_outputFormat"" ""string"" ""BC7""
	""m_outputClearColor"" ""vector4"" ""0 0 0 0""
	""m_nOutputMinDimension"" ""int"" ""0""
	""m_nOutputMaxDimension"" ""int"" ""0""
	""m_textureOutputChannelArray"" ""element_array"" 
	[
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" 
			[
				""color""
			]
			""m_srcChannels"" ""string"" ""rgb""
			""m_dstChannels"" ""string"" ""rgb""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""srgb""
		}},
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" 
			[
				""roughness""
			]
			""m_srcChannels"" ""string"" ""r""
			""m_dstChannels"" ""string"" ""a""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""linear""
		}}
	]
	""m_vClamp"" ""vector3"" ""0 0 0""
	""m_bNoLod"" ""bool"" ""0""
}}";

	public static string NHOTextureDefinition => @"<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
""CDmeVtex""
{{
	""m_inputTextureArray"" ""element_array"" 
	[
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""normal""
			""m_fileName"" ""string"" ""{0}""
			""m_colorSpace"" ""string"" ""linear""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
			]
		}},
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""height""
			""m_fileName"" ""string"" ""{1}""
			""m_colorSpace"" ""string"" ""linear""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
			]
		}},
		""CDmeInputTexture""
		{{
			""m_name"" ""string"" ""ao""
			""m_fileName"" ""string"" ""{2}""
			""m_colorSpace"" ""string"" ""linear""
			""m_typeString"" ""string"" ""2D""
			""m_imageProcessorArray"" ""element_array"" 
			[
			]
		}}
	]
	""m_outputTypeString"" ""string"" ""2D""
	""m_outputFormat"" ""string"" ""BC7""
	""m_outputClearColor"" ""vector4"" ""0 0 0 0""
	""m_nOutputMinDimension"" ""int"" ""0""
	""m_nOutputMaxDimension"" ""int"" ""0""
	""m_textureOutputChannelArray"" ""element_array"" 
	[
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" 
			[
				""normal""
			]
			""m_srcChannels"" ""string"" ""rg""
			""m_dstChannels"" ""string"" ""rg""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""linear""
		}},
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" 
			[
				""height""
			]
			""m_srcChannels"" ""string"" ""r""
			""m_dstChannels"" ""string"" ""b""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""linear""
		}},
		""CDmeTextureOutputChannel""
		{{
			""m_inputTextureArray"" ""string_array"" 
			[
				""ao""
			]
			""m_srcChannels"" ""string"" ""r""
			""m_dstChannels"" ""string"" ""a""
			""m_mipAlgorithm"" ""CDmeImageProcessor""
			{{
				""m_algorithm"" ""string"" ""Box""
				""m_stringArg"" ""string"" """"
				""m_vFloat4Arg"" ""vector4"" ""0 0 0 0""
			}}
			""m_outputColorSpace"" ""string"" ""linear""
		}}
	]
	""m_vClamp"" ""vector3"" ""0 0 0""
	""m_bNoLod"" ""bool"" ""0""
}}";

}
