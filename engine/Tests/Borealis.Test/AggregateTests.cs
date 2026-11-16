using Borealis;
using Borealis.Features;
using System.Linq;
using static BorealisTests.TestScenes;

namespace BorealisTests;

/// <summary>
/// Aggregates, a map's world geometry: each view culls their fragments on its own, the survivors are one batch, and every
/// fragment's transform is written once a frame where its draw's base instance finds it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class AggregateTests
{
	internal static AggregateObject.FragmentDraw Fragment( int index, Vector3 center, float size = 1, uint indices = 3 ) => new()
	{
		Transform = Matrix3x4.Identity,
		BoundsMin = center - size,
		BoundsMax = center + size,
		DrawTint = Vector3.One,
		Args = new Borealis.GpuBuffer.IndirectDrawIndexedArguments { IndexCount = indices, InstanceCount = 1, FirstIndex = (uint)index * 3, FirstInstance = (uint)index },
	};

	/// <summary>
	/// In front, behind, off to the side, and too small to see from the camera looking down +X.
	/// </summary>
	internal static AggregateObject TestAggregate() => new( TestMesh( false, false, false, false ),
	[
		Fragment( 0, new Vector3( 20, 0, 0 ), indices: 6 ),
		Fragment( 1, new Vector3( -20, 0, 0 ) ),
		Fragment( 2, new Vector3( 20, 500, 0 ) ),
		Fragment( 3, new Vector3( 900, 0, 0 ), size: 0.01f ),
	] );

	[TestMethod]
	public void FragmentsAreCulledOnTheirOwn()
	{
		var world = new RenderWorld();
		var aggregate = TestAggregate();
		world.Add( aggregate );

		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<AggregateRenderFeature>();
		var batch = feature.BatchesFor( system.MainPass ).Single();
		Assert.AreEqual( 1, batch.Count, "only the fragment in front is drawn" );
		Assert.AreEqual( 2, batch.Triangles );

		var args = feature.Args[batch.FirstArg];
		Assert.AreEqual( 0u, args.FirstInstance, "its base instance is its fragment's index" );
		Assert.AreEqual( 6u, args.IndexCount );
	}

	[TestMethod]
	public void EveryFragmentIsWrittenOnceAFrame()
	{
		var world = new RenderWorld();
		var aggregate = TestAggregate();
		aggregate.Tint = new Color( 0.5f, 1, 1 );
		world.Add( aggregate );

		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		// The cascades draw it too, from the same entries
		var feature = system.GetFeature<AggregateRenderFeature>();
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => feature.BatchesFor( p ).Count > 0 ), "a cascade drew it" );

		var transforms = system.Transforms;
		Assert.AreEqual( 4, transforms.Count, "an entry per fragment, culled or not" );
		Assert.AreEqual( 4, transforms.InstanceCount, "ids in fragment order, shared by every view" );
		for ( int i = 0; i < 4; i++ )
			Assert.AreEqual( i, transforms.InstanceSlot( aggregate.FirstInstance + i ) );

		// 127.5 rounds to even
		Assert.AreEqual( 0x80FFFFu, transforms[0].TintRgb888, "the object's tint times the draw's" );
	}

	[TestMethod]
	public void FragmentsThatDoNotCastShadowsAreLeftOutOfShadowMaps()
	{
		var world = new RenderWorld();
		var fragment = Fragment( 0, new Vector3( 20, 0, 0 ) );
		fragment.Flags = FragmentFlags.DoNotCastShadows;
		world.Add( new AggregateObject( TestMesh( false ), [fragment] ) );

		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var feature = system.GetFeature<AggregateRenderFeature>();
		Assert.AreEqual( 1, feature.BatchesFor( system.MainPass ).Count );
		Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => feature.BatchesFor( p ).Count == 0 ) );
	}

	[TestMethod]
	public void MainViewFragmentsAreOcclusionCulled()
	{
		var world = new RenderWorld();
		world.Add( TestAggregate() );

		using var system = new RenderSystem();
		Borealis.Shadows.ShadowMapper.ResetCache();
		CollectAndPrepare( system, world, LookingDownX() );

		var batch = system.GetFeature<AggregateRenderFeature>().BatchesFor( system.MainPass ).Single();
		Assert.IsTrue( batch.CullArg >= 0, "the batch has a cull run" );
		Assert.AreEqual( 1, system.Occlusion.InstanceCount, "an instance per drawn fragment" );
	}

	/// <summary>
	/// On a device that keeps resident aggregates, a view draws every aggregate of the world from one list, the same object
	/// frame after frame, made again only when an aggregate changes how it draws: so the device can keep what it makes of it.
	/// A view whose root hides a tag gets the list of the classes it shows; a shadow view takes static casters, unless its
	/// sun is baked, and those that aren't static where settling doesn't decide.
	/// </summary>
	[TestMethod]
	public void ResidentAggregatesKeepOneListBetweenFrames()
	{
		var device = HeadlessRenderHost.Headless;
		device.ResidentAggregates = true;
		try
		{
			var world = new RenderWorld();
			var plain = TestAggregate();
			var tagged = TestAggregate();
			plain.IsStatic = tagged.IsStatic = true;
			tagged.Tags = [new StringToken( "resident-test" ).Value];
			world.Add( plain );
			world.Add( tagged );

			// No depth prepass, so no occlusion culling, which keeps views preparing their own
			using var system = new RenderSystem { DepthPrepass = false };
			Borealis.Shadows.ShadowMapper.ResetCache();
			var feature = system.GetFeature<AggregateRenderFeature>();
			var view = LookingDownX();

			// The first frame their transforms are frame entries, so the view prepares them; from the next, kept runs
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( 0, feature.ResidentFor( system.MainPass ).Batches.Count, "nothing resident before the aggregates have kept runs" );
			Assert.AreEqual( 2, feature.BatchesFor( system.MainPass ).Count, "the view prepared both" );

			CollectAndPrepare( system, world, view );
			var list = feature.ResidentFor( system.MainPass );
			Assert.AreEqual( 2, list.Batches.Count, "both resident" );
			Assert.AreEqual( 4, list.Batches[0].Count, "every fragment, for the GPU to cull" );
			Assert.AreEqual( 0, feature.BatchesFor( system.MainPass ).Count, "the view prepares none of its own" );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => feature.ResidentFor( p ) is { Batches.Count: 2 } ), "a cascade draws both static casters from its list" );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Where( p => feature.ResidentFor( p ) is { Batches.Count: 2 } ).All( p => p.Visible( feature ).Count == 0 && p.ResidentInView ),
				"its culling leaves them out, noting it has casters in view" );

			var made = feature.ResidentListsMade;
			CollectAndPrepare( system, world, view );
			Assert.AreSame( list, feature.ResidentFor( system.MainPass ), "the same list the next frame" );
			Assert.AreEqual( made, feature.ResidentListsMade, "no list made again" );

			// A view hiding the tag: the other class only
			var hiding = LookingDownX();
			hiding.ExcludeTags = tagged.Tags;
			CollectAndPrepare( system, world, hiding );
			Assert.AreSame( plain, feature.ResidentFor( system.MainPass ).Batches.Single().Aggregate, "the hidden tag's aggregate isn't in its list" );
			Assert.AreEqual( 0, feature.BatchesFor( system.MainPass ).Count, "nor does it prepare it" );

			// A change to how one draws makes them again
			CollectAndPrepare( system, world, view );
			plain.GameLayers = false;
			CollectAndPrepare( system, world, view );
			Assert.AreNotSame( list, feature.ResidentFor( system.MainPass ), "a new list after a change" );
			Assert.AreSame( tagged, feature.ResidentFor( system.MainPass ).Batches.Single().Aggregate, "without the one out of the game's layers" );

			// A baked sun's cascades take no static casters
			world.Lighting.SunShadowsBaked = true;
			Borealis.Shadows.ShadowMapper.ResetCache();
			CollectAndPrepare( system, world, view );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => feature.ResidentFor( p ) is null or { Batches.Count: 0 } ), "a baked sun's cascades take no static casters from their lists" );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => feature.BatchesFor( p ).Count == 0 ), "nor of their own" );

			// One that isn't static is in the lists of a baked sun's cascades that take every such caster
			tagged.IsStatic = false;
			CollectAndPrepare( system, world, view );
			CollectAndPrepare( system, world, view );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().Any( p => p.StaticFilter == StaticFilter.NoStatic && feature.ResidentFor( p ) is { Batches.Count: 1 } list && list.Batches[0].Aggregate == tagged ), "a caster that isn't static is in a baked sun's cascade's list" );
			Assert.IsTrue( system.ShadowMaps.Passes.ToArray().All( p => p.StaticFilter is StaticFilter.NoStatic or StaticFilter.Any || feature.ResidentFor( p ) is null or { Batches.Count: 0 } ), "where settling decides, it's left to the view" );
		}
		finally
		{
			device.ResidentAggregates = false;
		}
	}

	/// <summary>
	/// Once nothing resident changes, each frame repeats the last look: the same instance ids, kept runs and lists, while an
	/// object that isn't resident moves and past the frames a kept slot lasts undrawn. A change to a resident one is looked at.
	/// </summary>
	[TestMethod]
	public void QuietFramesRepeatTheLastLook()
	{
		var device = HeadlessRenderHost.Headless;
		device.ResidentAggregates = true;
		try
		{
			var world = new RenderWorld();
			var aggregate = TestAggregate();
			aggregate.IsStatic = true;
			world.Add( aggregate );
			var mover = new MeshObject( TestMesh( false ), new Transform( new Vector3( 30, 0, 0 ) ) );
			world.Add( mover );

			using var system = new RenderSystem { DepthPrepass = false };
			Borealis.Shadows.ShadowMapper.ResetCache();
			var feature = system.GetFeature<AggregateRenderFeature>();
			var view = LookingDownX();

			// Kept runs from the second frame; the third finds them all kept, and is the look the quiet frames repeat
			CollectAndPrepare( system, world, view );
			CollectAndPrepare( system, world, view );
			CollectAndPrepare( system, world, view );
			var list = feature.ResidentFor( system.MainPass );
			Assert.AreEqual( 1, list.Batches.Count, "resident" );
			var first = aggregate.FirstInstance;
			var slot = system.Transforms.InstanceSlot( first );

			var quiet = feature.QuietFrames;
			var frames = Borealis.Gpu.TransformBuffer.SlotLifetime + 70;
			for ( int i = 0; i < frames; i++ )
			{
				mover.Transform = new Transform( new Vector3( 31 + i, 0, 0 ) );
				CollectAndPrepare( system, world, view );
			}

			Assert.AreEqual( quiet + frames, feature.QuietFrames, "every frame quiet while only an object that isn't resident moves" );
			Assert.AreSame( list, feature.ResidentFor( system.MainPass ), "the same list" );
			Assert.AreEqual( first, aggregate.FirstInstance, "its instance ids at the same place" );
			Assert.AreEqual( slot, system.Transforms.InstanceSlot( aggregate.FirstInstance ), "its kept run kept" );
			Assert.AreEqual( system.Transforms.Generation, aggregate.TransformsGeneration, "its transforms current this frame" );
			Assert.AreEqual( 0, feature.BatchesFor( system.MainPass ).Count, "the view prepares none of its own" );

			// A frame drawn the views' own way gives it other ids: the next frame looks again, and the quiet ones after keep its ids
			device.ResidentAggregates = false;
			CollectAndPrepare( system, world, view );
			device.ResidentAggregates = true;
			quiet = feature.QuietFrames;
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( quiet, feature.QuietFrames, "a look after a frame without one" );
			Assert.AreEqual( slot, system.Transforms.InstanceSlot( aggregate.FirstInstance ), "its ids find its kept run" );
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( quiet + 1, feature.QuietFrames, "then quiet" );
			Assert.AreEqual( slot, system.Transforms.InstanceSlot( aggregate.FirstInstance ), "its ids still find its kept run" );
			frames = feature.QuietFrames - quiet;

			// A change to the resident one is looked at: its kept entries written again, then quiet again
			var writes = system.Transforms.KeptWrites;
			aggregate.Tint = new Color( 1, 0, 0 );
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( quiet + frames, feature.QuietFrames, "not quiet the frame it changed" );
			Assert.AreEqual( writes + aggregate.Fragments.Length, system.Transforms.KeptWrites, "each fragment's entry written again" );
			CollectAndPrepare( system, world, view );
			Assert.AreEqual( quiet + frames + 1, feature.QuietFrames, "quiet again after" );
		}
		finally
		{
			device.ResidentAggregates = false;
		}
	}
}
