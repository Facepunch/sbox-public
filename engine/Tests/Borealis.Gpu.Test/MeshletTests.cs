using Borealis.Backend;
using Borealis.Backend.Meshlets;
using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;
using V3 = System.Numerics.Vector3;

namespace BorealisGpuTests;

/// <summary>
/// Meshlets built at load (docs/reference/borealis-gpu-driven.md, "Meshlets"): meshoptimizer's shared library from
/// <c>src/meshopt</c>, and what <see cref="MeshletBuilder"/> makes of a range.
/// </summary>
[TestClass]
public class MeshletTests
{
	static void UseEngineLibrary()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault();
		if ( core is not null ) MeshOptimizer.Directory = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !MeshOptimizer.Available ) Assert.Inconclusive( "meshopt isn't built" );
	}

	/// <summary>A grid of quads, two triangles each, its vertices from <paramref name="first"/>.</summary>
	static (uint[] Indices, V3[] Positions) Grid( int side, uint first )
	{
		var positions = new V3[(side + 1) * (side + 1)];
		for ( int y = 0; y <= side; y++ )
			for ( int x = 0; x <= side; x++ ) positions[y * (side + 1) + x] = new V3( x, y, (x * 7 + y * 3) % 5 * 0.1f );

		var indices = new List<uint>();
		for ( int y = 0; y < side; y++ )
		{
			for ( int x = 0; x < side; x++ )
			{
				var a = first + (uint)(y * (side + 1) + x);
				var b = a + 1;
				var c = a + (uint)(side + 1);
				var d = c + 1;
				indices.AddRange( [a, b, c, b, d, c] );
			}
		}

		return (indices.ToArray(), positions);
	}

	/// <summary>
	/// Every triangle lands in exactly one meshlet with its corners in their order (its first vertex is what the visibility
	/// ids name), no meshlet is over the config, runs tile the range, and each box holds its triangles' vertices.
	/// </summary>
	[TestMethod]
	public void MeshletsKeepEveryTriangleAndItsFirstVertex()
	{
		UseEngineLibrary();
		const uint First = 1000;
		const int BaseVertex = 5;
		var (indices, positions) = Grid( 40, First );
		V3 Position( int vertex ) => positions[vertex - BaseVertex - (int)First];

		foreach ( var config in new[] { MeshletConfig.Default, new MeshletConfig( 32, 32 ) } )
		{
			var set = MeshletBuilder.Build( indices, Position, BaseVertex, config );
			Assert.IsNotNull( set );
			Assert.AreEqual( indices.Length, set.Indices.Length );

			var expected = new Dictionary<(uint, uint, uint), int>();
			for ( int t = 0; t < indices.Length / 3; t++ ) expected[(indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2])] = expected.GetValueOrDefault( (indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]) ) + 1;
			for ( int t = 0; t < set.Triangles; t++ )
			{
				var key = (set.Indices[t * 3], set.Indices[t * 3 + 1], set.Indices[t * 3 + 2]);
				Assert.IsTrue( expected.TryGetValue( key, out var left ) && left > 0, $"triangle {key} isn't one of the range's, corners in order" );
				expected[key] = left - 1;
			}

			var next = 0u;
			foreach ( var meshlet in set.Meshlets )
			{
				Assert.AreEqual( next, meshlet.FirstTriangle );
				Assert.IsTrue( meshlet.TriangleCount is > 0 && meshlet.TriangleCount <= (uint)config.MaxTriangles );
				var vertices = new HashSet<uint>();
				for ( uint t = meshlet.FirstTriangle; t < meshlet.FirstTriangle + meshlet.TriangleCount; t++ )
				{
					for ( int k = 0; k < 3; k++ )
					{
						var index = set.Indices[t * 3 + k];
						vertices.Add( index );
						var p = Position( BaseVertex + (int)index );
						var d = V3.Abs( p - meshlet.Center ) - meshlet.Extents;
						Assert.IsTrue( d.X <= 1e-4f && d.Y <= 1e-4f && d.Z <= 1e-4f, $"vertex {index} is outside its meshlet's box" );
					}
				}

				Assert.IsTrue( vertices.Count <= config.MaxVertices );
				next += meshlet.TriangleCount;
			}

			Assert.AreEqual( (uint)set.Triangles, next );
		}
	}

	/// <summary>
	/// A meshlet's local data, for the mesh shaders: its vertex list holds each of its distinct index values once, in the
	/// order its triangles first use them, and each triangle's local corners name its own corners in their order.
	/// </summary>
	[TestMethod]
	public void LocalListsNameEachTrianglesCorners()
	{
		UseEngineLibrary();
		const uint First = 300;
		var (indices, positions) = Grid( 40, First );
		V3 Position( int vertex ) => positions[vertex - (int)First];

		foreach ( var config in new[] { MeshletConfig.Default, new MeshletConfig( 32, 32 ) } )
		{
			var set = MeshletBuilder.Build( indices, Position, 0, config );
			var local = set.Local();
			Assert.AreEqual( set.Triangles, local.Triangles.Length );
			var next = 0;
			for ( int m = 0; m < set.Meshlets.Length; m++ )
			{
				var meshlet = set.Meshlets[m];
				Assert.AreEqual( next, local.FirstVertex[m] );
				var list = local.Vertices.AsSpan( local.FirstVertex[m], local.VertexCount[m] ).ToArray();
				Assert.IsTrue( list.Length <= config.MaxVertices );

				var order = new List<uint>();
				for ( uint t = meshlet.FirstTriangle; t < meshlet.FirstTriangle + meshlet.TriangleCount; t++ )
				{
					for ( int c = 0; c < 3; c++ )
					{
						var index = set.Indices[t * 3 + c];
						if ( !order.Contains( index ) ) order.Add( index );
						var corner = (int)((local.Triangles[t] >> (c * 8)) & 0xff);
						Assert.IsTrue( corner < list.Length );
						Assert.AreEqual( index, list[corner], $"triangle {t} corner {c}" );
					}
				}

				CollectionAssert.AreEqual( order, list );
				next += list.Length;
			}

			Assert.AreEqual( next, local.Vertices.Length );
		}
	}

	/// <summary>
	/// A range's first corners as compaction reads them from its corner run: the lowest and how many values to the highest,
	/// the corner table's own, every triangle's first corner among them and both ends taken.
	/// </summary>
	[TestMethod]
	public void FirstCornersSpanEveryTrianglesFirstCorner()
	{
		UseEngineLibrary();
		const uint First = 700;
		var (indices, positions) = Grid( 40, First );
		V3 Position( int vertex ) => positions[vertex - (int)First];

		var set = MeshletBuilder.Build( indices, Position, 0, MeshletConfig.Default );
		var (low, span) = set.FirstCorners();
		var corners = set.Corners();
		Assert.AreEqual( corners.Low, low );
		Assert.AreEqual( corners.Span, span );

		uint lowest = uint.MaxValue, highest = 0;
		for ( int t = 0; t < set.Triangles; t++ )
		{
			var first = set.Indices[t * 3];
			Assert.IsTrue( first >= low && first - low < span, $"triangle {t}'s first corner {first} is in [{low}, {low + span})" );
			lowest = Math.Min( lowest, first );
			highest = Math.Max( highest, first );
		}

		Assert.AreEqual( lowest, low );
		Assert.AreEqual( highest, low + span - 1 );
	}

	/// <summary>
	/// The first-corner table (normals from the ids): each index value's run lists exactly the triangles whose first corner
	/// it is, in meshlet order, and the runs tile the triangles.
	/// </summary>
	[TestMethod]
	public void CornerTablesListEachTriangleUnderItsFirstCorner()
	{
		UseEngineLibrary();
		const uint First = 700;
		var (indices, positions) = Grid( 40, First );
		V3 Position( int vertex ) => positions[vertex - (int)First];

		var set = MeshletBuilder.Build( indices, Position, 0, MeshletConfig.Default );
		var corners = set.Corners();
		var span = (int)corners.Span;
		Assert.AreEqual( span + 1 + set.Triangles, corners.Words.Length );
		Assert.AreEqual( 0u, corners.Words[0] );
		Assert.AreEqual( (uint)set.Triangles, corners.Words[span] );

		var seen = new bool[set.Triangles];
		for ( int v = 0; v < span; v++ )
		{
			Assert.IsTrue( corners.Words[v] <= corners.Words[v + 1] );
			var last = -1;
			for ( var k = corners.Words[v]; k < corners.Words[v + 1]; k++ )
			{
				var t = (int)corners.Words[span + 1 + k];
				Assert.AreEqual( corners.Low + (uint)v, set.Indices[t * 3], $"triangle {t} is under {corners.Low + v}" );
				Assert.IsTrue( t > last, "a run's triangles are in meshlet order" );
				Assert.IsFalse( seen[t] );
				seen[t] = true;
				last = t;
			}
		}

		Assert.IsTrue( seen.All( s => s ), "every triangle is under its first corner" );
	}

	/// <summary>meshoptimizer is built without its asserts: a call it would assert on throws here instead of reaching it.</summary>
	[TestMethod]
	public void BadInputsThrowBeforeTheLibrary()
	{
		UseEngineLibrary();
		var (indices, positions) = Grid( 2, 0 );
		V3 Position( int vertex ) => positions[vertex];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => MeshletBuilder.Build( indices, Position, 0, new MeshletConfig( 64, 126 ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => MeshletBuilder.Build( indices, Position, 0, new MeshletConfig( 300, 124 ) ) );
		Assert.ThrowsException<ArgumentException>( () => MeshletBuilder.Build( indices.AsSpan( 0, 4 ), Position, 0, MeshletConfig.Default ) );
	}

	/// <summary>
	/// A mesh cooked as the resource compiler cooks an aggregate's meshes (<see cref="Borealis.Source2.Models.MeshletCooker"/>)
	/// holds, per draw, the indices and records <see cref="MeshletBuilder"/> builds from it as compiled, which the store takes
	/// as they are (<see cref="MeshletSet.FromCooked"/>); its file reads back the same; a block of another version or config
	/// is left for the load to build.
	/// </summary>
	[TestMethod]
	public void CookedMeshletsAreTheLoadTimeBuild()
	{
		UseEngineLibrary();
		var content = ContentFiles.FindGame();
		if ( content.TryRead( "models/citizen_props/trashcan01.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/citizen_props/trashcan01.vmdl isn't built" ); return; }

		var config = MeshletConfig.Default;
		var cut = MeshletBuilder.Cut( config );
		var lodConfig = Borealis.Backend.Lods.LodConfig.Default;
		var levelCut = Borealis.Backend.Lods.LodStore.Cut( lodConfig );
		var levelCount = 0;
		var file = Borealis.Source2.Models.ModelFile.Read( data, content.TryRead );
		var ranges = 0;
		foreach ( var mesh in file.Meshes )
		{
			var cooked = Borealis.Source2.Models.MeshletCooker.Cook( mesh, config.MaxVertices, config.MaxTriangles, cut, Borealis.Backend.Lods.LodStore.Cooked( lodConfig ), levelCut );
			Assert.IsNotNull( cooked, $"{mesh.Name} cooks" );
			foreach ( var draw in mesh.DrawCalls )
			{
				var (vbIndex, vbOffset) = draw.VertexBuffers[0];
				var (ibIndex, ibOffset) = draw.IndexBuffer;
				var vb = mesh.VertexBuffers[vbIndex];
				var position = vb.Find( "POSITION" ).Value;
				var start = ibOffset / mesh.IndexBuffers[ibIndex].Size + draw.StartIndex;
				var baseVertex = vbOffset / vb.Stride + draw.BaseVertex;
				var original = Enumerable.Range( start, draw.IndexCount ).Select( k => mesh.IndexBuffers[ibIndex][k] ).ToArray();
				var expected = MeshletBuilder.Build( original, v => { var p = vb.Read( position, v ); return new V3( p.X, p.Y, p.Z ); }, baseVertex, config );

				var stored = Enumerable.Range( start, draw.IndexCount ).Select( k => cooked.IndexBuffers[ibIndex][k] ).ToArray();
				CollectionAssert.AreEqual( expected.Indices, stored, "the range's indices are stored in the order the load would put them in" );
				var records = cooked.IndexBuffers[ibIndex].Meshlets.Find( start, draw.IndexCount, cooked.VertexBuffers[vbIndex], baseVertex );
				CollectionAssert.AreEqual( System.Runtime.InteropServices.MemoryMarshal.AsBytes( expected.Meshlets.AsSpan() ).ToArray(), records.ToArray(), "and its records are the load's" );
				var set = MeshletSet.FromCooked( stored, records.Span, config );
				Assert.IsTrue( set is { InPlace: true } && set.Meshlets.Length == expected.Meshlets.Length, "which the store takes as they are" );
				var (cookedCorners, builtCorners) = (set.Corners(), expected.Corners());
				Assert.IsTrue( cookedCorners.Low == builtCorners.Low && cookedCorners.Span == builtCorners.Span, "with the load's first-corner table" );
				CollectionAssert.AreEqual( builtCorners.Words, cookedCorners.Words, "word for word" );
				Assert.IsNull( MeshletSet.FromCooked( stored, records.Span, new MeshletConfig( 64, 4 ) ), "but not with meshlets over its config" );
				Assert.IsTrue( mesh.IndexBuffers[ibIndex].Meshlets is null, "the mesh as compiled has none" );
				ranges++;

				// Its levels: what a load makes of it as compiled, as stored (meshlet order) and with every triangle rotated (as
				// the index buffer's encoding stores them), each with meshlets over all of it
				var levels = cooked.IndexBuffers[ibIndex].Meshlets.FindLevels( start, draw.IndexCount, cooked.VertexBuffers[vbIndex], baseVertex );
				Assert.IsNotNull( levels, "the range's levels were made" );
				var rotated = new uint[original.Length];
				for ( int t = 0; t < original.Length; t += 3 ) (rotated[t], rotated[t + 1], rotated[t + 2]) = (original[t + 1], original[t + 2], original[t]);
				foreach ( var from in (uint[][])[original, stored, rotated] )
				{
					var (madeLevels, madeErrors) = levelCut( from, vb, position, baseVertex ).Value;
					CollectionAssert.AreEqual( madeErrors, levels.Errors.ToArray(), "each level's error is the load's" );
					Assert.AreEqual( madeLevels.Length, levels.Buffers.Count, "and so is their count" );
					for ( int k = 0; k < madeLevels.Length; k++ )
						CollectionAssert.AreEqual( Triangles( madeLevels[k] ), Triangles( System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( levels.Buffers[k].Data ).ToArray() ), "and so are its triangles (stored in meshlet order)" );
				}

				for ( int k = 0; k < levels.Buffers.Count; k++ )
				{
					var level = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( levels.Buffers[k].Data ).ToArray();
					var own = levels.Buffers[k].Meshlets.Find( 0, level.Length, cooked.VertexBuffers[vbIndex], baseVertex );
					Assert.IsTrue( MeshletSet.FromCooked( level, own.Span, config ) is { InPlace: true } levelSet && levelSet.Corners() is { } levelCorners && levelCorners.Words.Length == levelCorners.Span + 1 + levelSet.Triangles, "each level's meshlets cover it, taken as they are, with a first-corner table" );
				}

				levelCount += levels.Buffers.Count;
			}
		}

		Assert.IsTrue( ranges > 0, "a range was cooked" );
		Assert.IsTrue( levelCount > 0, "with levels" );

		// A compiled mesh file cooked: read back, its ranges are the same; a block of another version reads as none
		var vmesh = File.ReadAllBytes( Path.Combine( content.Roots.First(), "models", "dev", "error.vmesh_c" ) );
		if ( Borealis.Source2.Models.MeshletCooker.CookMesh( vmesh, config.MaxVertices, config.MaxTriangles, cut ) is not { } cookedFile ) { Assert.Inconclusive( "models/dev/error.vmesh_c has nothing to cook" ); return; }
		var resource = Borealis.Source2.Resources.ResourceFile.Read( cookedFile );
		Assert.IsTrue( resource.Has( "MSHL" ), "the cooked file has the block" );
		var read = Borealis.Source2.Models.ModelMesh.Read( resource.Block( "DATA" ), resource.Block( "VBIB" ), "error", 0, ulong.MaxValue, 0xFF, false, null, null, resource.Block( "MSHL" ) );
		var inMemory = Borealis.Source2.Models.MeshletCooker.Cook( Borealis.Source2.Models.ModelMesh.Read( Borealis.Source2.Resources.ResourceFile.Read( vmesh ).Block( "DATA" ), Borealis.Source2.Resources.ResourceFile.Read( vmesh ).Block( "VBIB" ), "error", 0, ulong.MaxValue, 0xFF, false, null ), config.MaxVertices, config.MaxTriangles, cut );
		for ( int b = 0; b < read.IndexBuffers.Count; b++ )
		{
			CollectionAssert.AreEqual( inMemory.IndexBuffers[b].Data, read.IndexBuffers[b].Data, "the file's indices are the cooked mesh's" );
			Assert.AreEqual( inMemory.IndexBuffers[b].Meshlets?.Count ?? 0, read.IndexBuffers[b].Meshlets?.Count ?? 0, "and so are its ranges" );
		}

		var block = resource.Block( "MSHL" ).ToArray();

		// The same block as version 1 wrote it (32-byte header and ranges, no levels): its ranges read, with no levels
		var rangeCount = BitConverter.ToInt32( block, 16 );
		var recordCount = BitConverter.ToInt32( block, 24 );
		var recordsAt = BitConverter.ToInt32( block, 28 );
		var first = new byte[32 + rangeCount * 32 + recordCount * 32];
		block.AsSpan( 0, 32 ).CopyTo( first );
		BitConverter.TryWriteBytes( first.AsSpan( 4 ), 1u );
		BitConverter.TryWriteBytes( first.AsSpan( 12 ), 1u );
		BitConverter.TryWriteBytes( first.AsSpan( 20 ), 32 );
		BitConverter.TryWriteBytes( first.AsSpan( 28 ), 32 + rangeCount * 32 );
		for ( int i = 0; i < rangeCount; i++ ) block.AsSpan( 80 + i * 48, 28 ).CopyTo( first.AsSpan( 32 + i * 32 ) );
		block.AsSpan( recordsAt, recordCount * 32 ).CopyTo( first.AsSpan( 32 + rangeCount * 32 ) );
		var version1 = Borealis.Source2.Models.CookedMeshlets.Read( first, read.VertexBuffers.ToArray(), read.IndexBuffers.ToArray() );
		Assert.IsNotNull( version1, "a version 1 block reads" );
		for ( int b = 0; b < read.IndexBuffers.Count; b++ )
		{
			Assert.AreEqual( read.IndexBuffers[b].Meshlets?.Count ?? 0, version1[b]?.Count ?? 0, "with the same ranges" );
			Assert.IsNull( version1[b]?.Lods, "and no levels" );
		}

		block[4] = 3;
		Assert.IsNull( Borealis.Source2.Models.CookedMeshlets.Read( block, read.VertexBuffers.ToArray(), read.IndexBuffers.ToArray() ), "another version is left to the load" );
	}

	/// <summary>A range's triangles sorted, each as stored, to compare ranges in other orders.</summary>
	static (uint, uint, uint)[] Triangles( uint[] indices )
	{
		var triangles = new (uint, uint, uint)[indices.Length / 3];
		for ( int t = 0; t < triangles.Length; t++ ) triangles[t] = (indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]);
		Array.Sort( triangles );
		return triangles;
	}

	[TestMethod]
	public void ConfigsParse()
	{
		Assert.AreEqual( new MeshletConfig( 32, 32 ), MeshletConfig.Parse( "32x32" ) );
		Assert.AreEqual( MeshletConfig.Default, MeshletConfig.Parse( null ) );
		Assert.AreEqual( MeshletConfig.Default, MeshletConfig.Parse( "64x125" ) );
	}

	/// <summary>
	/// borealis_meshlet_ids's module has task, mesh and pixel stages and no vertex stage, and each compiles with Slang: the
	/// task stage 64 threads a group, the mesh stage 128 (a thread per triangle of a 124-triangle meshlet); and the
	/// compute modules beside it compile.
	/// </summary>
	[TestMethod]
	public void MeshletIdsModuleCompiles()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "borealis_meshlet_ids.slang" ) ) );
		if ( core is null ) Assert.Inconclusive( "No borealis_meshlet_ids.slang" );
		var shaders = Path.Combine( core, "shaders" );
		var module = PassModule.Read( Path.Combine( shaders, "borealis_meshlet_ids.slang" ), [shaders] );
		Assert.IsTrue( module.Has( StageKind.Task ), "task stage" );
		Assert.IsTrue( module.Has( StageKind.Mesh ), "mesh stage" );
		Assert.IsTrue( module.Has( StageKind.Pixel ), "pixel stage" );
		Assert.IsFalse( module.Has( StageKind.Vertex ), "no vertex stage" );

		var compiler = new SlangCompiler( EngineFolders.NativeIn( Path.GetDirectoryName( core ) ), [shaders] );
		var none = new Dictionary<string, int>();
		Assert.AreEqual( (64, 1, 1), compiler.CompilePass( module, StageKind.Task, none ).ThreadGroupSize );
		Assert.AreEqual( (128, 1, 1), compiler.CompilePass( module, StageKind.Mesh, none ).ThreadGroupSize );
		Assert.IsTrue( compiler.CompilePass( module, StageKind.Pixel, none ).Stage.Spirv.Length > 0 );

		// The culls that fill its task runs, and the compactions that read its kept lists, as well
		foreach ( var name in new[] { "borealis_meshlet_cull_cs", "borealis_gpu_driven_cull_cs", "borealis_resident_cull_cs", "borealis_gpu_driven_compact_cs" } )
			Assert.IsTrue( compiler.CompilePass( PassModule.Read( Path.Combine( shaders, name + ".slang" ), [shaders] ), StageKind.Compute, none ).Stage.Spirv.Length > 0, name );
	}
}
