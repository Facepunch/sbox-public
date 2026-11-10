namespace Sandbox
{
	internal class TokenBasedTagSet : ITagSet
	{
		private HashSet<uint> Tags { get; set; } = new();

		public override void Add( string tag )
		{
			Tags.Add( StringToken.FindOrCreate( tag ) );
		}

		public override IEnumerable<string> TryGetAll()
		{
			foreach ( var k in Tags )
			{
				if ( StringToken.TryLookup( k, out var tag ) )
					yield return tag;
			}
		}

		/// <summary>
		/// Try to get all tags in the set.
		/// </summary>
		public override IReadOnlySet<uint> GetTokens()
		{
			return Tags;
		}

		public override void SetFrom( ITagSet set )
		{
			// Cameras copy their tags in every frame and they rarely change, so skip the string
			// comparisons when we already hold exactly the source's tokens
			if ( set is TagSet && set.GetTokens() is HashSet<uint> tokens && Tags.SetEquals( tokens ) )
				return;

			if ( set is TokenBasedTagSet other && Tags.SetEquals( other.Tags ) )
				return;

			base.SetFrom( set );
		}

		public override bool Has( string tag )
		{
			return Tags.Contains( StringToken.FindOrCreate( tag ) );
		}

		public override void Remove( string tag )
		{
			Tags.Remove( StringToken.FindOrCreate( tag ) );
		}

		public override void RemoveAll()
		{
			Tags.Clear();
		}
	}
}
