using System;
using System.Collections.Generic;

namespace ResourceTests;

[TestClass]
public class GraphicsDrawCountTests
{
	[TestMethod]
	public void VertexCountLargerThanSpanThrows()
	{
		var vertices = new Vertex[3];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( vertices.AsSpan(), 4, null ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( new List<Vertex>( vertices ), 4, null ) );
	}

	[TestMethod]
	public void NegativeVertexCountThrows()
	{
		var vertices = new Vertex[3];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( vertices.AsSpan(), -1, null ) );
	}

	[TestMethod]
	public void IndexCountLargerThanSpanThrows()
	{
		var vertices = new Vertex[3];
		var indices = new ushort[3];
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( vertices.AsSpan(), 3, indices.AsSpan(), 4, null ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( vertices.AsSpan(), 3, indices.AsSpan(), -1, null ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Graphics.Draw( vertices.AsSpan(), 4, indices.AsSpan(), 3, null ) );
	}

	[TestMethod]
	public void CountsWithinSpansAreAccepted()
	{
		var vertices = new Vertex[3];
		var indices = new ushort[3];
		Assert.ThrowsException<ArgumentNullException>( () => Graphics.Draw( vertices.AsSpan(), 3, null ) );
		Assert.ThrowsException<ArgumentNullException>( () => Graphics.Draw( vertices.AsSpan(), 2, indices.AsSpan(), 3, null ) );
	}
}
