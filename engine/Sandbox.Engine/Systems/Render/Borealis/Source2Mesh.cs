using NativeEngine;
using System.Runtime.CompilerServices;

namespace Borealis.Gpu.Source2;

/// <summary>
/// A Source 2 model's geometry: the engine's <see cref="Model"/>, wrapped once so identity holds. Its draws bind the model's
/// own compiled vertex and index buffers; nothing is copied.
/// </summary>
internal sealed class Source2Mesh : GpuMesh
{
	public Model Model { get; }

	Source2Mesh( Model model )
	{
		Model = model;
	}

	static readonly ConditionalWeakTable<Model, Source2Mesh> wrapped = new();

	/// <summary>
	/// The engine model's geometry as Borealis's, or null.
	/// </summary>
	public static GpuMesh From( Model model ) => model is null ? null : wrapped.GetValue( model, static m => new Source2Mesh( m ) );

	public override string Name => Model.ResourcePath ?? Model.Name;

	/// <summary>
	/// A model's LODs, draws, switch distances and skinning as a mesh Borealis draws. Throws without geometry. Keep the model
	/// alive while GPU frames use its buffers.
	/// </summary>
	public static RenderMesh FromModel( Model model )
	{
		ArgumentNullException.ThrowIfNull( model );

		var info = model.MeshInfo;
		var lodCount = Math.Max( 1, info.LodCount );
		var lods = new RenderMesh.Draw[lodCount][];

		for ( int lod = 0; lod < lodCount; lod++ )
		{
			var draws = new List<RenderMesh.Draw>();
			for ( int m = 0; m < info.Meshes.Length; m++ )
			{
				if ( (info.Meshes[m].LodMask & (1 << lod)) == 0 ) continue;

				var drawCalls = info.Meshes[m].DrawCalls;
				for ( int d = 0; d < drawCalls.Length; d++ )
				{
					var call = drawCalls[d];
					var material = Source2Material.From( call.Material );
					var translucent = RenderMesh.IsTranslucent( material );
					var alphaTest = RenderMesh.IsAlphaTest( material );
					var fastPath = material is { ShadowFastPath: true } && !translucent && !alphaTest;
					draws.Add( new RenderMesh.Draw( m, d, call.StartIndex, call.Indices, call.Vertices, call.PrimitiveType, material, translucent, alphaTest, fastPath, Tint: ReadDrawTint( model, m, d ), UvDensity: call.UvDensity ) );
				}
			}

			lods[lod] = draws.ToArray();
		}

		if ( lods[0].Length == 0 )
			throw new InvalidOperationException( $"Model {model.ResourcePath} has no render geometry" );

		var mesh = new RenderMesh( From( model ), model.Bounds, lods, info.LodSwitchDistances ?? [] );

		var skins = new RenderMesh.MeshSkin[info.Meshes.Length];
		for ( int m = 0; m < skins.Length; m++ )
			skins[m] = ReadMeshSkin( model, m );

		var bindPose = new Matrix3x4[model.BoneCount];
		for ( int b = 0; b < bindPose.Length; b++ )
			bindPose[b] = Matrix3x4.From( model.GetBoneTransform( b ) );

		mesh.SetSkinning( skins, bindPose );
		return mesh;
	}

	/// <summary>
	/// Read a draw's linear tint, or null for white (<c>RenderTools.GetModelDrawCallTint</c>).
	/// </summary>
	static unsafe Vector3? ReadDrawTint( Model model, int mesh, int drawCall )
	{
		Vector3 tint = Vector3.One;
		if ( !RenderTools.GetModelDrawCallTint( model.native, mesh, drawCall, (IntPtr)(&tint) ) ) return null;
		return tint == Vector3.One ? null : tint;
	}

	/// <summary>
	/// Read mesh skinning data, or null without a render skeleton.
	/// </summary>
	static unsafe RenderMesh.MeshSkin ReadMeshSkin( Model model, int mesh )
	{
		int bones = 0, weights = 0;
		var vertices = RenderTools.GetModelMeshSkinning( model.native, mesh, (IntPtr)(&bones), (IntPtr)(&weights) );
		if ( vertices < 0 || bones <= 0 ) return null;

		var skin = new RenderMesh.MeshSkin
		{
			BlendWeightCount = weights,
			VertexCount = vertices,
			InverseBindPoses = new Matrix3x4[bones],
			MasterBones = new int[bones],
			Morphs = RenderTools.ModelMeshHasMorphs( model.native, mesh ),
		};

		fixed ( Matrix3x4* poses = skin.InverseBindPoses )
		fixed ( int* masters = skin.MasterBones )
		{
			if ( !RenderTools.GetModelMeshSkinningBones( model.native, mesh, (IntPtr)poses, (IntPtr)masters, bones ) )
				return null;
		}

		return skin;
	}
}

/// <summary>
/// Borealis's meshes as the engine's models, while the rest of Borealis still records through engine APIs.
/// </summary>
internal static class Source2MeshExtensions
{
	/// <summary>
	/// The engine model behind a Borealis mesh, or null.
	/// </summary>
	public static Model Engine( this GpuMesh mesh ) => (mesh as Source2Mesh)?.Model;

	/// <summary>
	/// The native model behind a Borealis mesh, or an invalid handle.
	/// </summary>
	public static IModel Native( this GpuMesh mesh ) => mesh is Source2Mesh m ? m.Model.native : default;
}
