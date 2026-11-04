using System;
using System.IO;

namespace ContentBuild;

/// <summary>
/// Small source files for fixtures: the shapes shipped content uses, cut down to what a scenario needs.
/// </summary>
static class Sources
{
	// Shipped inputs, relative to game/, each a few KB
	public const string ImageA = "core/materials/debug/particleerror.tga";
	public const string ImageB = "core/materials/debug/debugempty.tga";
	public const string ImageC = "core/dev/helper/reflectivity_90b_color.tga";
	public const string SoundA = "core/sounds/common/null.wav";
	public const string SoundB = "core/sounds/Physics/phys-impact-meat-3.wav";
	public const string MeshA = "core/models/dev/new_model/dmx/mesh/new_model_mesh.dmx";
	public const string MeshB = "core/models/editor/dmx/mesh/scripted_sequence_mesh.dmx";

	/// <summary>
	/// A shipped prop as it is: LODs, physics and break pieces from two FBX files, and a material with
	/// three large PNGs. The break pieces compile to models of their own beside it.
	/// </summary>
	public static readonly string[] Crate = ["crate01.vmdl", "crate01.fbx", "crate01gibs.fbx", "crate01a.vmat", "crate01a_ao.png", "crate01a_color.png", "crate01a_normal.png"];
	public const string CrateFolder = "addons/citizen/Assets/models/citizen_props";

	public const string BonnetFolder = "addons/citizen/Assets/models/citizen_clothes/seasonal/2025/easter_farmer/easter_bonnet/Models";

	/// <summary>
	/// A complex.shader material; pairs are parameter name then value.
	/// </summary>
	public static string Material( params string[] pairs ) => Material( "shaders/complex.shader", pairs );

	public static string Material( string shader, string[] pairs )
	{
		var body = string.Concat( pairs.Chunk( 2 ).Select( x => $"\t{x[0]} \"{x[1]}\"\n" ) );
		return $"Layer0\n{{\n\tshader \"{shader}\"\n{body}}}\n";
	}

	/// <summary>
	/// A texture with one sequence per image.
	/// </summary>
	public static string Texture( params string[] images )
	{
		var sequences = string.Join( ", ", images.Select( x => $$"""{ "Source": "{{x}}", "IsLooping": false, "FlipBook": false, "Columns": 0, "Rows": 0, "Frames": 1 }""" ) );
		return $$"""
			{
			  "Sequences": [ {{sequences}} ],
			  "InputColorSpace": "sRGB",
			  "OutputFormat": "RGBA8888",
			  "OutputColorSpace": "sRGB",
			  "OutputMipAlgorithm": "None",
			  "OutputTypeString": "2D"
			}
			""";
	}

	/// <summary>
	/// A texture padded to a power of two, which records the image's real size as its display rect.
	/// Materials using the compiled texture directly read that rect while compiling.
	/// </summary>
	public static string PaddedTexture( string image ) => $$"""
		<!-- dmx encoding keyvalues2_noids 4 format vtex 1 -->
		"CDmeVtex"
		{
			"m_inputTextureArray" "element_array"
			[
				"CDmeInputTexture"
				{
					"m_name" "string" "inputTexture0"
					"m_fileName" "string" "{{image}}"
					"m_colorSpace" "string" "srgb"
					"m_fileExt" "string" ""
					"m_nMinBitsPerChannel" "int" "-1"
					"m_typeString" "string" "2D"
					"m_imageProcessorArray" "element_array"
					[
						"CDmeImageProcessor" { "m_algorithm" "string" "FillToPowerOfTwo" "m_stringArg" "string" "" "m_vFloat4Arg" "vector4" "0 0 0 0" }
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
					"m_inputTextureArray" "string_array" [ "inputTexture0" ]
					"m_srcChannels" "string" "rgba"
					"m_dstChannels" "string" "rgba"
					"m_mipAlgorithm" "CDmeImageProcessor" { "m_algorithm" "string" "Box" "m_stringArg" "string" "" "m_vFloat4Arg" "vector4" "0 0 0 0" }
					"m_outputColorSpace" "string" "srgb"
				}
			]
			"m_vClamp" "vector3" "0 0 0"
			"m_bNoLod" "bool" "0"
		}
		""";

	/// <summary>
	/// Doesn't parse as any source format.
	/// </summary>
	public const string Broken = "{ this is not a source file";

	/// <summary>
	/// A primitive box model, like models/dev/box.vmdl.
	/// </summary>
	public static string Box( string material ) => Model( $$"""
		{
			_class = "RenderMeshList"
			children =
			[
				{
					_class = "RenderPrimitiveBox"
					name = "box"
					parent_bone = ""
					material_name = "{{material}}"
					origin = [ 0.0, 0.0, 0.0 ]
					angles = [ 0.0, 0.0, 0.0 ]
					max_u = 1.0
					max_v = 1.0
					dimensions = [ 50.0, 50.0, 50.0 ]
					segments_x = 1
					segments_y = 1
					segments_z = 1
					invert = false
				},
			]
		},
		""" );

	/// <summary>
	/// A model importing a mesh file, with every mesh material remapped, like models/dev/plane_blend.vmdl.
	/// </summary>
	public static string Mesh( string mesh, string material ) => Model( MeshNodes( mesh, material ) );

	static string MeshNodes( string mesh, string material ) => $$"""
		{
			_class = "MaterialGroupList"
			children =
			[
				{
					_class = "DefaultMaterialGroup"
					remaps = [  ]
					use_global_default = true
					global_default_material = "{{material}}"
				},
			]
		},
		{
			_class = "RenderMeshList"
			children =
			[
				{
					_class = "RenderMeshFile"
					filename = "{{mesh}}"
					import_translation = [ 0.0, 0.0, 0.0 ]
					import_rotation = [ 0.0, 0.0, 0.0 ]
					import_scale = 1.0
					align_origin_x_type = "None"
					align_origin_y_type = "None"
					align_origin_z_type = "None"
					parent_bone = ""
					import_filter = { exclude_by_default = false exception_list = [  ] }
				},
			]
		},
		""";

	/// <summary>
	/// Animation only, compiled against another model's skeleton.
	/// </summary>
	public static string Animated( string baseModel, string animation ) => Model( AnimationNodes( animation ), baseModel );

	/// <summary>
	/// A mesh model whose animation is compiled against another model's skeleton.
	/// </summary>
	public static string MeshAnimated( string mesh, string material, string baseModel, string animation ) => Model( MeshNodes( mesh, material ) + AnimationNodes( animation ), baseModel );

	static string AnimationNodes( string animation ) => $$"""
		{
			_class = "AnimationList"
			children =
			[
				{
					_class = "AnimFile"
					name = "idle"
					activity_name = ""
					activity_weight = 1
					weight_list_name = ""
					fade_in_time = 0.2
					fade_out_time = 0.2
					looping = true
					delta = false
					worldSpace = false
					hidden = false
					anim_markup_ordered = false
					disable_compression = false
					disable_interpolation = false
					enable_scale = false
					source_filename = "{{animation}}"
					start_frame = -1
					end_frame = -1
					framerate = -1.0
					take = 0
					reverse = false
				},
			]
			default_root_bone_name = ""
		},
		""";

	static string Model( string nodes, string baseModel = "" ) => $$"""
		<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->
		{
			rootNode =
			{
				_class = "RootNode"
				children =
				[
		{{nodes}}
				]
				model_archetype = ""
				primary_associated_entity = ""
				anim_graph_name = ""
				base_model_name = "{{baseModel}}"
			}
		}
		""";

	/// <summary>
	/// A tenth of a second of a 16-bit mono sine wave.
	/// </summary>
	public static void WriteWav( ContentFixture fixture, string rel, int hz )
	{
		const int rate = 22050, samples = rate / 10;
		var bytes = new byte[44 + samples * 2];
		"RIFF"u8.CopyTo( bytes );
		BitConverter.TryWriteBytes( bytes.AsSpan( 4 ), 36 + samples * 2 );
		"WAVEfmt "u8.CopyTo( bytes.AsSpan( 8 ) );
		BitConverter.TryWriteBytes( bytes.AsSpan( 16 ), 16 );
		BitConverter.TryWriteBytes( bytes.AsSpan( 20 ), (short)1 );
		BitConverter.TryWriteBytes( bytes.AsSpan( 22 ), (short)1 );
		BitConverter.TryWriteBytes( bytes.AsSpan( 24 ), rate );
		BitConverter.TryWriteBytes( bytes.AsSpan( 28 ), rate * 2 );
		BitConverter.TryWriteBytes( bytes.AsSpan( 32 ), (short)2 );
		BitConverter.TryWriteBytes( bytes.AsSpan( 34 ), (short)16 );
		"data"u8.CopyTo( bytes.AsSpan( 36 ) );
		BitConverter.TryWriteBytes( bytes.AsSpan( 40 ), samples * 2 );
		for ( var i = 0; i < samples; i++ )
			BitConverter.TryWriteBytes( bytes.AsSpan( 44 + i * 2 ), (short)(Math.Sin( 2 * Math.PI * hz * i / rate ) * 8000) );

		Directory.CreateDirectory( Path.GetDirectoryName( fixture.Path( rel ) ) );
		File.WriteAllBytes( fixture.Path( rel ), bytes );
	}

	/// <summary>
	/// An uncompressed 32-bit TGA filled with one colour. Odd sizes get padded to a power of two by the
	/// texture compiler, which records the original size as the display rect.
	/// </summary>
	public static void WriteTga( ContentFixture fixture, string rel, int width, int height, uint bgra = 0xFF4080C0 )
	{
		var bytes = new byte[18 + width * height * 4];
		bytes[2] = 2;
		BitConverter.TryWriteBytes( bytes.AsSpan( 12 ), (ushort)width );
		BitConverter.TryWriteBytes( bytes.AsSpan( 14 ), (ushort)height );
		bytes[16] = 32;
		bytes[17] = 0x28;
		for ( var i = 18; i < bytes.Length; i += 4 ) BitConverter.TryWriteBytes( bytes.AsSpan( i ), bgra );

		Directory.CreateDirectory( Path.GetDirectoryName( fixture.Path( rel ) ) );
		File.WriteAllBytes( fixture.Path( rel ), bytes );
	}
}
