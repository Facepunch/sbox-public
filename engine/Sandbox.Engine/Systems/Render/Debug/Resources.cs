namespace Sandbox;

internal static partial class DebugOverlay
{
	/// <summary>
	/// What the resource indexes and the native resource cache hold, with the most each type has reached, for the
	/// performance hub's Memory tab.
	/// </summary>
	public partial class Resources
	{
		static int _lastUpdate = -1;

		internal static ResourceSystem.ResourceStats Stats { get; private set; }
		internal static NativeResourceCache.NativeCacheStats NativeStats { get; private set; }

		internal static readonly Dictionary<string, int> MaxStrong = new();
		internal static readonly Dictionary<string, int> MaxWeak = new();
		internal static readonly Dictionary<string, int> MaxNative = new();

		static void UpdateMax( Dictionary<string, int> maxDict, Dictionary<string, int> current )
		{
			foreach ( var kvp in current )
			{
				if ( kvp.Key == "(dead)" ) continue;
				maxDict.TryGetValue( kvp.Key, out var prev );
				if ( kvp.Value > prev ) maxDict[kvp.Key] = kvp.Value;
			}
		}

		/// <summary>
		/// Refresh the stats, at most once a second.
		/// </summary>
		internal static void Update()
		{
			var now = RealTime.Now.FloorToInt();
			if ( _lastUpdate == now ) return;

			_lastUpdate = now;
			Stats = Game.Resources.GetResourceStats();
			NativeStats = NativeResourceCache.GetStats();
			UpdateMax( MaxStrong, Stats.StrongIndex );
			UpdateMax( MaxWeak, Stats.WeakIndexEntries );
			UpdateMax( MaxNative, NativeStats.Entries );
		}
	}
}
