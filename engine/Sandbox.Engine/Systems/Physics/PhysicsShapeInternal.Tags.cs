namespace Sandbox;

using System.Collections.Frozen;

partial class PhysicsShapeInternal
{
	internal class ManagedTagAccessor : ITagSet
	{
		HashSet<string> all = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		// Rebuilt on the main thread so physics filtering can read it from a worker thread.
		volatile IReadOnlySet<uint> tokens = FrozenSet<uint>.Empty;

		public override void Add( string tag )
		{
			if ( all.Add( tag ) ) RebuildTokens();
		}

		public override IEnumerable<string> TryGetAll() => all.AsEnumerable();
		public override bool Has( string tag ) => all.Contains( tag );

		public override void Remove( string tag )
		{
			if ( all.Remove( tag ) ) RebuildTokens();
		}

		public override void RemoveAll()
		{
			if ( all.Count == 0 ) return;

			all.Clear();
			RebuildTokens();
		}

		public override IReadOnlySet<uint> GetTokens() => tokens;

		void RebuildTokens() => tokens = all.Select( x => (uint)StringToken.FindOrCreate( x ) ).ToFrozenSet();
	}
}
