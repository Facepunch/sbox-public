using Sandbox;
using Sandbox.SteamApi;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ServicesTests;

[TestClass]
public class WorkshopServerItemsTest
{
	static Workshop.Item Item( ulong id ) => new() { Id = id, Title = $"Item {id}" };

	static Workshop.Item Collection( ulong id, params ulong[] children ) => new()
	{
		Id = id,
		Title = $"Collection {id}",
		FileType = WorkshopFileType.Collection,
		Children = children
	};

	static System.Func<IReadOnlyCollection<ulong>, Task<Workshop.Item[]>> Fetcher( params Workshop.Item[] items )
	{
		var all = items.ToDictionary( x => x.Id );
		return ids => Task.FromResult( ids.Where( all.ContainsKey ).Select( x => all[x] ).ToArray() );
	}

	[TestMethod]
	public void ParsesConfigFile()
	{
		var text = """
			[
				111,
				"222",
				0,
				"not-an-id",
				333,
			]
			""";

		CollectionAssert.AreEqual( new ulong[] { 111, 222, 333 }, Workshop.ParseServerItems( text ) );
	}

	[TestMethod]
	public async Task ExpandsNestedCollectionsInOrderOnce()
	{
		var fetch = Fetcher(
			Collection( 1, 10, 2, 11 ),
			Collection( 2, 20, 10, 1 ), // repeats an item, and links back to its parent
			Item( 10 ), Item( 11 ), Item( 20 ), Item( 30 ) );

		var items = await Workshop.ExpandCollectionsAsync( [1, 30, 10, 99], fetch );

		CollectionAssert.AreEqual( new ulong[] { 10, 20, 11, 30 }, items.Select( x => x.Id ).ToArray() );
	}
}
