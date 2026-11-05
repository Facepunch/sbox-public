using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ContentBuild;

/// <summary>
/// A tool project's compilers, compiled the way the editor compiles projects (a compile group), into a folder content
/// workers load them from. Its sources can change between compiles, as a project's do.
/// </summary>
public sealed class CompilerFixture : IDisposable
{
	/// <summary>
	/// The assemblies the workers load.
	/// </summary>
	public string Folder { get; } = Directory.CreateTempSubdirectory( "sbox-fixture-compilers" ).FullName;

	/// <summary>
	/// The compilers' project: a game resource type of its own, its compiler, and a texture generator.
	/// </summary>
	public string Compilers { get; set; } = CompilersSource;

	/// <summary>
	/// A library the compilers call into.
	/// </summary>
	public string Helper { get; set; } = HelperSource;

	/// <summary>
	/// Code the compiler generates into the compilers' assembly, as a project's settings would (global usings).
	/// </summary>
	public string GeneratedCode { get; set; } = "";

	/// <summary>
	/// Without the compilers' project: only the helper library is compiled.
	/// </summary>
	public bool Removed { get; set; }

	readonly string sources = Directory.CreateTempSubdirectory( "sbox-fixture-sources" ).FullName;

	public void Dispose()
	{
		foreach ( var dir in new[] { Folder, sources } )
		{
			try { Directory.Delete( dir, true ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { }
		}
	}

	/// <summary>
	/// Compiles the current sources into <see cref="Folder"/>, replacing what was there.
	/// </summary>
	public void Compile()
	{
		foreach ( var file in Directory.GetFiles( Folder ) ) File.Delete( file );

		using var group = new CompileGroup( "Fixture" );
		var settings = new Compiler.Configuration { Whitelist = false };

		var helper = group.CreateCompiler( "fixturehelper", Write( "helper", Helper ), settings );
		Compiler compilers = null;
		if ( !Removed )
		{
			compilers = group.CreateCompiler( "fixture", Write( "compilers", Compilers ), settings );
			compilers.AddReference( helper.AssemblyName );
			compilers.GeneratedCode.Append( GeneratedCode );
		}

		Task.Run( group.BuildAsync ).GetAwaiter().GetResult();

		foreach ( var compiler in new[] { helper, compilers } )
		{
			if ( compiler is null ) continue;

			var output = compiler.Output;
			Assert.IsTrue( output?.Successful == true, $"{compiler.AssemblyName}: {output?.Exception}\n{string.Join( "\n", output?.Diagnostics ?? [] )}" );
			File.WriteAllBytes( Path.Combine( Folder, $"{compiler.AssemblyName}.dll" ), output.AssemblyData );
		}
	}

	string Write( string project, string source )
	{
		var dir = Path.Combine( sources, project );
		if ( Directory.Exists( dir ) ) Directory.Delete( dir, true );
		Directory.CreateDirectory( dir );
		File.WriteAllText( Path.Combine( dir, $"{project}.cs" ), source );
		return dir;
	}

	public const string HelperSource = """
		namespace Fixture;

		public static class FixtureHelper
		{
			public static string Describe() => "helper 1";
		}
		""";

	/// <summary>
	/// Like an animation graph's compiler: a resource with its own extension that reads another asset's compiled output,
	/// generates a native texture, and compiles variants of itself from data it gives them. "Fail" in its source makes it
	/// throw; "FailVariant" makes that variant throw; "Entered" (a path) makes it write that file and never finish.
	/// </summary>
	public const string CompilersSource = """"
		using Sandbox;
		using Sandbox.Resources;
		using System.Text.Json.Nodes;
		using System.Threading;
		using System.Threading.Tasks;

		namespace Fixture;

		[AssetType( Name = "Fixture Graph", Extension = "fxg" )]
		public class FixtureGraph : GameResource
		{
		}

		[ResourceIdentity( "fxg" )]
		public class FixtureGraphCompiler : ResourceCompiler
		{
			public const string Marker = "compiler 1";

			protected override async Task<bool> Compile()
			{
				var json = Context.ReadSourceAsJson();
				var texture = (string)json["Texture"];
				if ( texture is not null )
					Context.AddCompiledReference( texture );

				if ( Context.Describing )
					return true;

				await Task.Yield();

				if ( (string)json["Fail"] is { } failure )
					throw new System.Exception( failure );

				if ( texture is not null )
				{
					var compiled = Texture.Load( texture );
					json["Size"] = $"{compiled.Width}x{compiled.Height}";
				}

				json["Marker"] = Marker;
				json["Helper"] = FixtureHelper.Describe();

				var dir = System.IO.Path.GetDirectoryName( Context.AbsolutePath ).Replace( '\\', '/' );
				var name = System.IO.Path.GetFileNameWithoutExtension( Context.AbsolutePath );

				if ( (string)json["Image"] is { } image )
				{
					var child = Context.CreateChild( $"{dir}/{name}_fx.generated.vtex" );
					child.SetInputData( TextureDefinition.Replace( "IMAGE", image ) );
					if ( !child.Compile() )
						throw new System.Exception( "couldn't generate the texture" );
				}

				foreach ( var variant in json["Variants"]?.AsArray() ?? new JsonArray() )
				{
					var data = new JsonObject { ["Variant"] = (string)variant };
					if ( (string)json["FailVariant"] == (string)variant ) data["Fail"] = $"variant {variant}";

					var child = Context.CreateChild( $"{dir}/{name}_{variant}.fxg" );
					child.SetInputData( data.ToJsonString() );
					if ( !child.Compile() )
						throw new System.Exception( $"variant {variant} didn't compile" );
				}

				// Says it's compiling (with its children made), then never finishes: only ending its worker stops it
				if ( (string)json["Entered"] is { } entered )
				{
					System.IO.File.WriteAllText( entered, "" );
					await new TaskCompletionSource().Task;
				}

				Context.Data.Write( Context.ScanJson( json.ToJsonString() ) );
				return true;
			}

			const string TextureDefinition = """
				<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
				"CDmeVtex"
				{
					"m_inputTextureArray" "element_array"
					[
						"CDmeInputTexture"
						{
							"m_name" "string" "color"
							"m_fileName" "string" "IMAGE"
							"m_colorSpace" "string" "srgb"
							"m_typeString" "string" "2D"
							"m_imageProcessorArray" "element_array"
							[
							]
						}
					]
					"m_outputTypeString" "string" "2D"
					"m_outputFormat" "string" "RGBA8888"
					"m_outputClearColor" "vector4" "0 0 0 0"
					"m_nOutputMinDimension" "int" "0"
					"m_nOutputMaxDimension" "int" "0"
					"m_textureOutputChannelArray" "element_array"
					[
						"CDmeTextureOutputChannel"
						{
							"m_inputTextureArray" "string_array"
							[
								"color"
							]
							"m_srcChannels" "string" "rgba"
							"m_dstChannels" "string" "rgba"
							"m_mipAlgorithm" "CDmeImageProcessor"
							{
								"m_algorithm" "string" "Box"
								"m_stringArg" "string" ""
								"m_vFloat4Arg" "vector4" "0 0 0 0"
							}
							"m_outputColorSpace" "string" "srgb"
						}
					]
					"m_vClamp" "vector3" "0 0 0"
					"m_bNoLod" "bool" "0"
				}
				""";
		}

		/// <summary>
		/// A texture generator: a flat colour, written to disk by the texture compiler.
		/// </summary>
		[ClassName( "fixturecolor" )]
		public class FixtureColor : TextureGenerator
		{
			public override bool CacheToDisk => true;

			protected override ValueTask<Bitmap> CreateBitmap( Options options, CancellationToken ct )
			{
				var bitmap = new Bitmap( 4, 4 );
				bitmap.Clear( new Color( 1, 0, 0 ) );
				return ValueTask.FromResult( bitmap );
			}
		}
		"""";
}
