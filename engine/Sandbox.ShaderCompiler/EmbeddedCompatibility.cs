using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// What a package shader's embedded source needs to compile against today's engine includes as it compiled against the
/// older engine's that built it (<see cref="PassConverter.FromEmbedded"/>). Packages can't be edited, so each program's
/// text is adjusted here, and <c>sbox/embedded.slangh</c> and <c>sbox/embedded_end.slangh</c> declare what the engine has
/// since dropped. See docs/managed/pass-modules.md, "Shaders an older engine compiled".
/// </summary>
public static partial class EmbeddedCompatibility
{
	/// <summary>
	/// The engine's cbuffers a package's text may declare a copy of (its own lighting or view code, written to bind them by
	/// name as an older engine did), with the include that declares them today. The view's five environment blocks are one
	/// cbuffer now, ViewEnvironment, with each member's name kept.
	/// </summary>
	static readonly Dictionary<string, string> EngineCbuffers = new( StringComparer.Ordinal )
	{
		["PerViewConstantBuffer_t"] = "common.fxc",
		["PerViewConstantBufferVR_t"] = "common/ViewEnvironment.hlsl",
		["ViewLightingConfigV2"] = "common/ViewEnvironment.hlsl",
		["DirectionalLightCB"] = "common/ViewEnvironment.hlsl",
		["ClusteredLightingConstants"] = "common/ViewEnvironment.hlsl",
		["FogConstants"] = "common/ViewEnvironment.hlsl",
	};

	/// <summary>
	/// The samplers sbox/samplers.slangh declares (in place of common_samplers.fxc), which every module has (pass.slangh
	/// includes system.fxc, which includes it). Packages from before it was included everywhere declare some of them themselves.
	/// </summary>
	public static readonly string[] EngineSamplers =
	[
		"g_sDefault", "g_sAniso", "g_sBilinearClamp", "g_sBilinearWrap", "g_sBilinearMirror", "g_sTrilinearWrap", "g_sTrilinearClamp",
		"g_sTrilinearMirror", "g_sTrilinearBorder", "g_sPointBorder", "g_sPointClamp", "g_sPointWrap", "g_sPointMirror",
	];

	/// <summary>
	/// Includes a package's text keeps that no package ships (a library's file its author had, or the package's own file left
	/// out of it), by path, with the stand-in in <c>sbox/standins</c> for each: what the package reads from it, doing
	/// nothing. Without one the shader compiles nowhere.
	/// </summary>
	static readonly Dictionary<string, string> StandIns = new( StringComparer.OrdinalIgnoreCase )
	{
		["environmentsystems/shaders/tonemap_exempt.hlsl"] = "tonemap_exempt.slangh",
		["materials/shaders/void_infection.hlsl"] = "void_infection.slangh",
	};

	/// <summary>One program's text adjusted to compile against today's includes.</summary>
	public static string Apply( string body )
	{
		body = EngineCbuffersOnce( body );

		// An include no package ships is its stand-in
		body = IncludeLine().Replace( body, m => StandIns.TryGetValue( m.Groups["path"].Value.Replace( '\\', '/' ), out var standIn ) ? $"{m.Groups["lead"].Value}#include \"sbox/standins/{standIn}\"" : m.Value );

		// common.fxc's descriptor sets, which a package's own copy of an older system header declares too (vfx's parser takes
		// each once): the engine's include declares them, where the package did
		body = DescriptorSetDeclaration().Replace( body, m => $"#include \"common.fxc\" // {m.Groups["name"].Value}, declared by the engine" );

		// A sampler the engine declares for every module is declared once, by the engine
		body = SamplerDeclaration().Replace( body, m => Array.IndexOf( EngineSamplers, m.Groups["name"].Value ) >= 0 ? $"// {m.Groups["name"].Value}: declared by the engine" : m.Value );

		// HLSL's class is a struct (vfx compiled it with DXC); Slang's is a reference type, which nothing GPU takes. Today's
		// engine structs were classes once, and packages derive from them (class ShadingModelX : ShadingModelStandard).
		body = ClassKeyword().Replace( body, "struct" );

		// Today's pixel input has the face's side as face in every pixel shader, as any material can be two-sided: a member of
		// the shader's own for it beside the include is that one (two would be two members of one name, or two front-face
		// inputs), and older engines' bIsFrontFace is too. A shader that reads its own turns its back faces' normals itself,
		// so the engine's (Material::Init, ShadingModelStandard::Shade) leave them alone
		var turnsFaces = false;
		body = PixelInputStruct().Replace( body, s => OwnFrontFace().Replace( s.Value, d =>
		{
			turnsFaces = true;
			return d.Groups["name"].Value == "face" ? "" : $"#define {d.Groups["name"].Value} face";
		} ) );

		if ( FrontFaceUse().IsMatch( body ) && !FrontFaceDeclaration().IsMatch( body ) )
		{
			body = PixelInputInclude().Replace( body, m => m.Value + FrontFaceMember, 1 );
			turnsFaces = true;
		}

		if ( turnsFaces ) body = "#define SBOX_TURNS_BACK_FACES\n" + body;

		var end = new StringBuilder();
		end.Append( '\n' );
		if ( PcfDiscUse().IsMatch( body ) && !PcfDiscDeclaration().IsMatch( body ) ) end.Append( "#define SBOX_EMBEDDED_PCF_DISCS\n" );
		if ( TextureFilteringUse().IsMatch( body ) && !TextureFilteringDeclaration().IsMatch( body ) && !body.Contains( "Material.CommonInputs.hlsl", StringComparison.OrdinalIgnoreCase ) )
			end.Append( "#define SBOX_EMBEDDED_TEXTURE_FILTERING\n" );
		if ( ExtraDataFromInput().Match( body ) is { Success: true } extra && Regex.IsMatch( body, $@"\b(?:VS_INPUT|VertexInput)\s+{extra.Groups["arg"].Value}\b" ) )
			end.Append( "#define SBOX_EMBEDDED_EXTRA_DATA_FROM_INPUT\n" );
		if ( !body.Contains( "g_nMorphTextureAtlasWidth", StringComparison.Ordinal ) ) end.Append( "#define SBOX_EMBEDDED_MORPH_ATLAS_WIDTH\n" );
		if ( InvProjectPositionUse().IsMatch( body ) && !InvProjectPositionDeclaration().IsMatch( body ) ) end.Append( "#define SBOX_EMBEDDED_INV_PROJECT_POSITION\n" );
		end.Append( "#include \"sbox/embedded_end.slangh\"\n" );
		return body + end;
	}

	/// <summary>
	/// One program's text adjusted as <see cref="Apply"/> does, with <c>sbox/embedded.slangh</c>'s macros first, for vfx to
	/// compile on its own (a package shader recompiled for the engine's material system as it installs). vfx's compile
	/// doesn't find <c>sbox/</c>'s includes, so their text, from the engine's <paramref name="shaders"/> folder, goes in
	/// whole. Applying it again to its own result changes nothing that compiles differently.
	/// </summary>
	public static string ForRecompile( string body, string shaders )
	{
		var text = "#include \"sbox/embedded.slangh\"\n" + Apply( body );
		return ShimInclude().Replace( text, m => System.IO.File.ReadAllText( System.IO.Path.Combine( shaders, "sbox", m.Groups["file"].Value ) ).Replace( "\r\n", "\n" ) + "\n" );
	}

	[GeneratedRegex( @"^[ \t]*#include ""sbox/(?<file>embedded(?:_end)?\.slangh|standins/\w+\.slangh)""[ \t]*\r?\n", RegexOptions.Multiline )]
	private static partial Regex ShimInclude();

	[GeneratedRegex( @"(?<=^|\n)(?<lead>[ \t]*)#[ \t]*include[ \t]+""(?<path>[^""\n]+)""" )]
	private static partial Regex IncludeLine();

	/// <summary>
	/// The package's copies of the engine's cbuffers replaced by the engine's include, so each member is declared once and
	/// binds where the engine fills it. The preprocessor lines inside a copy (its <c>#define</c> shorthands) stay.
	/// </summary>
	static string EngineCbuffersOnce( string body )
	{
		var text = new StringBuilder( body.Length );
		var at = 0;
		foreach ( Match m in CbufferStart().Matches( body ) )
		{
			if ( m.Index < at || !EngineCbuffers.TryGetValue( m.Groups["name"].Value, out var include ) ) continue;
			var open = m.Index + m.Length - 1;
			var close = Close( body, open );
			if ( close < 0 ) continue;

			var after = close + 1;
			while ( after < body.Length && body[after] is ' ' or '\t' ) after++;
			if ( after < body.Length && body[after] == ';' ) after++;

			text.Append( body, at, m.Index - at );
			text.Append( "#include \"" ).Append( include ).Append( "\" // " ).Append( m.Groups["name"].Value ).Append( ", declared by the engine\n" );
			foreach ( var line in body[(open + 1)..close].Split( '\n' ) )
			{
				if ( line.TrimStart().StartsWith( '#' ) ) text.Append( line.TrimEnd( '\r' ) ).Append( '\n' );
			}

			at = after;
		}

		text.Append( body, at, body.Length - at );
		return text.ToString();
	}

	/// <summary>The brace closing the one at <paramref name="open"/>, past comments; -1 when there's none.</summary>
	static int Close( string text, int open )
	{
		var depth = 0;
		for ( int i = open; i < text.Length; i++ )
		{
			if ( text[i] == '/' && i + 1 < text.Length && text[i + 1] == '/' )
			{
				i = text.IndexOf( '\n', i );
				if ( i < 0 ) return -1;
				continue;
			}

			if ( text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*' )
			{
				i = text.IndexOf( "*/", i + 2, StringComparison.Ordinal );
				if ( i < 0 ) return -1;
				i++;
				continue;
			}

			if ( text[i] == '{' ) depth++;
			else if ( text[i] == '}' && --depth == 0 ) return i;
		}

		return -1;
	}

	[GeneratedRegex( @"(?<=^|\n)[ \t]*cbuffer[ \t]+(?<name>\w+)[^{;\n]*\s*\{" )]
	private static partial Regex CbufferStart();

	// A file-scope declaration of one of common.fxc's descriptor sets on its own line
	[GeneratedRegex( @"(?<=^|\n)[ \t]*ExternalDescriptorSet[ \t]+(?<name>g_globalLateBoundBindlessSet|g_viewSet)[ \t]+Slot[ \t]+\d+[ \t]*;" )]
	private static partial Regex DescriptorSetDeclaration();

	// A file-scope sampler declaration on its own line, with its annotations (which hold semicolons)
	[GeneratedRegex( @"(?<=^|\n)[ \t]*SamplerState[ \t]+(?<name>\w+)\s*(?:<[^>]*>)?\s*;" )]
	private static partial Regex SamplerDeclaration();

	[GeneratedRegex( @"(?<!\benum\s+)\bclass(?=\s+\w+\s*[:{\n])" )]
	private static partial Regex ClassKeyword();

	[GeneratedRegex( @"\bPCFDiscSamples(?:5|12|29)\b" )]
	private static partial Regex PcfDiscUse();

	[GeneratedRegex( @"\bfloat2\s+PCFDiscSamples(?:5|12|29)\s*\[" )]
	private static partial Regex PcfDiscDeclaration();

	const string FrontFaceMember = "#if ( PROGRAM == VFX_PROGRAM_PS )\n#define bIsFrontFace face\n#endif\n";

	// A pixel input struct built on the engine's (no braces inside it), and a front-face member a shader declared in it
	[GeneratedRegex( @"\bstruct\s+PixelInput\s*\{[^{}]*?#include\s+""common/pixelinput\.hlsl""[^{}]*\}" )]
	private static partial Regex PixelInputStruct();

	[GeneratedRegex( @"\bbool\s+(?<name>\w+)\s*:\s*SV_IsFrontFace\s*;" )]
	private static partial Regex OwnFrontFace();

	[GeneratedRegex( @"\.\s*bIsFrontFace\b" )]
	private static partial Regex FrontFaceUse();

	[GeneratedRegex( @"(?:\bbool\s+|#define\s+)bIsFrontFace\b" )]
	private static partial Regex FrontFaceDeclaration();

	[GeneratedRegex( @"\bstruct\s+PixelInput\s*\{[^}]*?#include\s+""common/pixelinput\.hlsl""[^\n]*\n" )]
	private static partial Regex PixelInputInclude();

	// GetExtraPerInstanceShaderData called with a variable (an older engine took the vertex input) rather than a member
	[GeneratedRegex( @"\bGetExtraPerInstanceShaderData\(\s*(?<arg>\w+)\s*\)" )]
	private static partial Regex ExtraDataFromInput();

	[GeneratedRegex( @"\bInvProjectPosition\s*\(" )]
	private static partial Regex InvProjectPositionUse();

	[GeneratedRegex( @"\bfloat3\s+InvProjectPosition\s*\(" )]
	private static partial Regex InvProjectPositionDeclaration();

	[GeneratedRegex( @"\bTextureFiltering\b" )]
	private static partial Regex TextureFilteringUse();

	[GeneratedRegex( @"\bSamplerState\s+TextureFiltering\b" )]
	private static partial Regex TextureFilteringDeclaration();
}
