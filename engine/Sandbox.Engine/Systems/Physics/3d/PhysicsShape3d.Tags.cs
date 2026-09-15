namespace Sandbox;

partial class PhysicsShape3d
{
	internal class TagAccessor : ITagSet
	{
		readonly PhysicsShape3d shape;
		HashSet<string> all = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		internal TagAccessor( PhysicsShape3d shape )
		{
			this.shape = shape;
		}

		public override void Add( string tag )
		{
			if ( all.Add( tag ) )
			{
				shape.native.AddTag( StringToken.FindOrCreate( tag ) );
			}
		}

		public override IEnumerable<string> TryGetAll()
		{
			return all.AsEnumerable();
		}

		public override bool Has( string tag )
		{
			return all.Contains( tag );
		}

		public override void Remove( string tag )
		{
			if ( all.Remove( tag ) )
			{
				shape.native.RemoveTag( StringToken.FindOrCreate( tag ) );
			}
		}

		public override void RemoveAll()
		{
			foreach ( var t in all.ToArray() )
			{
				Remove( t );
			}

			shape.native.ClearTags();
		}
	}
}
