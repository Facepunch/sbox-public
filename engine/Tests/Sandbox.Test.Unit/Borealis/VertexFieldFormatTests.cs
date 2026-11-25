using Borealis.Gpu;
using Borealis.Source2.Models;

namespace BorealisTests;

/// <summary>
/// Vertex fields a compiled mesh lays out as Borealis.Gpu reads them: a run-time mesh's packed integers (Rubble's voxels,
/// one <c>uint3</c> at TEXCOORD10) included.
/// </summary>
[TestClass]
public class VertexFieldFormatTests
{
	[TestMethod]
	public void ThreeComponentUintsAreRead()
	{
		var field = new VertexField( "TEXCOORD", 10, ColorFormat.R32G32B32_UINT, 0, 0, false, 0 );
		Assert.AreEqual( VertexFormat.UInt3, field.VertexFormat );
		Assert.IsTrue( VertexFormat.UInt3.IsInteger(), "read by a shader's uint input" );
	}

	[TestMethod]
	public void IntegerFormatsAreIntegers()
	{
		foreach ( var format in new[] { VertexFormat.UByte4, VertexFormat.UInt, VertexFormat.UInt2, VertexFormat.UInt3, VertexFormat.UInt4, VertexFormat.Int, VertexFormat.UByte, VertexFormat.Byte } )
			Assert.IsTrue( format.IsInteger(), format.ToString() );
		foreach ( var format in new[] { VertexFormat.Float, VertexFormat.Float3, VertexFormat.Half4, VertexFormat.UByte4Norm, VertexFormat.UInt1010102Norm, VertexFormat.UShort2Norm } )
			Assert.IsFalse( format.IsInteger(), format.ToString() );
	}
}
