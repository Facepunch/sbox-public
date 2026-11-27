using System.Globalization;
using System.Reflection;
using System.Text;

namespace Editor.ShaderGraph;

/// <summary>
/// A graph of the surface domain compiles to a Slang surface (docs/reference/borealis-slang-materials.md): one
/// <c>[Material]</c> struct whose <c>Evaluate</c> is the graph's pixel stage, its parameters and textures the struct's fields,
/// lit and drawn in every pass by the engine. A position offset moves vertices in world space (an
/// <c>IInterpolatedVertexSurface</c>'s <c>Displace</c>), and the per-vertex values the pixel stage reads that the engine
/// doesn't hand a surface (the mesh's vertex colour, object-space position and tangent frame) are its <c>Varyings</c>.
/// Nodes write their code through the inputs here (<see cref="WorldPosition"/>, <see cref="Uv"/>, ...), which name what
/// each output reads in the stage being compiled.
/// </summary>
public sealed partial class GraphCompiler
{
	/// <summary>
	/// What the graph compiles to: a Slang surface (a material the engine draws in every pass), or for post-processing a
	/// standalone module.
	/// </summary>
	public bool IsSurface => Graph.Domain != ShaderDomain.PostProcess;

	/// <summary>The surface's name: its struct's (<see cref="SurfaceName"/> of the graph's file name).</summary>
	public string Name { get; set; } = "ShaderGraph";

	/// <summary>
	/// A graph's surface name from its file name: an identifier, letters, digits and underscores, whatever the file is called
	/// ("My Graph-2" is <c>My_Graph_2</c>). A name that already is one stays as it is, so saved graphs keep theirs; one that
	/// would be a name the code has (<c>Material</c>, <c>float</c>) gets <c>_Graph</c> after it.
	/// </summary>
	public static string SurfaceName( string name )
	{
		var clean = new string( (name ?? "").Select( c => char.IsAsciiLetterOrDigit( c ) || c == '_' ? c : '_' ).ToArray() );
		clean = clean.Length == 0 || char.IsAsciiDigit( clean[0] ) ? "_" + clean : clean;
		return ReservedNames.Contains( clean ) || clean.StartsWith( "__", StringComparison.Ordinal ) ? clean + "_Graph" : clean;
	}

	/// <summary>A parameter of a surface: a field of its material struct.</summary>
	internal sealed record SurfaceParameter( string Field, string Source, string Attribute, object Value, object Min, object Max, bool IsRange, bool IsAttribute, ParameterUI UI, int Components );

	[Flags]
	private enum VaryingUse
	{
		None = 0,
		Color = 1,
		PositionOs = 2,
		NormalOs = 4,
		TangentOs = 8,
	}

	private VaryingUse Varyings;
	private bool UsesScreenDerivatives;
	private bool UsesDepth;
	private bool UsesFrameBufferCopy;

	/// <summary>Names a field can't have: the struct's methods, the code's own names, and types it reads.</summary>
	private static readonly HashSet<string> ReservedNames = new( StringComparer.Ordinal )
	{
		"Evaluate", "Vertex", "Displace", "Varyings", "Material", "SurfaceInput", "WorldVertexInput", "PreviewStage",
		"i", "o", "v", "m", "s", "in", "out", "inout", "float", "int", "uint", "bool", "half", "true", "false", "return", "if", "else",
		"for", "while", "struct", "static", "const", "public", "this", "Texture2D", "SamplerState",
	};

	/// <summary>
	/// The field a parameter or texture named <paramref name="name"/> (with its <paramref name="prefix"/>: <c>g_fl</c>, <c>g_v</c>,
	/// <c>g_b</c>, <c>g_t</c>) is in a surface: the name without its prefix, which the material keeps the same name for
	/// (<c>g_flRoughness</c> is <c>Roughness</c>). A name that can't stand alone keeps its prefix, the material's name given by
	/// <c>[Source]</c>.
	/// </summary>
	private static (string Name, string Source) FieldName( string name, string prefix )
	{
		var stem = name.StartsWith( prefix, StringComparison.Ordinal ) ? name[prefix.Length..] : name;
		if ( stem.Length > 0 && (char.IsLetter( stem[0] ) || stem[0] == '_') && !ReservedNames.Contains( stem ) && !stem.StartsWith( "g_", StringComparison.Ordinal ) )
			return (stem, null);
		return (name, name);
	}

	private string Vary( VaryingUse use, string field )
	{
		Varyings |= use;
		return $"o.{field}";
	}

	/// <summary>The mesh's first and second uv as a float4 (vfx's vTextureCoords).</summary>
	public string TextureCoords => IsSurface ? (IsVs ? "float4( v.Uv, 0, 0 )" : "float4( i.Uv, i.Uv2 )") : "i.vTextureCoords";

	/// <summary>The mesh's first uv.</summary>
	public string Uv => IsSurface ? (IsVs ? "v.Uv" : "i.Uv") : "i.vTextureCoords.xy";

	/// <summary>The mesh's second uv (zero in a surface's vertex stage).</summary>
	public string Uv2 => IsSurface ? (IsVs ? "float2( 0, 0 )" : "i.Uv2") : "i.vTextureCoords.zw";

	/// <summary>The vertex normal in world space.</summary>
	public string WorldNormal => IsSurface ? (IsVs ? "v.Normal" : "i.Normal") : "i.vNormalWs";

	/// <summary>The tangent frame in world space.</summary>
	public string WorldTangentU => IsSurface ? (IsVs ? "v.TangentU" : "i.TangentU") : "i.vTangentUWs";
	public string WorldTangentV => IsSurface ? (IsVs ? "v.TangentV" : "i.TangentV") : "i.vTangentVWs";

	/// <summary>The position in world space.</summary>
	public string WorldPosition => IsSurface ? (IsVs ? "v.Position" : "i.Position") : IsVs ? "i.vPositionWs" : "i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz";

	/// <summary>The mesh's position in object space.</summary>
	public string ObjectPosition => IsSurface ? (IsVs ? "v.ObjectPosition" : Vary( VaryingUse.PositionOs, "PositionOs" )) : "i.vPositionOs";

	/// <summary>The mesh's normal in object space.</summary>
	public string ObjectNormal => IsSurface ? (IsVs ? "v.ObjectNormal" : Vary( VaryingUse.NormalOs, "NormalOs" )) : "i.vNormalOs";

	/// <summary>The mesh's tangent U in object space, and V's sign in w.</summary>
	public string ObjectTangent => IsSurface ? (IsVs ? "v.ObjectTangent" : Vary( VaryingUse.TangentOs, "TangentOs" )) : "i.vTangentUOs_flTangentVSign";

	/// <summary>The mesh's vertex colour.</summary>
	public string VertexColor => IsSurface ? Vary( VaryingUse.Color, "Color" ) : "i.vColor";

	/// <summary>The object's tint (white in a surface's vertex stage).</summary>
	public string Tint => IsSurface ? (IsVs ? "float4( 1, 1, 1, 1 )" : "i.Color") : "i.vTintColor";

	/// <summary>Whether the pixel is on a front face (false in a vertex stage).</summary>
	public string FrontFace => IsVs ? "false" : IsSurface ? "i.FrontFace" : "i.vFrontFacing";

	/// <summary>The pixel's position (SV_Position); a module's vertex stage has its clip-space position; null in a surface's vertex stage.</summary>
	public string ScreenPosition => IsSurface ? (IsVs ? null : "i.ScreenPosition") : IsVs ? "i.vPositionPs" : "i.vPositionSs";

	/// <summary>The camera's position in world space.</summary>
	public string CameraPosition => IsSurface ? (IsVs ? "v.Camera" : "i.Camera") : "g_vCameraPositionWs";

	/// <summary>The camera's forward in world space; null in a surface's vertex stage.</summary>
	public string CameraDirection => IsSurface ? (IsVs ? null : "i.CameraDirection") : "g_vCameraDirWs";

	/// <summary>The view's near and far planes; null in a surface's vertex stage.</summary>
	public string NearPlane => IsSurface ? (IsVs ? null : "i.NearPlane") : "g_flNearPlane";
	public string FarPlane => IsSurface ? (IsVs ? null : "i.FarPlane") : "g_flFarPlane";

	/// <summary>The viewport: its size, its inverse, where it starts and its depth range; null in a surface's vertex stage.</summary>
	public string ViewportSize => IsSurface ? (IsVs ? null : "i.ViewportSize") : "g_vViewportSize";
	public string InverseViewportSize => IsSurface ? (IsVs ? null : "( 1.0 / i.ViewportSize )") : "g_vInvViewportSize";
	public string ViewportOffset => IsSurface ? (IsVs ? null : "i.ViewportOffset") : "g_vViewportOffset";
	public string ViewportMinZ => IsSurface ? (IsVs ? null : "i.DepthRange.x") : "g_flViewportMinZ";
	public string ViewportMaxZ => IsSurface ? (IsVs ? null : "i.DepthRange.y") : "g_flViewportMaxZ";

	/// <summary>The view's time in seconds (the preview's own clock in the preview).</summary>
	public string Time => IsPreview ? "g_flPreviewTime" : IsSurface ? (IsVs ? "v.Time" : "i.Time") : "g_flTime";

	/// <summary>The sun: the way its light travels, and its colour; null in a surface's vertex stage.</summary>
	public string SunDirection => IsSurface ? (IsVs ? null : "i.SunDirection") : "g_DirectionalLightDirection.xyz";
	public string SunColor => IsSurface ? (IsVs ? null : "i.SunColor") : "g_DirectionalLightColor.rgb";

	/// <summary>A pixel position as a 0..1 position in the viewport; null in a surface's vertex stage.</summary>
	public string ViewportUv( string position ) => IsSurface ? (IsVs ? null : $"( ( {position} ) - i.ViewportOffset ) / i.ViewportSize") : $"CalculateViewportUv( {position} )";

	/// <summary>The direction from a world position to the camera.</summary>
	public string PositionToCamera( string position ) => IsSurface ? $"normalize( {CameraPosition} - ( {position} ) )" : $"CalculatePositionToCameraDirWs( {position} )";

	/// <summary>The direction from the camera to a world position.</summary>
	public string CameraToPosition( string position ) => IsSurface ? $"normalize( ( {position} ) - {CameraPosition} )" : $"CalculateCameraToPositionDirWs( {position} )";

	/// <summary>The depth buffer's value at a pixel position; null in a surface's vertex stage.</summary>
	public string Depth( string position )
	{
		if ( !IsSurface ) return $"Depth::Get( {position} )";
		if ( IsVs ) return null;
		UsesDepth = true;
		return $"GraphDepth( {position} )";
	}

	/// <summary>The distance from the camera to the scene at a pixel position, in world units; null in a surface's vertex stage.</summary>
	public string LinearDepth( string position )
	{
		if ( !IsSurface ) return $"Depth::GetLinear( {position} )";
		if ( IsVs ) return null;
		UsesDepth = true;
		return $"GraphLinearDepth( i, {position} )";
	}

	/// <summary>
	/// A surface's frame-buffer copy (what's drawn behind a translucent surface, which the engine copies for it) and the scale
	/// from a viewport uv to its uv.
	/// </summary>
	public (string Texture, string UvScale) FrameBufferCopy()
	{
		UsesFrameBufferCopy = true;
		return ("g_tFrameBufferCopyTexture", IsVs ? "float2( 1, 1 )" : "i.FrameBufferUvScale");
	}

	/// <summary>A texture read at a uv: at its top mip in a vertex stage.</summary>
	public string Sample( string texture, string sampler, string uv )
	{
		if ( IsVs ) return $"{texture}.SampleLevel( {sampler}, {uv}, 0 )";
		return IsSurface ? $"{texture}.Sample( {sampler}, {uv} )" : $"Tex2DS( {texture}, {sampler}, {uv} )";
	}

	/// <summary>A cube texture read in a direction.</summary>
	public string SampleCube( string texture, string sampler, string direction )
	{
		return IsSurface ? $"{texture}.Sample( {sampler}, {direction} )" : $"TexCubeS( {texture}, {sampler}, {direction} )";
	}

	/// <summary>
	/// A screen-space derivative of a value (<paramref name="op"/>: ddx, ddy, fwidth and their coarse and fine forms), which
	/// only a pixel stage has: an error on the node <paramref name="node"/> in a vertex stage (Position Offset). A surface
	/// that takes one is marked <c>ScreenDerivatives</c>, for a pass that shades without raster derivatives (a
	/// visibility-buffer resolve) to draw it the raster way: in every raster pass a surface's Evaluate runs in uniform
	/// control flow per quad, where they're right.
	/// </summary>
	public NodeResult ScreenDerivative( string op, NodeResult value, string node )
	{
		if ( IsVs )
			return NodeResult.Error( $"{node} reads screen-space derivatives, which only the pixel stage has: it can't feed Position Offset" );
		UsesScreenDerivatives |= IsSurface;
		return new NodeResult( value.Components, $"{op}( {value} )" );
	}

	/// <summary>
	/// A node output from an input expression, or an error naming what isn't there when it's null (an input a surface's vertex
	/// stage doesn't have).
	/// </summary>
	public NodeResult Input( int components, string code, string what, bool constant = false )
	{
		if ( code is null )
			return NodeResult.Error( $"{what} isn't available in a surface's vertex stage (Position Offset)" );
		return new NodeResult( components, code, constant );
	}

	private static string Number( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );

	private static string Floats( params float[] values ) => string.Join( ", ", values.Select( Number ) );

	private static string Floats( Color c ) => Floats( c.r, c.g, c.b, c.a );

	/// <summary>A value's components, as a default or range writes them.</summary>
	private static float[] Components( object value ) => value switch
	{
		float f => [f],
		Vector2 v => [v.x, v.y],
		Vector3 v => [v.x, v.y, v.z],
		Vector4 v => [v.x, v.y, v.z, v.w],
		Color c => [c.r, c.g, c.b, c.a],
		bool b => [b ? 1 : 0],
		_ => [0],
	};

	private static string DefaultAttribute( object value )
	{
		var components = Components( value );
		return components.Length == 1 ? $"Default( {Floats( components )} )" : $"Default{components.Length}( {Floats( components )} )";
	}

	private static string Quote( string text ) => (text ?? "").Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );

	/// <summary>The graph as a surface.</summary>
	private string GenerateSurface()
	{
		var resultNode = Graph.Nodes.OfType<BaseResult>().FirstOrDefault();
		if ( resultNode == null )
			return null;

		var lit = Graph.ShadingModel == ShadingModel.Lit;
		var material = lit ? GenerateMaterial() : GenerateUnlitMaterial( resultNode );

		// If we have any errors after evaluating, no point going further
		if ( Errors.Any() )
			return null;

		// The pixel stage's locals, evaluated before the vertex stage's
		Stage = ShaderStage.Pixel;
		var pixelLocals = GenerateLocals();

		var displace = GenerateDisplace( resultNode );
		if ( Errors.Any() )
			return null;

		var displaces = displace is not null;
		var interpolated = displaces || Varyings != VaryingUse.None;
		var type = Name;
		var varyings = $"{type}Varyings";

		var sb = new StringBuilder();
		sb.Append( $"// {type}: a surface ShaderGraph generated from its graph. Edit the graph, not this file: saving the graph writes it again.\n" );
		sb.Append( Comment( Graph.Description ) );
		sb.Append( "import sbox.surface;\n" );
		sb.Append( "#include \"procedural.hlsl\"\n" );

		// Functions, once each, then samplers
		var functions = new StringBuilder();
		foreach ( var function in PixelResult.Functions.Concat( VertexResult.Functions ).Distinct() )
		{
			if ( ShaderTemplate.TryGetFunction( function, out var code ) )
				functions.Append( ShaderTemplate.ForSurface( code, IsPreview ) );
		}

		if ( functions.Length > 0 ) sb.Append( functions );

		var samplers = PixelResult.SamplerStates.Count > 0 || VertexResult.SamplerStates.Count > 0;
		if ( samplers ) sb.Append( '\n' );
		for ( int i = 0; i < PixelResult.SamplerStates.Count; ++i ) sb.AppendLine( SamplerDeclaration( PixelResult.SamplerStates[i], i ) );

		// The vertex stage's samplers have their own numbers in its code: a module global each, under another name
		if ( VertexResult.SamplerStates.Count > 0 )
		{
			for ( int i = 0; i < VertexResult.SamplerStates.Count; ++i )
				sb.AppendLine( SamplerDeclaration( VertexResult.SamplerStates[i], i ).Replace( $"g_sSampler{i} ", $"g_sVertexSampler{i} " ) );
		}

		if ( UsesFrameBufferCopy )
		{
			sb.Append( "\n// What's drawn behind the surface, which the engine copies for it (bWantsFBCopyTexture)\n" );
			sb.Append( "Texture2D g_tFrameBufferCopyTexture < Attribute( \"FrameBufferCopyTexture\" ); SrgbRead( false ); >;\n" );
			sb.Append( "SamplerState g_sSceneColor < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;\n" );
		}

		if ( UsesDepth ) sb.Append( ShaderTemplate.SurfaceDepth );

		if ( interpolated )
		{
			sb.Append( $"\n/// What the graph reads per vertex that the engine doesn't hand a surface: the mesh's colour, and what Displace sets (on semantics no mesh has, so they read zeros first)\n" );
			sb.Append( $"public struct {varyings}\n{{\n" );
			if ( Varyings.HasFlag( VaryingUse.Color ) || Varyings == VaryingUse.None ) sb.Append( "\tpublic float4 Color : COLOR0;\n" );
			if ( Varyings.HasFlag( VaryingUse.PositionOs ) ) sb.Append( "\tpublic float3 PositionOs : BINORMAL0;\n" );
			if ( Varyings.HasFlag( VaryingUse.NormalOs ) ) sb.Append( "\tpublic float3 NormalOs : BINORMAL1;\n" );
			if ( Varyings.HasFlag( VaryingUse.TangentOs ) ) sb.Append( "\tpublic float4 TangentOs : BINORMAL2;\n" );
			sb.Append( "}\n" );
		}

		// The struct: how it draws, its vertex inputs and attributes
		var attributes = new List<string> { "Material" };
		if ( Graph.BlendMode == BlendMode.Translucent ) attributes.Add( "Blend( \"Alpha\" )" );
		if ( Graph.BlendMode == BlendMode.Masked ) attributes.Add( "AlphaTest" );
		attributes.Add( "Features( \"Common\" )" );
		attributes.Add( Varyings.HasFlag( VaryingUse.Color ) || (interpolated && Varyings == VaryingUse.None) ? "Vertex( \"Common, SecondaryUv, Color\" )" : "Vertex( \"Common, SecondaryUv\" )" );
		attributes.Add( "AttributeExpression( \"renderbackfaces\", \"RenderBackfaces\" )" );
		if ( Graph.BlendMode == BlendMode.Translucent ) attributes.Add( "Attribute( \"translucent\" )" );
		if ( UsesFrameBufferCopy ) attributes.Add( "Attribute( \"bWantsFBCopyTexture\" )" );
		if ( UsesScreenDerivatives ) attributes.Add( "Attribute( \"ScreenDerivatives\" )" );

		var interfaces = lit
			? (interpolated ? "IInterpolatedVertexSurface" : "ISurface")
			: (interpolated ? "IInterpolatedVertexSurface, IInterpolatedCombinerSurface" : "ICombinerSurface");

		sb.Append( $"\n[{string.Join( ", ", attributes )}]\n" );
		sb.Append( $"public struct {type} : {interfaces}\n{{\n" );
		sb.Append( "\t[Group( \"Rendering\" ), Feature( \"F_RENDER_BACKFACES\" ), RenderState( \"TwoSided\" )] bool RenderBackfaces;\n" );
		if ( Graph.BlendMode == BlendMode.Masked )
			sb.Append( "\t[Group( \"Translucent\" ), Default( 0.5 ), Range( 0.01, 0.99 )] float AlphaTestReference;\n" );

		sb.Append( GenerateFields() );

		if ( interpolated ) sb.Append( $"\n\ttypealias Varyings = {varyings};\n" );

		// Evaluate: the pixel stage
		sb.Append( '\n' );
		sb.Append( interpolated ? $"\tpublic Material Evaluate( SurfaceInput i, {varyings} o )\n\t{{\n" : "\tpublic Material Evaluate( SurfaceInput i )\n\t{\n" );
		sb.Append( IndentString( pixelLocals?.Trim( '\n' ), 2 ) is { Length: > 0 } locals ? locals + "\n\n" : "" );
		sb.Append( IndentString( material.TrimEnd( '\n' ), 2 ) ).Append( '\n' );
		if ( Graph.BlendMode == BlendMode.Masked ) sb.Append( "\t\tm.AlphaTestReference = AlphaTestReference;\n" );
		sb.Append( "\t\treturn m;\n\t}\n" );

		if ( IsPreview )
		{
			// The preview's selected node: its value drawn as it is, through emission on a black surface when lit
			sb.Append( "\n\tstatic Material PreviewStage( SurfaceInput i, float4 value )\n\t{\n" );
			sb.Append( "\t\tMaterial m = Material.Init( i );\n" );
			sb.Append( lit
				? "\t\tm.Albedo = 0;\n\t\tm.Emission = value.rgb;\n\t\tm.Roughness = 1;\n\t\tm.AmbientOcclusion = 0;\n"
				: "\t\tm.Albedo = value.rgb;\n\t\tm.Unlit = true;\n\t\tm.Fog = false;\n" );
			sb.Append( "\t\treturn m;\n\t}\n" );
		}

		if ( interpolated )
		{
			sb.Append( $"\n\tpublic {varyings} Vertex( {varyings} o )\n\t{{\n\t\treturn o;\n\t}}\n" );
			sb.Append( $"\n\tpublic {varyings} Displace( inout WorldVertexInput v, {varyings} o )\n\t{{\n" );
			if ( Varyings.HasFlag( VaryingUse.PositionOs ) ) sb.Append( "\t\to.PositionOs = v.ObjectPosition;\n" );
			if ( Varyings.HasFlag( VaryingUse.NormalOs ) ) sb.Append( "\t\to.NormalOs = v.ObjectNormal;\n" );
			if ( Varyings.HasFlag( VaryingUse.TangentOs ) ) sb.Append( "\t\to.TangentOs = v.ObjectTangent;\n" );
			if ( displace is not null ) sb.Append( IndentString( displace.TrimEnd( '\n' ), 2 ) ).Append( '\n' );
			sb.Append( "\t\treturn o;\n\t}\n" );
		}

		sb.Append( "}\n" );
		return sb.ToString();
	}

	/// <summary>An unlit graph's material: its colour and opacity as they are, no lights and no fog.</summary>
	private string GenerateUnlitMaterial( BaseResult resultNode )
	{
		Stage = ShaderStage.Pixel;
		Subgraph = null;
		SubgraphStack.Clear();

		CurrentResultInput = "Albedo";
		var albedo = resultNode.GetAlbedoResult( this ).Cast( 3 ) ?? "float3( 1, 1, 1 )";
		CurrentResultInput = null;
		var opacity = resultNode.GetOpacityResult( this ).Cast( 1 ) ?? "1.0";

		return $"Material m = Material.Init( i );\nm.Albedo = {albedo};\nm.Opacity = {opacity};\nm.Metalness = 0;\nm.Unlit = true;\nm.Fog = false;\n";
	}

	/// <summary>A lit graph's material: the result node's inputs on the defaults, its normal from tangent space to world.</summary>
	private string GenerateMaterial()
	{
		Stage = ShaderStage.Pixel;
		Subgraph = null;
		SubgraphStack.Clear();

		var resultNode = Graph.Nodes.OfType<BaseResult>().FirstOrDefault();
		if ( resultNode == null )
			return null;

		var sb = new StringBuilder();
		sb.Append( ShaderTemplate.MaterialInit );

		var visited = new HashSet<string>();

		foreach ( var property in GetNodeInputProperties( resultNode.GetType() ) )
		{
			if ( property.Name == "PositionOffset" )
				continue;

			CurrentResultInput = property.Name;
			visited.Add( property.Name );

			NodeResult result;

			if ( property.GetValue( resultNode ) is NodeInput connection && connection.IsValid() )
			{
				result = Result( connection );
			}
			else
			{
				var editorAttribute = property.GetCustomAttribute<BaseNode.EditorAttribute>();
				if ( editorAttribute == null )
					continue;

				var valueProperty = resultNode.GetType().GetProperty( editorAttribute.ValueName );
				if ( valueProperty == null )
					continue;

				result = ResultValue( valueProperty.GetValue( resultNode ) );
			}

			if ( Errors.Any() )
				return null;

			if ( !result.IsValid() )
				continue;

			if ( string.IsNullOrWhiteSpace( result.Code ) )
				continue;

			var inputAttribute = property.GetCustomAttribute<BaseNode.InputAttribute>();
			var componentCount = GetComponentCount( inputAttribute.Type );

			sb.AppendLine( $"m.{property.Name} = {result.Cast( componentCount )};" );
		}

		if ( resultNode is FunctionResult functionResult )
		{
			functionResult.AddMaterialOutputs( this, sb, visited );
		}

		CurrentResultInput = null;

		sb.Append( ShaderTemplate.MaterialOutput );
		return sb.ToString();
	}

	/// <summary>The vertex stage's code, which moves the vertex by the position offset in world space; null without one.</summary>
	private string GenerateDisplace( BaseResult resultNode )
	{
		Stage = ShaderStage.Vertex;
		Subgraph = null;
		SubgraphStack.Clear();

		var connection = resultNode.GetPositionOffset();
		if ( !connection.IsValid() )
			return null;

		var result = Result( connection );
		if ( Errors.Any() || !result.IsValid() || string.IsNullOrWhiteSpace( result.Code ) )
			return null;

		var sb = new StringBuilder();
		foreach ( var local in ShaderResult.Results )
		{
			sb.AppendLine( $"{local.Item2.TypeName} {local.Item1} = {local.Item2.Code};" );
		}

		sb.AppendLine( $"v.Position += {result.Cast( 3 )};" );

		// The vertex stage's samplers are its own
		var code = sb.ToString();
		for ( int i = VertexResult.SamplerStates.Count - 1; i >= 0; --i )
			code = code.Replace( $"g_sSampler{i}", $"g_sVertexSampler{i}" );

		return code;
	}

	/// <summary>
	/// The struct's fields: each material texture (its inputs and how the material compiler makes it), each parameter (as the
	/// material editor shows it) and each engine-bound value; in the preview, every value and texture is bound by attribute.
	/// </summary>
	private string GenerateFields()
	{
		var sb = new StringBuilder();
		var declared = new HashSet<string>( StringComparer.Ordinal );

		foreach ( var result in new[] { PixelResult, VertexResult } )
		{
			foreach ( var (id, input) in result.TextureInputs )
			{
				var cube = input.Type == TextureType.TexCube;
				var type = cube ? "TextureCube" : "Texture2D";

				// Bound by attribute: the preview's, and those the graph marks as attributes
				if ( IsPreview || input.IsAttribute )
				{
					if ( declared.Add( $"g_t{id}" ) )
						sb.Append( $"\t[Engine( \"{id}\" ){(IsPreview && input.SrgbRead ? ", Srgb" : "")}] {type} g_t{id};\n" );
					continue;
				}

				var field = FieldName( $"g_t{id}", "g_t" );
				if ( !declared.Add( field.Name ) )
					continue;

				var defaults = string.IsNullOrEmpty( input.DefaultTexture ) ? $"Default4( {Floats( input.Default )} )" : $"DefaultFile( \\\"{Quote( input.DefaultTexture )}\\\" )";
				var settings = $"{input.ColorSpace}; {input.Processor}; _{input.ExtensionString.ToLower()}; {defaults}";
				var attributes = new List<string>();
				if ( field.Source is not null ) attributes.Add( $"Source( \"{field.Source}\" )" );
				if ( input.SrgbRead ) attributes.Add( "Srgb" );
				attributes.Add( $"InputWith( \"{id}\", \"{Quote( input.UIGroup )}\", \"{settings}\" )" );
				attributes.Add( $"Pack( \"RGBA: {id} {(input.SrgbRead ? "Srgb" : "Linear")}\", \"{input.ImageFormat}\" )" );
				if ( !cube && result.RepresentativeTexture == $"g_t{id}" )
				{
					attributes.Add( "TextureAttribute( \"LightSim_DiffuseAlbedoTexture\" )" );
					attributes.Add( "TextureAttribute( \"RepresentativeTexture\" )" );
				}

				sb.Append( $"\t[{string.Join( ", ", attributes )}] {type} {field.Name};\n" );
			}

			foreach ( var parameter in result.SurfaceParameters.Values )
			{
				if ( !declared.Add( parameter.Field ) )
					continue;

				var type = parameter.Components switch { 0 => "bool", 1 => "float", var n => $"float{n}" };
				if ( parameter.IsAttribute )
				{
					sb.Append( $"\t[Engine( \"{Quote( parameter.Attribute )}\" ), {DefaultAttribute( parameter.Value )}] {type} {parameter.Field};\n" );
					continue;
				}

				var attributes = new List<string> { $"Group( \"{Quote( parameter.UI.UIGroup )}\" )" };
				if ( parameter.Source is not null ) attributes.Add( $"Source( \"{parameter.Source}\" )" );
				if ( parameter.UI.Type == UIType.Color ) attributes.Add( "Color" );
				attributes.Add( DefaultAttribute( parameter.Value ) );
				if ( parameter.IsRange && parameter.Components > 0 )
				{
					// A field's range is one for all its components
					var min = Components( parameter.Min ).Take( parameter.Components ).Min();
					var max = Components( parameter.Max ).Take( parameter.Components ).Max();
					attributes.Add( $"Range( {Number( min )}, {Number( max )} )" );
				}

				sb.Append( $"\t[{string.Join( ", ", attributes )}] {type} {parameter.Field};\n" );
			}

			// The preview's values, bound by attribute so it changes them without compiling
			if ( IsPreview )
			{
				foreach ( var (name, value) in result.Attributes )
				{
					if ( declared.Add( name ) )
						sb.Append( $"\t[Engine( \"{name}\" )] {AttributeType( value )} {name};\n" );
				}
			}
		}

		if ( IsPreview )
		{
			sb.Append( "\t[Engine( \"g_flPreviewTime\" )] float g_flPreviewTime;\n" );
			sb.Append( "\t[Engine( \"g_iStageId\" )] int g_iStageId;\n" );
		}

		return sb.Length > 0 ? "\n" + sb.ToString() : "";
	}
}
