using Borealis.Backend;
using Sandbox;
using System.Threading;
using System.Threading.Tasks;

namespace BorealisGpuTests;

/// <summary>
/// Shader parameters on Borealis.Gpu (<see cref="BackendParameters"/>), which recording threads read without a lock while
/// they're written.
/// </summary>
[TestClass]
public class ParameterTests
{
	/// <summary>
	/// Readers on other threads, while parameters are cleared, set again and grown over and over, only ever find a value
	/// that was set for the name they asked for, or nothing; and with nothing set here, the parent's.
	/// </summary>
	[TestMethod]
	public void ReadsWhileWrittenFindOnlyWhatWasSet()
	{
		const uint names = 96;
		var parent = new BackendParameters();
		parent.Set( new StringToken( 1000u ), 7 );
		var parameters = new BackendParameters { Parent = parent };

		var stop = 0;
		var wrong = 0;
		var found = 0L;
		var readers = new Task[3];
		for ( int r = 0; r < readers.Length; r++ )
		{
			readers[r] = Task.Run( () =>
			{
				while ( Volatile.Read( ref stop ) == 0 )
				{
					for ( uint name = 0; name < names; name++ )
					{
						if ( parameters.Get( new StringToken( name ) ) is int value )
						{
							if ( (uint)value >> 16 != name ) Interlocked.Increment( ref wrong );
							Interlocked.Increment( ref found );
						}

						if ( parameters.GetCombo( new StringToken( name ) ) is int combo && combo != (int)(name % 7) ) Interlocked.Increment( ref wrong );
					}

					if ( parameters.Get( new StringToken( 1000u ) ) is not 7 ) Interlocked.Increment( ref wrong );
				}
			} );
		}

		for ( int round = 0; round < 3000; round++ )
		{
			parameters.Clear();
			var count = (uint)(round % (int)names) + 1;
			for ( uint name = 0; name < count; name++ )
			{
				parameters.Set( new StringToken( name ), (int)(name << 16 | (uint)round & 0xFFFF) );
				parameters.SetCombo( new StringToken( name ), (int)(name % 7) );
			}
		}

		Volatile.Write( ref stop, 1 );
		Task.WaitAll( readers );

		Assert.AreEqual( 0, wrong, "values read were set for the name read" );
		Assert.IsTrue( found > 0, "readers found values" );
	}

	/// <summary>
	/// Attributes that differ only in their lightmap or light probe volume constants, which GPU-driven passes read per draw,
	/// share a bucket key; ones that differ in anything else don't.
	/// </summary>
	[TestMethod]
	public void BucketKeysLeaveOutWhatIsReadPerDraw()
	{
		static BackendParameters Make( byte baked, byte probe, float tint = 1.0f )
		{
			var parameters = new BackendParameters();
			parameters.Set( new StringToken( "g_vColorTint" ), tint );
			parameters.SetData( new StringToken( "BakedLightingConstantBuffer_t" ), new System.Numerics.Vector4( baked, 0, 0, 0 ) );
			parameters.SetData( new StringToken( "PerBatchLightProbeVolumeConstantBuffer_t" ), new System.Numerics.Vector4( probe, 0, 0, 0 ) );
			return parameters;
		}

		var a = Make( 1, 1 );

		Assert.AreEqual( a.LightingFreeKey, Make( 2, 1 ).LightingFreeKey, "another lightmap" );
		Assert.AreEqual( a.LightingFreeKey, Make( 1, 2 ).LightingFreeKey, "another probe volume" );
		Assert.AreNotEqual( a.LightingFreeKey, Make( 1, 1, 0.5f ).LightingFreeKey, "another tint" );
	}

	/// <summary>
	/// Setting the same value again leaves the parameters' version alone; a value that changes every frame, cleared or not,
	/// reads back as set and, once each name has been set a few times, allocates nothing.
	/// </summary>
	[TestMethod]
	public void ValuesSetEveryFrameAllocateNothing()
	{
		var parameters = new BackendParameters();
		var (time, color, count, view, flag) = (new StringToken( "g_flTime" ), new StringToken( "g_vColor" ), new StringToken( "g_nCount" ), new StringToken( "g_matView" ), new StringToken( "g_bFlag" ));

		void Frame( int frame, bool clear )
		{
			if ( clear ) parameters.Clear();
			parameters.Set( time, frame * 0.5f );
			parameters.Set( color, new Vector4( frame, 1, 2, 3 ) );
			parameters.Set( count, 1000 + frame );
			parameters.Set( view, Matrix.CreateTranslation( new Vector3( frame, 0, 0 ) ) );
			parameters.Set( flag, frame % 2 == 0 );
			parameters.SetCombo( count, frame % 3 );
		}

		Frame( 0, false );
		var version = parameters.Version;
		Frame( 0, false );
		Assert.AreEqual( version, parameters.Version, "the same values again change nothing" );

		for ( int frame = 1; frame < 8; frame++ ) Frame( frame, frame % 2 == 0 );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int frame = 8; frame < 64; frame++ ) Frame( frame, frame % 2 == 0 );
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual( 63 * 0.5f, parameters.Get( time ) );
		Assert.AreEqual( new Vector4( 63, 1, 2, 3 ), parameters.Get( color ) );
		Assert.AreEqual( 1063, parameters.Get( count ) );
		Assert.AreEqual( Matrix.CreateTranslation( new Vector3( 63, 0, 0 ) ), parameters.Get( view ) );
		Assert.AreEqual( false, parameters.Get( flag ) );
		Assert.AreEqual( 0, parameters.GetCombo( count ) );
		Assert.AreEqual( 0, allocated, $"setting changed values every frame allocated {allocated} B" );
	}
}
