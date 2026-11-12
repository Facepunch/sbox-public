using Basalt.Backend;
using Sandbox;
using System.Threading;
using System.Threading.Tasks;

namespace BasaltGpuTests;

/// <summary>
/// Shader parameters on Basalt.Gpu (<see cref="BackendParameters"/>), which recording threads read without a lock while
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
	/// Attributes that differ only in their lightmap constants share a key without lighting (GPU-driven variants read those
	/// per draw) but not a key without the probe volume alone (GPU-driven surfaces keep their lightmap constants bound);
	/// differing only in the probe volume, they share both.
	/// </summary>
	[TestMethod]
	public void BucketKeysLeaveOutWhatIsReadPerDraw()
	{
		BackendParameters Make( byte baked, byte probe )
		{
			var parameters = new BackendParameters();
			parameters.Set( new StringToken( "g_vColorTint" ), 1.0f );
			parameters.SetData( new StringToken( "BakedLightingConstantBuffer_t" ), new System.Numerics.Vector4( baked, 0, 0, 0 ) );
			parameters.SetData( new StringToken( "PerBatchLightProbeVolumeConstantBuffer_t" ), new System.Numerics.Vector4( probe, 0, 0, 0 ) );
			return parameters;
		}

		var a = Make( 1, 1 );
		var otherLightmap = Make( 2, 1 );
		var otherProbe = Make( 1, 2 );

		Assert.AreEqual( a.LightingFreeKey, otherLightmap.LightingFreeKey );
		Assert.AreNotEqual( a.ProbeFreeKey, otherLightmap.ProbeFreeKey );
		Assert.AreEqual( a.LightingFreeKey, otherProbe.LightingFreeKey );
		Assert.AreEqual( a.ProbeFreeKey, otherProbe.ProbeFreeKey );
	}
}
