using Sandbox.UI;
using System.Collections;

namespace Sandbox;

internal partial class UISystem
{
	readonly List<RootPanel> _screenInputRoots = new();

	/// <summary>
	/// Active screen roots, highest z-index first. Equal z-indices keep root order, like OrderByDescending.
	/// The list is reused, so it's only valid until the next call.
	/// </summary>
	internal List<RootPanel> GetScreenInputRoots()
	{
		var roots = _screenInputRoots;
		roots.Clear();

		foreach ( var root in GetActiveRoots() )
		{
			if ( root.IsWorldPanel ) continue;

			var z = root.ComputedStyle?.ZIndex ?? 0;
			int i = roots.Count;
			while ( i > 0 && (roots[i - 1].ComputedStyle?.ZIndex ?? 0) < z ) i--;
			roots.Insert( i, root );
		}

		return roots;
	}

	/// <summary>
	/// Direct foreach iteration doesn't allocate. Keep the live index traversal so root callbacks can
	/// modify the list, and check activity as each root is visited rather than taking a snapshot.
	/// </summary>
	internal readonly struct ActiveRootEnumerable( UISystem system, bool reverse ) : IEnumerable<RootPanel>
	{
		public Enumerator GetEnumerator() => new( system, reverse );
		IEnumerator<RootPanel> IEnumerable<RootPanel>.GetEnumerator() => GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		public RootPanel FirstOrDefault()
		{
			foreach ( var root in this ) return root;
			return null;
		}

		public struct Enumerator( UISystem system, bool reverse ) : IEnumerator<RootPanel>
		{
			int _index;
			bool _started;
			bool _finished;

			public RootPanel Current { get; private set; }
			object IEnumerator.Current => Current;

			public bool MoveNext()
			{
				if ( _finished ) return false;

				var step = reverse ? -1 : 1;
				if ( !_started )
				{
					_index = reverse ? system.RootPanels.Count - 1 : 0;
					_started = true;
				}
				else
				{
					_index += step;
				}

				for ( ; _index >= 0 && _index < system.RootPanels.Count; _index += step )
				{
					var root = system.RootPanels[_index];
					if ( root is not { IsActive: true } ) continue;

					Current = root;
					return true;
				}

				_finished = true;
				Current = null;
				return false;
			}

			public void Dispose() => _finished = true;
			public void Reset() => throw new NotSupportedException();
		}
	}
}
