namespace Sandbox;

internal static partial class DebugOverlay
{
	public partial class Resources
	{
		static ResourceSystem.ResourceStats _resourceStats;
		static NativeResourceCache.NativeCacheStats _nativeCacheStats;
		static int _lastUpdate = -1;

		static Dictionary<string, int> _maxStrong = new();
		static Dictionary<string, int> _maxWeak = new();
		static Dictionary<string, int> _maxNative = new();

		static void UpdateMax( Dictionary<string, int> maxDict, Dictionary<string, int> current )
		{
			foreach ( var kvp in current )
			{
				if ( kvp.Key == "(dead)" ) continue;
				maxDict.TryGetValue( kvp.Key, out var prev );
				if ( kvp.Value > prev ) maxDict[kvp.Key] = kvp.Value;
			}
		}

		internal static void Draw( ref Vector2 pos )
		{
			// Update stats once per second
			var now = RealTime.Now.FloorToInt();
			if ( _lastUpdate != now )
			{
				_lastUpdate = now;
				_resourceStats = Game.Resources.GetResourceStats();
				_nativeCacheStats = NativeResourceCache.GetStats();
				UpdateMax( _maxStrong, _resourceStats.StrongIndex );
				UpdateMax( _maxWeak, _resourceStats.WeakIndexEntries );
				UpdateMax( _maxNative, _nativeCacheStats.Entries );
			}

			var x = pos.x;
			var y = pos.y;

			var header = new TextRendering.Scope( "", Color.White, 12, "Roboto Mono", 700 );
			header.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

			var scope = new TextRendering.Scope( "", Color.White, 11, "Roboto Mono", 600 );
			scope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

			var dimScope = new TextRendering.Scope( "", Color.White.WithAlpha( 0.5f ), 11, "Roboto Mono", 600 );
			dimScope.Outline = new TextRendering.Outline { Color = Color.Black, Enabled = true, Size = 2 };

			//
			// ResourceIndex (strong refs - GameResources)
			//
			header.Text = $"ResourceIndex (strong): {_resourceStats.StrongTotal}";
			header.TextColor = new Color( 0.6f, 0.9f, 1f );
			Hud.DrawText( header, new Vector2( x, y ), TextFlag.LeftTop );
			y += 16;

			foreach ( var kvp in _resourceStats.StrongIndex.OrderByDescending( x => x.Value ) )
			{
				DrawResourceRow( ref y, x, scope, dimScope, kvp.Key, kvp.Value, _maxStrong, new Color( 0.8f, 0.9f, 1f ) );
			}

			y += 8;

			//
			// WeakIndex (weak refs - runtime/native resources)
			//
			header.Text = $"WeakIndex (weak): {_resourceStats.WeakTotal}";
			header.TextColor = new Color( 0.6f, 1f, 0.7f );
			Hud.DrawText( header, new Vector2( x, y ), TextFlag.LeftTop );
			y += 16;

			foreach ( var kvp in _resourceStats.WeakIndexEntries.OrderByDescending( x => x.Value ) )
			{
				var isDead = kvp.Key == "(dead)";
				var nameColor = isDead ? new Color( 1f, 0.5f, 0.5f ) : new Color( 0.7f, 1f, 0.7f );
				DrawResourceRow( ref y, x, scope, dimScope, kvp.Key, kvp.Value, isDead ? null : _maxWeak, nameColor );
			}

			y += 8;

			//
			// NativeResourceCache
			//
			header.Text = $"NativeResourceCache (by handle): {_nativeCacheStats.WeakTableTotal} weak, {_nativeCacheStats.MemoryCacheCount} cached";
			header.TextColor = new Color( 1f, 0.9f, 0.6f );
			Hud.DrawText( header, new Vector2( x, y ), TextFlag.LeftTop );
			y += 16;

			foreach ( var kvp in _nativeCacheStats.Entries.OrderByDescending( x => x.Value ) )
			{
				var isDead = kvp.Key == "(dead)";
				var nameColor = isDead ? new Color( 1f, 0.5f, 0.5f ) : new Color( 1f, 1f, 0.7f );
				DrawResourceRow( ref y, x, scope, dimScope, kvp.Key, kvp.Value, isDead ? null : _maxNative, nameColor );
			}

			pos.y = y;
		}

		static void DrawResourceRow( ref float y, float x, TextRendering.Scope scope, TextRendering.Scope dimScope, string name, int count, Dictionary<string, int> maxDict, Color nameColor )
		{
			var isDead = name == "(dead)";
			var countColor = isDead ? new Color( 1f, 0.5f, 0.5f ) : Color.White;

			scope.TextColor = countColor;
			scope.Text = count.ToString( "N0" );
			Hud.DrawText( scope, new Rect( x, y, 40, 13 ), TextFlag.RightTop );

			if ( maxDict is not null && maxDict.TryGetValue( name, out var max ) )
			{
				dimScope.TextColor = max > count ? new Color( 1f, 0.7f, 0.3f ) : Color.White.WithAlpha( 0.6f );
				dimScope.Text = $"(max {max:N0})";
				Hud.DrawText( dimScope, new Rect( x + 45, y, 80, 13 ), TextFlag.LeftTop );
			}

			scope.TextColor = nameColor;
			scope.Text = name;
			Hud.DrawText( scope, new Vector2( x + 130, y ), TextFlag.LeftTop );

			y += 14;
		}
	}
}
