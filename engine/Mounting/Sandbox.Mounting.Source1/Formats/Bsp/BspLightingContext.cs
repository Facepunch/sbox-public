using System;

namespace Sandbox.Mounting.Source1;

internal sealed class BspLightingContext
{
	internal readonly Dictionary<Vector3, PropLightingState> Props = [];
	internal readonly Dictionary<int, byte[]> Pvs = [];
	private int[] brushVisits = [];
	private int traceGeneration;

	internal int BeginTrace( int brushCount )
	{
		if ( brushVisits.Length != brushCount )
		{
			brushVisits = new int[brushCount];
			traceGeneration = 0;
		}
		if ( traceGeneration == int.MaxValue )
		{
			Array.Clear( brushVisits );
			traceGeneration = 0;
		}
		return ++traceGeneration;
	}

	internal bool VisitBrush( int index, int generation )
	{
		if ( brushVisits[index] == generation )
			return false;
		brushVisits[index] = generation;
		return true;
	}
}
