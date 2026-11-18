using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.Linq;

namespace Borealis.Backend;

/// <summary>
/// The pass families a surface's materials draw with, from their render state (<see cref="SurfacePassNotes.Of"/>): one
/// colour pass and at most one depth pass each.
/// </summary>
[Flags]
internal enum SurfacePassFamilies
{
	None = 0,

	/// <summary>Opaque colour (<c>Forward</c>).</summary>
	Forward = 1,

	/// <summary>Alpha-tested colour (<c>ForwardAlphaTest</c>, or <c>Forward</c> where a shader has none).</summary>
	ForwardAlphaTest = 2,

	/// <summary>Blended or translucent-layer colour (<c>ForwardTranslucent</c>).</summary>
	ForwardTranslucent = 4,

	/// <summary>Depth prepass and shadows of opaque materials (<c>Depth</c>).</summary>
	Depth = 8,

	/// <summary>Depth prepass and shadows of alpha-tested materials (<c>DepthAlphaTest</c>).</summary>
	DepthAlphaTest = 16,
}

/// <summary>What a material is drawn on, as a model's draw call says (<see cref="SurfacePassNotes.Of"/>).</summary>
[Flags]
internal enum SurfaceDrawKinds
{
	None = 0,

	/// <summary>A static mesh: GPU-driven where the device draws that way (<c>*GpuDriven</c>), else classic.</summary>
	Static = 1,

	/// <summary>A static mesh with baked lightmaps (<c>*GpuDrivenLightmapped</c>, or <c>*Lightmapped</c> classic).</summary>
	StaticLightmapped = 2,

	/// <summary>A skinned mesh drawn from the vertex cache (<c>*Skinned</c>).</summary>
	Skinned = 4,

	/// <summary>A classic draw (one-weight skinned meshes, objects the GPU-driven path leaves, materials a game draws itself).</summary>
	Dynamic = 8,

	All = Static | StaticLightmapped | Skinned | Dynamic,
}

/// <summary>
/// The passes a surface's materials draw with, noted as they load (<see cref="SurfaceShaders.NoteMaterial"/>): pairs of a
/// render-state family and a kind of draw, one bit each, so a material's alpha test doesn't make another material's
/// lightmapped geometry guess its alpha-tested passes. A program guesses the parts of the passes its notes resolve to
/// (<see cref="Resolve"/>), not every pass it has.
/// </summary>
internal static class SurfacePassNotes
{
	const int Families = 5, Kinds = 4;

	/// <summary>Every pair.</summary>
	public const uint All = (1u << (Families * Kinds)) - 1;

	/// <summary>The pairs of static draws (<see cref="SurfaceDrawKinds.Static"/>, <see cref="SurfaceDrawKinds.StaticLightmapped"/>).</summary>
	public static readonly uint StaticOnly = Pairs( (SurfacePassFamilies)((1 << Families) - 1), SurfaceDrawKinds.Static | SurfaceDrawKinds.StaticLightmapped );

	/// <summary>
	/// The pairs a material with <paramref name="state"/> drawn as <paramref name="kinds"/> has, its families as
	/// BackendContext.DrawSurface and SurfaceBucket pick them: translucent colour when it draws in the translucent layer,
	/// else alpha-tested or opaque colour; a depth pass when it writes depth outside the translucent layer.
	/// </summary>
	public static uint Of( SurfaceRenderState state, SurfaceDrawKinds kinds )
	{
		var colour = state.DrawsTranslucent ? SurfacePassFamilies.ForwardTranslucent : state.AlphaTest ? SurfacePassFamilies.ForwardAlphaTest : SurfacePassFamilies.Forward;
		var depth = !state.DepthWrite || state.Translucent ? SurfacePassFamilies.None : state.AlphaTest ? SurfacePassFamilies.DepthAlphaTest : SurfacePassFamilies.Depth;
		return Pairs( colour | depth, kinds );
	}

	static uint Pairs( SurfacePassFamilies families, SurfaceDrawKinds kinds )
	{
		uint notes = 0;
		for ( int f = 0; f < Families; f++ )
		{
			if ( ((int)families & (1 << f)) == 0 ) continue;
			for ( int k = 0; k < Kinds; k++ )
			{
				if ( ((int)kinds & (1 << k)) != 0 ) notes |= 1u << (f * Kinds + k);
			}
		}

		return notes;
	}

	/// <summary>The kind of draw a pass is for, by its name: GPU-driven passes static, skinned ones skinned, the rest classic.</summary>
	public static SurfaceDrawKinds KindOf( string pass ) =>
		pass.Contains( ShaderPasses.GpuDriven, StringComparison.Ordinal ) ? (pass.Contains( ShaderPasses.Lightmapped, StringComparison.Ordinal ) ? SurfaceDrawKinds.StaticLightmapped : SurfaceDrawKinds.Static)
		: pass.Contains( ShaderPasses.Skinned, StringComparison.Ordinal ) ? SurfaceDrawKinds.Skinned : SurfaceDrawKinds.Dynamic;

	/// <summary>
	/// The passes of <paramref name="program"/> that the pairs in <paramref name="notes"/> draw with, each with its family,
	/// as the draws pick them: a static mesh's GPU-driven pass where the device draws GPU-driven and the shader has it,
	/// else its classic one; a lightmapped draw's lightmapped pass, else the plain one; alpha-tested classic colour falls
	/// back to opaque colour where the shader has no alpha-tested pass; a skinned draw has its skinned pass or none.
	/// </summary>
	public static List<(SurfaceProgram.PassObjects Pass, SurfacePassFamilies Family)> Resolve( uint notes, SurfaceProgram program, bool gpuDriven )
	{
		var found = new List<(SurfaceProgram.PassObjects, SurfacePassFamilies)>();
		for ( int f = 0; f < Families; f++ )
		{
			for ( int k = 0; k < Kinds; k++ )
			{
				if ( (notes & (1u << (f * Kinds + k))) == 0 ) continue;
				var family = (SurfacePassFamilies)(1 << f);
				var pass = Pass( program, family, (SurfaceDrawKinds)(1 << k), gpuDriven );
				if ( pass is not null && !found.Contains( (pass, family) ) ) found.Add( (pass, family) );
			}
		}

		return found;
	}

	static SurfaceProgram.PassObjects Pass( SurfaceProgram program, SurfacePassFamilies family, SurfaceDrawKinds kind, bool gpuDriven )
	{
		var noColor = family is SurfacePassFamilies.Depth or SurfacePassFamilies.DepthAlphaTest;
		var alphaTest = family is SurfacePassFamilies.ForwardAlphaTest or SurfacePassFamilies.DepthAlphaTest;
		var lightmapped = kind == SurfaceDrawKinds.StaticLightmapped;
		if ( gpuDriven && kind is SurfaceDrawKinds.Static or SurfaceDrawKinds.StaticLightmapped && program.GpuDrivenPass( noColor, alphaTest, lightmapped, family == SurfacePassFamilies.ForwardTranslucent ) is { } driven ) return driven;

		var name = family switch
		{
			SurfacePassFamilies.Depth => ShaderPasses.Depth,
			SurfacePassFamilies.DepthAlphaTest => ShaderPasses.DepthAlphaTest,
			SurfacePassFamilies.ForwardTranslucent => ShaderPasses.Forward + "Translucent",
			SurfacePassFamilies.ForwardAlphaTest when program.Pass( ShaderPasses.Forward + "AlphaTest" ) is not null => ShaderPasses.Forward + "AlphaTest",
			_ => ShaderPasses.Forward,
		};

		if ( kind == SurfaceDrawKinds.Skinned ) return program.Pass( name + ShaderPasses.Skinned );
		return (lightmapped ? program.Pass( name + ShaderPasses.Lightmapped ) : null) ?? program.Pass( name );
	}
}

/// <summary>
/// A view's specialization values as surface pipelines were made in it (<see cref="SurfaceShaders.NoteView"/>): whether
/// it's the 3D skybox's, whether it has the opaque pass's normals target, and the global attributes' values (shadow filter
/// quality, DDGI). Guesses are made per view kind, so a skybox's or a reflection's draws find their parts too.
/// </summary>
internal sealed class SurfaceView( string key, bool skybox, bool normals, Dictionary<string, int> values )
{
	/// <summary>Tells views apart.</summary>
	public readonly string Key = key;

	public readonly bool Skybox = skybox, Normals = normals;

	/// <summary>Attribute values by attribute, as the view's draws took them.</summary>
	public readonly Dictionary<string, int> Values = values;

	/// <summary>
	/// The view a pipeline of <paramref name="pass"/> was made in: what its constants say, by slot. Fading and the material
	/// offset are the draw's, not the view's, so left out.
	/// </summary>
	public static SurfaceView Of( SurfaceProgram.PassObjects pass, SpecializationConstant[] vertexConstants, SpecializationConstant[] pixelConstants, bool normals )
	{
		var values = new Dictionary<string, int>( StringComparer.Ordinal );
		var skybox = false;
		Read( pass.Pass.Vertex, vertexConstants );
		Read( pass.Pass.Pixel, pixelConstants );
		var key = $"{(skybox ? 's' : '-')}{(normals ? 'n' : '-')}{string.Concat( values.OrderBy( v => v.Key, StringComparer.Ordinal ).Select( v => $";{v.Key}={v.Value}" ) )}";
		return new SurfaceView( key, skybox, normals, values );

		void Read( CompiledStage stage, SpecializationConstant[] constants )
		{
			if ( stage is null || constants is null ) return;
			foreach ( var constant in constants )
			{
				if ( constant.Id == BackendContext.SkyboxId ) skybox |= constant.Value != 0;
				foreach ( var slot in stage.Specializations )
				{
					if ( slot.Id == constant.Id ) values[slot.Attribute] = (int)constant.Value;
				}
			}
		}
	}
}
