using Borealis.Source2;
using Borealis.Source2.Models;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using V3 = System.Numerics.Vector3;
using V4 = System.Numerics.Vector4;

namespace BorealisGpuTests;

/// <summary>
/// Reading s&amp;box's compiled models (<c>.vmdl_c</c>) from the checkout's <c>game/core</c> and the citizen addon, which
/// Bootstrap builds. Inconclusive where they haven't been built.
/// </summary>
[TestClass]
public class ModelFileTests
{
	static ContentFiles Files
	{
		get
		{
			var files = ContentFiles.FindGame();
			if ( files.Find( "models/dev/box.vmdl" ) is null ) Assert.Inconclusive( "Compiled models aren't built (run Bootstrap)" );
			return files;
		}
	}

	static ModelFile Load( string path )
	{
		var files = Files;
		if ( files.Find( path ) is null ) Assert.Inconclusive( $"{path} isn't built" );
		return ModelFile.Load( files, path );
	}

	/// <summary>
	/// A mesh made in code without indices (Special Delivery's crystal shards: vertices and <c>Mesh.SetVertexRange</c>) has
	/// draws with a vertex range and no index buffer, which native draws unindexed: they become ranges of an index buffer
	/// counting up from zero, reaching the same vertices, and an indexed draw beside them is left as it was.
	/// </summary>
	[TestMethod]
	public void UnindexedDrawsGetCountingIndices()
	{
		var draws = new List<MeshDrawCall>
		{
			new() { BaseVertex = 3, VertexCount = 6, IndexBuffer = (0, 0), VertexBuffers = [(0, 0)] },
			new() { BaseVertex = 0, VertexCount = 3, StartIndex = 0, IndexCount = 3, IndexBuffer = (0, 0), VertexBuffers = [(0, 0)] },
		};
		var existing = new MeshIndexBuffer( 3, 2, new byte[6] );

		var indexBuffers = ModelMesh.IndexUnindexedDraws( draws, [existing] );

		Assert.AreEqual( 2, indexBuffers.Length, "an index buffer added" );
		var counting = indexBuffers[1];
		Assert.AreEqual( 9, counting.Count, "as far as the draw's last vertex" );
		Assert.AreEqual( 7u, BitConverter.ToUInt32( counting.Data, 7 * 4 ) );
		Assert.AreEqual( (1, 0), draws[0].IndexBuffer );
		Assert.AreEqual( 3, draws[0].StartIndex, "from the first vertex" );
		Assert.AreEqual( 6, draws[0].IndexCount );
		Assert.AreEqual( 0, draws[0].BaseVertex );
		Assert.AreEqual( (0, 0), draws[1].IndexBuffer, "the indexed draw keeps its buffer" );
		Assert.AreEqual( 3, draws[1].IndexCount );
	}

	/// <summary>
	/// Every compiled model in the content reads: meshes, buffers (meshopt decoded), draws and skeletons. Every vertex
	/// lies in its mesh's bounds, every index reaches a vertex, and packed normals decode to unit vectors at right angles
	/// to their tangents, which a misdecoded buffer wouldn't manage.
	/// </summary>
	[TestMethod]
	public void EveryModelReads()
	{
		var files = Files;
		var timer = Stopwatch.StartNew();
		int read = 0, meshes = 0, draws = 0, encoded = 0;
		var failures = new List<string>();

		foreach ( var root in files.Roots )
		{
			foreach ( var path in Directory.EnumerateFiles( root, "*.vmdl_c", SearchOption.AllDirectories ) )
			{
				try
				{
					var model = ModelFile.Read( File.ReadAllBytes( path ), mesh => files.Find( mesh ) is { } found ? File.ReadAllBytes( found ) : null );
					foreach ( var mesh in model.Meshes )
					{
						Check( mesh );
						meshes++;
						draws += mesh.DrawCalls.Count;
					}

					encoded += model.Meshes.Sum( m => m.VertexBuffers.Count + m.IndexBuffers.Count );
					read++;
				}
				catch ( Exception e )
				{
					failures.Add( $"{Path.GetRelativePath( root, path )}: {e.Message}" );
				}
			}
		}

		Console.WriteLine( $"{read} models read ({meshes} meshes, {draws} draws, {encoded} buffers) in {timer.ElapsedMilliseconds}ms, {failures.Count} failed" );
		foreach ( var failure in failures ) Console.WriteLine( failure );
		Assert.IsTrue( read > 0, "no models found" );
		Assert.AreEqual( 0, failures.Count, string.Join( "\n", failures.Take( 10 ) ) );
	}

	static void Check( ModelMesh mesh )
	{
		foreach ( var draw in mesh.DrawCalls )
		{
			Assert.AreEqual( PrimitiveKind.Triangles, draw.Primitive, mesh.Name );
			var indices = mesh.IndexBuffers[draw.IndexBuffer.Buffer];
			var vertices = mesh.VertexBuffers[draw.VertexBuffers[0].Buffer];
			Assert.IsTrue( draw.StartIndex + draw.IndexCount <= indices.Count, $"{mesh.Name}: indices out of range" );
			for ( int i = draw.StartIndex; i < draw.StartIndex + draw.IndexCount; i++ )
			{
				if ( indices[i] + draw.BaseVertex >= vertices.Count ) Assert.Fail( $"{mesh.Name}: index {indices[i]} of {vertices.Count} vertices" );
			}
		}

		var slack = (mesh.Maxs - mesh.Mins).Length() * 0.01f + 0.01f;
		foreach ( var buffer in mesh.VertexBuffers )
		{
			var position = buffer.Find( "POSITION" ) ?? throw new InvalidDataException( $"{mesh.Name} has no positions" );
			var normal = buffer.Find( "NORMAL" );
			for ( int v = 0; v < buffer.Count; v++ )
			{
				var p = buffer.Read( position, v );
				var inside = p.X >= mesh.Mins.X - slack && p.Y >= mesh.Mins.Y - slack && p.Z >= mesh.Mins.Z - slack && p.X <= mesh.Maxs.X + slack && p.Y <= mesh.Maxs.Y + slack && p.Z <= mesh.Maxs.Z + slack;
				if ( !inside ) Assert.Fail( $"{mesh.Name}: vertex {v} at {p} is outside {mesh.Mins}..{mesh.Maxs}" );

				if ( normal?.Format is not (ColorFormat.R10G10B10A2_UNORM or ColorFormat.R8G8B8A8_UNORM) ) continue;
				var packed = BitConverter.ToUInt32( buffer.Data, v * buffer.Stride + normal.Value.Offset );
				var (n, t) = normal.Value.Format == ColorFormat.R10G10B10A2_UNORM ? TangentFrame.UnpackOctDiamond( packed ) : TangentFrame.UnpackLegacy( packed );
				Assert.AreEqual( 1, n.Length(), 0.01f, $"{mesh.Name}: normal of vertex {v}" );
				Assert.AreEqual( 0, V3.Dot( n, new V3( t.X, t.Y, t.Z ) ), 0.08f, $"{mesh.Name}: tangent of vertex {v}" );
			}
		}
	}

	/// <summary>
	/// The 10:10:10:2 tangent frame packs and unpacks as native's does, to within its precision, and the older 8:8:8:8
	/// encoding repacks into it.
	/// </summary>
	[TestMethod]
	public void TangentFramesRoundTrip()
	{
		var random = new Random( 7 );
		for ( int i = 0; i < 1000; i++ )
		{
			var n = V3.Normalize( new V3( random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1 ) );
			var t = V3.Normalize( V3.Cross( n, V3.Normalize( new V3( random.NextSingle(), random.NextSingle() - 0.5f, 0.3f ) ) ) );
			var sign = i % 2 == 0 ? 1 : -1;

			var (normal, tangent) = TangentFrame.UnpackOctDiamond( TangentFrame.PackOctDiamond( n, t, sign ) );
			Assert.IsTrue( V3.Dot( n, normal ) > 0.99999f, $"normal {n} came back {normal}" );
			Assert.IsTrue( V3.Dot( t, new V3( tangent.X, tangent.Y, tangent.Z ) ) > 0.9999f, $"tangent {t} came back {tangent}" );
			Assert.AreEqual( sign, tangent.W );
		}

		// The old encoding (PackNormal_UBYTE4): +z is 0xC0 0xC0, a tangent along +x with a positive sign 0xFF 0xC0
		var (z, x) = TangentFrame.UnpackLegacy( 0xC0FF_C0C0 );
		Assert.IsTrue( V3.Distance( z, V3.UnitZ ) < 1e-5f, $"{z}" );
		Assert.IsTrue( V4.Distance( x, new V4( 1, 0, 0, 1 ) ) < 1e-5f, $"{x}" );
	}

	/// <summary>The dev box: one mesh, one draw of 12 triangles, packed normals, a 50-unit cube.</summary>
	[TestMethod]
	public void DevBox()
	{
		var model = Load( "models/dev/box.vmdl" );
		Assert.AreEqual( "models/dev/box.vmdl", model.Name );
		Assert.AreEqual( 1, model.Meshes.Count );
		Assert.AreEqual( 1, model.LodCount );
		Assert.AreEqual( new V3( -25 ), model.Mins );
		Assert.AreEqual( new V3( 25 ), model.Maxs );
		Assert.IsFalse( model.IsSkinned );

		var draw = model.Meshes[0].DrawCalls.Single();
		Assert.AreEqual( "materials/dev/primary_white.vmat", draw.Material );
		Assert.AreEqual( 36, draw.IndexCount );
		Assert.AreEqual( 24, draw.VertexCount );
		Assert.IsTrue( draw.CompressedTangentFrame );

		var vertices = model.Meshes[0].VertexBuffers.Single();
		Assert.AreEqual( 24, vertices.Count );
		CollectionAssert.AreEquivalent( new[] { "POSITION", "TEXCOORD", "NORMAL" }, vertices.Fields.Select( f => f.Semantic ).ToArray() );
	}

	/// <summary>
	/// What the engine reports for these models (<c>Model.MeshInfo</c> and <c>Model.Bounds</c>, read once through
	/// <c>borealislab -model-parity</c>, which also checks every draw's range and material and every vertex against the
	/// engine's own buffers): vertices as <c>CRenderMesh::CalculateNumTriangles</c> counts them, triangles, draws, LODs
	/// (at least one) and bounds, with each LOD's materials from the reader.
	/// </summary>
	[TestMethod]
	[DataRow( "models/dev/box.vmdl", 24, 12, 1, 1, -25f, -25f, -25f, 25f, 25f, 25f, "materials/dev/primary_white.vmat" )]
	[DataRow( "models/dev/gordon_at_desk.vmdl", 1400, 830, 3, 1, -8.8459f, -13.1256f, -52.6451f, 35.9664f, 13.1256f, 6.4164f, "materials/models/editor/player_start.vmat;dev/helper/reflectivity_90b.vmat;dev/helper/testgrid.vmat" )]
	[DataRow( "models/citizen_props/trashcan01.vmdl", 1287, 1681, 6, 3, -17.7754f, -16.8806f, 0.206f, 17.9443f, 18.2964f, 45.9803f, "models/citizen_props/trashcan01.vmat;models/citizen_props/trashbag01.vmat" )]
	[DataRow( "models/citizen/citizen.vmdl", 12684, 21216, 23, 5, -5.2418f, -25.683f, -0.0353f, 8.6324f, 25.683f, 71.6743f, null )]
	[DataRow( "models/citizen_human/human.vmdl", 70474, 128891, 69, 4, -5.7833f, -23.933f, -0.0696f, 6.3864f, 23.9598f, 68.9154f, null )]
	public void MatchesEngine( string path, int vertices, int triangles, int draws, int lods, float minX, float minY, float minZ, float maxX, float maxY, float maxZ, string lod0Materials )
	{
		var model = Load( path );
		Assert.AreEqual( vertices, model.Meshes.Sum( m => m.VertexCount ), "vertices" );
		Assert.AreEqual( triangles, model.Meshes.Sum( m => m.TriangleCount ), "triangles" );
		Assert.AreEqual( draws, model.Meshes.Sum( m => m.DrawCalls.Count ), "draws" );
		Assert.AreEqual( lods, model.LodCount, "LODs" );
		Assert.IsTrue( V3.Distance( new V3( minX, minY, minZ ), model.Mins ) < 1e-3f, $"mins {model.Mins}" );
		Assert.IsTrue( V3.Distance( new V3( maxX, maxY, maxZ ), model.Maxs ) < 1e-3f, $"maxs {model.Maxs}" );

		if ( lod0Materials is null ) return;
		var materials = model.Meshes.Where( m => (m.LodMask & 1) != 0 ).SelectMany( m => m.DrawCalls ).Select( d => d.Material ).Distinct().ToArray();
		CollectionAssert.AreEqual( lod0Materials.Split( ';' ), materials );
	}

	/// <summary>
	/// The citizen: twenty meshes over five LODs, five body groups each with two choices, a 95-bone skeleton, and a head
	/// with morphs (not read). Its first bone's bind pose matches its first mesh's inverse bind pose.
	/// </summary>
	[TestMethod]
	public void Citizen()
	{
		var model = Load( "models/citizen/citizen.vmdl" );
		Assert.AreEqual( 20, model.Meshes.Count );
		Assert.AreEqual( 5, model.LodCount );
		CollectionAssert.AreEqual( new[] { 0f, 5, 20, 40, 70 }, model.LodSwitchDistances.ToArray() );
		CollectionAssert.AreEqual( new[] { "Head", "Chest", "Legs", "Hands", "Feet" }, model.BodyGroups.Select( b => b.Name ).ToArray() );
		Assert.IsTrue( model.BodyGroups.All( b => b.Choices.Count == 2 ) );
		Assert.AreEqual( 341ul, model.DefaultMeshGroupMask );
		Assert.AreEqual( 95, model.Skeleton.Count );
		Assert.AreEqual( "pelvis", model.Skeleton.Names[0] );
		Assert.AreEqual( -1, model.Skeleton.Parents[0] );
		Assert.IsTrue( model.IsSkinned );
		Assert.IsTrue( model.HasMorphs );
		Assert.AreEqual( "models/citizen/citizen/head_lod0_vmorf.vtex", model.Meshes[16].MorphTexture );

		// The torso's first bone is the pelvis: its bind pose times its inverse bind pose is the identity
		var torso = model.Meshes[0];
		var remap = model.MeshBoneToModelBone( 0 );
		Assert.AreEqual( torso.Bones.Count, remap.Length );
		Assert.AreEqual( "pelvis", model.Skeleton.Names[remap[0]] );
		var bindPose = model.Skeleton.BindPose();
		for ( int b = 0; b < torso.Bones.Count; b++ )
		{
			Assert.AreEqual( torso.Bones[b].Name, model.Skeleton.Names[remap[b]] );
			var identity = torso.Bones[b].InverseBindPose * bindPose[remap[b]];
			Assert.IsTrue( Near( identity, Matrix4x4.Identity, 0.02f ), $"{torso.Bones[b].Name}: {identity}" );
		}
	}

	static bool Near( Matrix4x4 a, Matrix4x4 b, float tolerance )
	{
		for ( int r = 0; r < 4; r++ )
		{
			for ( int c = 0; c < 4; c++ )
			{
				if ( MathF.Abs( a[r, c] - b[r, c] ) > tolerance ) return false;
			}
		}

		return true;
	}

	/// <summary>
	/// The human's morph sets read (<c>MRPH</c>): positions then normals and wrinkles, every morph's rectangles with both
	/// bundles' offsets and ranges, flex controllers and rules, and a compiled texture atlas; with no controller set no
	/// flex desc is weighted, and setting each controller weights some.
	/// </summary>
	[TestMethod]
	public void MorphSetsRead()
	{
		var files = Files;
		var model = Load( "models/citizen_human/human.vmdl" );
		var morphing = model.Meshes.Where( x => x.HasMorphs ).ToArray();
		Assert.IsTrue( morphing.Length > 0, "the human has meshes with morphs" );

		foreach ( var mesh in morphing )
		{
			var set = mesh.Morphs;
			Assert.IsNotNull( set, $"{mesh.Name}'s morph set reads" );
			Console.WriteLine( $"{mesh.Name}: {set.Width}x{set.Height}, {set.Morphs.Count} morphs, {set.FlexDescs.Count} descs, {set.Controllers.Count} controllers, {set.Rules.Count} rules, atlas {set.TextureAtlas ?? mesh.MorphTexture}" );
			Assert.IsTrue( set.Width > 0 && set.Height > 0, "a size" );
			CollectionAssert.AreEqual( new[] { MorphBundleType.PositionSpeed, MorphBundleType.NormalWrinkle }, set.BundleTypes.ToArray() );
			Assert.IsTrue( set.Morphs.Count > 0 && set.Controllers.Count > 0 && set.Rules.Count > 0, "morphs, controllers and rules" );
			var rects = set.Morphs.SelectMany( m => m.Rects ).ToArray();
			Console.WriteLine( $"  {rects.Length} rectangles, morphs without any: {set.Morphs.Count( m => m.Rects.Length == 0 )}, bundles {string.Join( ",", rects.Select( r => r.Bundles.Length ).Distinct() )}, offsets {string.Join( ",", rects.SelectMany( r => r.Bundles ).Select( b => b.Offsets.Length ).Distinct() )}, ranges {string.Join( ",", rects.SelectMany( r => r.Bundles ).Select( b => b.Ranges.Length ).Distinct() )}" );
			Assert.IsTrue( rects.Length > 0 && rects.All( r => r.Bundles.Length == 2 && r.Bundles.All( b => b.Offsets.Length == 4 && b.Ranges.Length == 4 ) ), "every rectangle has both bundles, scaled per channel" );
			Assert.IsNotNull( files.Find( set.TextureAtlas ?? mesh.MorphTexture ), "the morph texture is built" );

			var weights = new float[set.FlexDescs.Count];
			var controllers = new float[set.Controllers.Count];
			set.RunFlexRules( controllers, weights );
			Assert.IsTrue( weights.All( x => x == 0 ), "nothing set, nothing weighted" );

			var weighted = 0;
			for ( int c = 0; c < controllers.Length; c++ )
			{
				Array.Clear( controllers );
				controllers[c] = set.Controllers[c].Max;
				set.RunFlexRules( controllers, weights );
				if ( weights.Any( x => x > 0 ) ) weighted++;
			}

			Assert.IsTrue( weighted > controllers.Length / 2, $"{weighted} of {controllers.Length} controllers weight a morph" );
		}
	}

	/// <summary>Flex rules evaluate as native's do: fetches, constants, arithmetic, two-way sliders, combos and earlier descs.</summary>
	[TestMethod]
	public void FlexRulesEvaluate()
	{
		static FlexOp Const( float value ) => new( FlexOpCode.Const, BitConverter.SingleToInt32Bits( value ) );
		var set = new MorphSet
		{
			FlexDescs = ["a", "b", "c", "d"],
			Controllers = [new( "x", "default", 0, 1 ), new( "y", "default", -1, 1 )],
			Rules =
			[
				new( 0, [new( FlexOpCode.Fetch1, 0 ), Const( 0.5f ), new( FlexOpCode.Mul, 0 )] ),
				new( 1, [new( FlexOpCode.TwoWay0, 1 ), new( FlexOpCode.Fetch2, 0 ), new( FlexOpCode.Add, 0 )] ),
				new( 2, [new( FlexOpCode.Fetch1, 0 ), new( FlexOpCode.TwoWay1, 1 ), new( FlexOpCode.Combo, 2 )] ),
				new( 3, [Const( 1 ), Const( 0 ), new( FlexOpCode.Div, 0 )] ),
			],
		};

		var weights = new float[4];
		set.RunFlexRules( [0.8f, -0.5f], weights );
		Assert.AreEqual( 0.4f, weights[0], 1e-6f );
		Assert.AreEqual( 0.5f + 0.4f, weights[1], 1e-6f );
		Assert.AreEqual( 0.0f, weights[2], 1e-6f );
		Assert.AreEqual( 0.0f, weights[3], 1e-6f, "division by about 0 is 0" );

		set.RunFlexRules( [0.8f, 0.5f], weights );
		Assert.AreEqual( 0.4f, weights[1], 1e-6f );
		Assert.AreEqual( 0.4f, weights[2], 1e-6f );
	}
}
