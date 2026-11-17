using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using Sandbox.Rendering;
using System;

namespace BorealisTests;

/// <summary>
/// What <see cref="BorealisGpuRenderBackend"/> works out on the CPU for draws from engine buffers and models: vertex types'
/// stream layouts, instance transforms, and where a model's indices go.
/// </summary>
[TestClass]
public class GpuGraphicsTests
{
#pragma warning disable CS0649 // laid out, never set
	struct LineVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector3 Tangent;
		[VertexLayout.Color] public Color Color;
		[VertexLayout.TexCoord] public Vector2 TextureCoord;
	}

	struct NumberedVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.TexCoord( 3 )] public uint Id;
		[VertexLayout.TexCoord] public int Group;
		[VertexLayout.TexCoord] public Color32 Tint;
		[VertexLayout.BlendIndices] public byte Bone;
	}
#pragma warning restore CS0649

	/// <summary>
	/// A vertex type's fields in order, packed, by their attributes' semantics; unnumbered ones take the first free index;
	/// the type's size apart, as native's layout of it.
	/// </summary>
	[TestMethod]
	public void VertexTypesLayOutAsNativeDoes()
	{
		var line = BorealisGpuRenderBackend.LayoutOf( typeof( LineVertex ) );
		Assert.AreEqual( 60, line.Stride );
		CollectionAssert.AreEqual( new[]
		{
			new VertexField( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
			new VertexField( VertexSemantic.Normal, 0, VertexFormat.Float3, 12 ),
			new VertexField( VertexSemantic.Tangent, 0, VertexFormat.Float3, 24 ),
			new VertexField( VertexSemantic.Color, 0, VertexFormat.Float4, 36 ),
			new VertexField( VertexSemantic.TexCoord, 0, VertexFormat.Float2, 52 ),
		}, line.Fields.ToArray() );

		var numbered = BorealisGpuRenderBackend.LayoutOf( typeof( NumberedVertex ) );
		CollectionAssert.AreEqual( new[]
		{
			new VertexField( VertexSemantic.Position, 0, VertexFormat.Float3, 0 ),
			new VertexField( VertexSemantic.TexCoord, 3, VertexFormat.UInt, 12 ),
			new VertexField( VertexSemantic.TexCoord, 0, VertexFormat.Int, 16 ),
			new VertexField( VertexSemantic.TexCoord, 1, VertexFormat.UByte4Norm, 20 ),
			new VertexField( VertexSemantic.BlendIndices, 0, VertexFormat.UByte, 24 ),
		}, numbered.Fields.ToArray() );

		Assert.AreSame( line, BorealisGpuRenderBackend.LayoutOf( typeof( LineVertex ) ), "one layout object per type, which pipelines are cached by" );
		Assert.AreSame( VertexStreamLayout.EngineVertex, BorealisGpuRenderBackend.LayoutOf( typeof( Vertex ) ) );
	}

	/// <summary>An instance's transform entry matches <see cref="GpuInstanceTransform.From"/>, which goes through matrices.</summary>
	[TestMethod]
	public void InstanceTransformsMatchTheMatrixPath()
	{
		var random = new Random( 7 );
		float R( float range ) => (float)(random.NextDouble() * 2 - 1) * range;
		for ( int i = 0; i < 64; i++ )
		{
			var transform = new Transform( new Vector3( R( 500 ), R( 500 ), R( 500 ) ), Rotation.From( R( 180 ), R( 180 ), R( 180 ) ), new Vector3( 0.1f + MathF.Abs( R( 3 ) ), 0.1f + MathF.Abs( R( 3 ) ), 0.1f + MathF.Abs( R( 3 ) ) ) );
			var expected = GpuInstanceTransform.From( transform );
			var actual = BorealisGpuRenderBackend.InstanceTransform( transform );
			foreach ( var (e, a, row) in new[] { (expected.Row0, actual.Row0, 0), (expected.Row1, actual.Row1, 1), (expected.Row2, actual.Row2, 2) } )
			{
				float[] want = [e.x, e.y, e.z, e.w], got = [a.x, a.y, a.z, a.w];
				for ( int c = 0; c < 4; c++ )
					Assert.AreEqual( want[c], got[c], 1e-3f * MathF.Max( 1, MathF.Abs( want[c] ) ), $"transform {i} row {row} column {c}" );
			}

			Assert.AreEqual( expected.Alpha, actual.Alpha );
			Assert.AreEqual( expected.Tint, actual.Tint );
		}
	}

	/// <summary>
	/// A model's draws sit in its index buffer at native's first index or after the draw before, so binding each from the
	/// difference keeps native's first index: draws sharing one index buffer keep their places, draws each starting at 0 pack.
	/// </summary>
	[TestMethod]
	public void ModelIndicesKeepNativesFirstIndex()
	{
		var end = 0;
		Assert.AreEqual( 0, BorealisGpuRenderBackend.PlaceIndices( ref end, 0, 30 ) );
		Assert.AreEqual( 30, BorealisGpuRenderBackend.PlaceIndices( ref end, 30, 12 ) );
		Assert.AreEqual( 60, BorealisGpuRenderBackend.PlaceIndices( ref end, 60, 6 ) );
		Assert.AreEqual( 66, BorealisGpuRenderBackend.PlaceIndices( ref end, 0, 6 ) );
		Assert.AreEqual( 72, end );
	}
}
