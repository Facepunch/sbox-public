using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// What the C# tile assembler's message says.
/// </summary>
public partial class SplitTests
{
	[TestMethod]
	[DataRow( 255 )]
	[DataRow( 1023 )]
	[DataRow( 16383 )]
	public void OnePolygonOverTheLimitIsAnErrorThatGivesTheLimit( int limit )
	{
		var tile = Floors( 0, 0, limit );
		Assert.IsNull( new TileAssembler().Create( 0, 0, tile.Verts, new Poly[limit + 1], Limited( limit ), out var error ) );
		StringAssert.Contains( error, $"the limit is {limit}" );
	}
}
