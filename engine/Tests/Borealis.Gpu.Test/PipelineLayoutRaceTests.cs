using Borealis.Gpu;
using System.Threading;
using System.Threading.Tasks;

namespace BorealisGpuTests;

/// <summary>
/// The pipeline layout cache under threads: pipelines made on several threads (the builders, the frame thread) while binding
/// layouts are destroyed on another.
/// </summary>
[TestClass]
public class PipelineLayoutRaceTests : GpuTest
{
	/// <summary>
	/// A binding layout destroyed on another thread between a pipeline layout reading its handle and taking the cache's lock
	/// (here, deterministically, in that gap): no layout is cached for it, so a later binding layout given its driver handle
	/// makes a layout of its own, with its hash.
	/// </summary>
	[TestMethod]
	public void SetDestroyedWhileItsLayoutIsAskedForIsntCached()
	{
		BindingSlot[] slots = [new( 0, BindingType.UniformBuffer, ShaderStages.Compute )];
		var layout = Device.CreateBindingLayout( slots );
		var setLayout = Device.Get( layout ).SetLayout;
		Device.PipelineLayoutSetsRead = () =>
		{
			Device.PipelineLayoutSetsRead = null;
			Task.Run( () => Device.Destroy( layout ) ).Wait();
		};

		Assert.ThrowsException<ArgumentException>( () => Device.PipelineLayout( [layout], graphics: false ), "its set was destroyed" );
		Assert.IsTrue( Device.PipelineLayoutsConsistent, "a layout of the destroyed set was cached" );

		// The set layout destroyed for real; the driver may give the next one its handle, which then must not find a stale layout
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
		Device.WaitIdle();
		for ( int i = 0; i < 64; i++ )
		{
			var again = Device.CreateBindingLayout( slots );
			Device.LayoutHash( Device.PipelineLayout( [again], graphics: false ) );
			if ( Device.Get( again ).SetLayout == setLayout ) break;
		}
	}

	/// <summary>
	/// A pipeline layout asked for while one of its binding layouts is destroyed is never cached without its hash, nor for a set
	/// layout that's gone: a later binding layout the driver gives the same handle would find it, and the part keys of every
	/// pipeline made with it threw (KeyNotFoundException in LayoutHash, on a builder thread, which took the process down).
	/// </summary>
	[TestMethod]
	public void LayoutsAskedForWhileTheirSetsGoStayConsistent()
	{
		BindingSlot[] slots = [new( 0, BindingType.UniformBuffer, ShaderStages.Compute )];
		const int Threads = 4;
		const int Rounds = 400;

		for ( int round = 0; round < Rounds; round++ )
		{
			var layout = Device.CreateBindingLayout( slots );
			using var start = new Barrier( Threads + 1 );
			var makers = new Task[Threads];
			for ( int t = 0; t < Threads; t++ )
			{
				var graphics = (t & 1) == 1;
				makers[t] = Task.Factory.StartNew( () =>
				{
					start.SignalAndWait();
					try
					{
						Device.PipelineLayout( [layout], graphics );
					}
					catch ( ArgumentException )
					{
						// Destroyed before it was read: no layout, as for any destroyed handle
					}
				}, TaskCreationOptions.LongRunning );
			}

			start.SignalAndWait();
			Device.Destroy( layout );
			Task.WaitAll( makers );
			Assert.IsTrue( Device.PipelineLayoutsConsistent, $"round {round}: a layout of a destroyed set was cached" );

			// Its set layout destroyed for real, so the driver can hand its handle out again, as in a running game
			if ( round % 16 == 15 )
			{
				for ( int i = 0; i <= Device.FramesInFlight; i++ ) Device.BeginFrame();
				Device.WaitIdle();
			}
		}

		// Layouts with set handles the driver may have reused all have their hashes
		for ( int i = 0; i < 32; i++ )
		{
			var layout = Device.CreateBindingLayout( slots );
			foreach ( var graphics in new[] { false, true } ) Device.LayoutHash( Device.PipelineLayout( [layout], graphics ) );
		}
	}
}
