using Sandbox.Rendering;

namespace SceneTests.Components;

/// <summary>
/// Sprite sorting against a real scene and a real engine.
///
/// The unit tests cover the arithmetic - key packing, draw runs, index math. What they cannot
/// reach is the thing the arithmetic depends on: which batch a sprite is actually put in. Sprites
/// in different batches have no defined order however their layers are set, because nothing orders
/// one scene object against another. So "did these two sprites end up in the same batch" is the
/// question that decides whether sort layers work at all, and it needs a live scene to answer.
/// </summary>
[TestClass]
public class SpriteSortingTests
{
	static SpriteRenderer CreateSprite( Scene scene, bool sorted = true, bool additive = false, bool opaque = false, bool shadows = false )
	{
		var go = scene.CreateObject();
		var sprite = go.Components.Create<SpriteRenderer>();

		sprite.IsSorted = sorted;
		sprite.Additive = additive;
		sprite.Opaque = opaque;
		sprite.Shadows = shadows;

		return sprite;
	}

	/// <summary>
	/// The batch a sprite would be put in.
	///
	/// Asking the system to actually build its batches is not possible here - a batch allocates GPU
	/// buffers in its constructor and these tests run without a graphics device. But the batch a
	/// sprite lands in is decided entirely by this key, so comparing keys answers the same question
	/// without needing one.
	/// </summary>
	static ulong BatchKey( SpriteRenderer sprite )
		=> SceneSpriteSystem.GetRenderGroupKey( sprite, sprite.Tags as GameTags, sprite.RenderOptions, allowBlendMerge: true );

	/// <summary>
	/// The fix this whole change exists for. An additive sprite and an ordinary one used to be put
	/// in separate scene objects, and no sort layer could order them against each other. They have
	/// to share a batch now.
	/// </summary>
	[TestMethod]
	public void AdditiveAndTransparentSpritesShareOneBatch()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var transparent = CreateSprite( scene, additive: false );
		var additive = CreateSprite( scene, additive: true );

		Assert.AreEqual( BatchKey( transparent ), BatchKey( additive ),
			"additive and transparent sprites must batch together, or their sort layers cannot be honoured" );
	}

	/// <summary>
	/// Opaque sprites belong to a different render pass, so folding them in would change when they
	/// draw. They stay separate on purpose.
	/// </summary>
	[TestMethod]
	public void OpaqueSpritesStayInTheirOwnBatch()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		Assert.AreNotEqual( BatchKey( CreateSprite( scene, opaque: false ) ), BatchKey( CreateSprite( scene, opaque: true ) ) );
	}

	/// <summary>
	/// A shadow caster carries a scene object flag that additive sprites never set, so it cannot be
	/// merged without silently changing whether it casts a shadow.
	/// </summary>
	[TestMethod]
	public void ShadowCastingSpritesStayInTheirOwnBatch()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		Assert.AreNotEqual( BatchKey( CreateSprite( scene, shadows: false ) ), BatchKey( CreateSprite( scene, shadows: true ) ) );
	}

	/// <summary>
	/// Unsorted sprites opted out of ordering entirely, so there is nothing to gain by merging them
	/// and the old batching is left exactly as it was.
	/// </summary>
	[TestMethod]
	public void UnsortedSpritesAreNotMerged()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var plain = CreateSprite( scene, sorted: false, additive: false );
		var additive = CreateSprite( scene, sorted: false, additive: true );

		Assert.AreNotEqual( BatchKey( plain ), BatchKey( additive ) );
	}

	/// <summary>
	/// Sprites differing only in sort layer must stay together - splitting by layer would put each
	/// layer in a scene object whose draw order against the others is undefined, which is worse
	/// than not having layers at all.
	/// </summary>
	[TestMethod]
	public void SortLayerDoesNotSplitTheBatch()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var settings = ProjectSettings.Sorting;
		settings.AddLayer( "Foreground" );

		var back = CreateSprite( scene );
		var front = CreateSprite( scene );
		front.SortLayer = new SortLayerHandle( settings.Layers[^1] );

		Assert.AreEqual( BatchKey( back ), BatchKey( front ) );
		Assert.AreNotEqual( back.SortLayer.Index, front.SortLayer.Index );
	}

	// ---- sorting groups, against a real hierarchy --------------------------------------------

	/// <summary>
	/// Authored sort orders can be any integers with gaps. The draw order only has room for a small
	/// dense rank, so they have to be compressed while keeping their relative order.
	/// </summary>
	[TestMethod]
	public void GroupRanksCompressArbitraryOrders()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var root = scene.CreateObject();
		var group = root.Components.Create<SortingGroup>();

		var legs = CreateSprite( scene );
		var body = CreateSprite( scene );
		var head = CreateSprite( scene );

		legs.GameObject.SetParent( root );
		body.GameObject.SetParent( root );
		head.GameObject.SetParent( root );

		legs.SortOrder = -500;
		body.SortOrder = 0;
		head.SortOrder = 9000;

		group.RefreshRanks();

		Assert.AreEqual( 0, group.GetRank( legs ) );
		Assert.AreEqual( 1, group.GetRank( body ) );
		Assert.AreEqual( 2, group.GetRank( head ) );
	}

	/// <summary>
	/// A grouped sprite takes its place in the world from the group, and keeps its own order only
	/// for ranking against the group's other members.
	/// </summary>
	[TestMethod]
	public void GroupedSpriteResolvesToTheGroupsPlaceInTheOrder()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var settings = ProjectSettings.Sorting;
		settings.AddLayer( "Characters" );

		var root = scene.CreateObject();
		root.WorldPosition = new Vector3( 100, 200, 300 );

		var group = root.Components.Create<SortingGroup>();
		group.SortLayer = new SortLayerHandle( settings.Layers[^1] );
		group.SortOrder = 42;

		var sprite = CreateSprite( scene );
		sprite.GameObject.SetParent( root );
		sprite.GameObject.WorldPosition = new Vector3( 0, 0, 0 );
		sprite.SortOrder = 7;

		group.RefreshRanks();

		var (layer, order, origin, rank) = sprite.ResolveSorting( group );

		Assert.AreEqual( group.SortLayer.Id, layer.Id, "the group decides the layer" );
		Assert.AreEqual( 42, order, "the group decides the order against the rest of the world" );
		Assert.AreEqual( root.WorldPosition, origin, "every member sorts at the group's origin" );
		Assert.AreEqual( 0, rank, "the sprite's own order only ranks it inside the group" );
	}

	/// <summary>
	/// Groups do not nest. A sprite belongs to the nearest one above it, and the outer group never
	/// sees it - reported rather than silently flattened.
	/// </summary>
	[TestMethod]
	public void NestedGroupsResolveToTheNearestOne()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var outer = scene.CreateObject();
		var outerGroup = outer.Components.Create<SortingGroup>();

		var inner = scene.CreateObject();
		inner.SetParent( outer );
		var innerGroup = inner.Components.Create<SortingGroup>();

		var sprite = CreateSprite( scene );
		sprite.GameObject.SetParent( inner );

		Assert.AreEqual( innerGroup, SortingGroup.FindFor( sprite ) );
		Assert.IsTrue( innerGroup.IsNested, "the inner group has to know it is nested so it can say so" );
		Assert.IsFalse( outerGroup.IsNested );

		// The outer group must not claim the sprite, or it would be ranked in two places at once.
		outerGroup.RefreshRanks();
		Assert.AreEqual( 0, outerGroup.GetRank( sprite ) );
	}

	/// <summary>
	/// An ungrouped sprite keeps its own layer, order and position.
	/// </summary>
	[TestMethod]
	public void UngroupedSpriteKeepsItsOwnSorting()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		var sprite = CreateSprite( scene );
		sprite.GameObject.WorldPosition = new Vector3( 10, 20, 30 );
		sprite.SortOrder = 5;

		Assert.IsNull( sprite.SortingGroup );

		var (layer, order, origin, rank) = sprite.ResolveSorting( null );

		Assert.AreEqual( sprite.SortLayer.Id, layer.Id );
		Assert.AreEqual( 5, order );
		Assert.AreEqual( sprite.WorldPosition, origin );
		Assert.AreEqual( 0, rank );
	}

	// ---- the layer list is mutable at runtime, and the id lookup must keep up ----------------

	/// <summary>
	/// A layer added through the editor has to be usable straight away. Adding it to the list
	/// without rebuilding the id lookup does not throw - the new layer simply resolves to the
	/// default, so sprites assigned to it quietly render in the wrong place.
	/// </summary>
	[TestMethod]
	public void NewlyAddedLayerResolvesImmediately()
	{
		var settings = new SortingSettings();
		var added = settings.AddLayer( "Foreground" );

		Assert.AreEqual( 1, settings.GetLayerIndex( added.Id ) );
		Assert.AreEqual( added, settings.GetLayer( added.Id ) );
	}

	/// <summary>
	/// Reordering is the whole point of the layer editor, and it changes what every id maps to. A
	/// stale lookup here would draw every sprite in the scene in the wrong layer.
	/// </summary>
	[TestMethod]
	public void ReorderingLayersUpdatesEveryIndex()
	{
		var settings = new SortingSettings();
		var first = settings.AddLayer( "First" );
		var second = settings.AddLayer( "Second" );

		Assert.AreEqual( 1, settings.GetLayerIndex( first.Id ) );
		Assert.AreEqual( 2, settings.GetLayerIndex( second.Id ) );

		settings.MoveLayer( 2, 1 );

		Assert.AreEqual( 1, settings.GetLayerIndex( second.Id ), "the moved layer has to report its new position" );
		Assert.AreEqual( 2, settings.GetLayerIndex( first.Id ), "and the one it displaced has to report its new one" );
	}

	/// <summary>
	/// Deleting a layer shifts everything after it, and orphaned sprites must land on the default
	/// rather than on whichever layer happens to have inherited the index.
	/// </summary>
	[TestMethod]
	public void DeletingALayerReindexesAndOrphansFallBack()
	{
		var settings = new SortingSettings();
		var doomed = settings.AddLayer( "Doomed" );
		var after = settings.AddLayer( "After" );

		Assert.IsTrue( settings.RemoveLayer( doomed ) );

		Assert.AreEqual( 1, settings.GetLayerIndex( after.Id ), "later layers move down" );
		Assert.AreEqual( 0, settings.GetLayerIndex( doomed.Id ), "the deleted layer falls back to the default" );
		Assert.IsNull( settings.GetLayer( doomed.Id ) );
	}

	/// <summary>
	/// Everything falls back to the first layer, so it cannot be removed.
	/// </summary>
	[TestMethod]
	public void TheLastLayerCannotBeRemoved()
	{
		var settings = new SortingSettings();

		Assert.IsFalse( settings.RemoveLayer( settings.DefaultLayer ) );
		Assert.AreEqual( 1, settings.Layers.Count );
	}
}
