using System.Collections.Frozen;

namespace Sandbox
{
	public partial class SceneObject
	{
		/// <summary>
		/// List of tags for this scene object.
		/// </summary>
		public ITagSet Tags { get; private set; }
	}

	namespace Internal
	{
		internal class SceneObjectTags : ITagSet
		{
			readonly SceneObject SceneObject;

			internal SceneObjectTags( SceneObject sceneObject )
			{
				SceneObject = sceneObject;
			}

			public override void Add( string tag )
			{
				if ( !SceneObject.native.IsValid ) return;
				SceneObject.native.AddTag( StringToken.FindOrCreate( tag ) );
				SceneObject.NotifyChanged( Rendering.SceneObjectChange.Tags );
			}

			public override IEnumerable<string> TryGetAll()
			{
				if ( !SceneObject.native.IsValid )
					yield break;

				var count = SceneObject.native.GetTagCount();

				for ( var i = 0; i < count; i++ )
				{
					var tag = SceneObject.native.GetTagAt( i );
					yield return StringToken.GetValue( tag );
				}
			}

			/// <summary>
			/// Try to get all tags in the set.
			/// </summary>
			public override IReadOnlySet<uint> GetTokens()
			{
				var count = SceneObject.native.GetTagCount();
				return Enumerable.Range( 0, count ).Select( SceneObject.native.GetTagAt ).ToFrozenSet();
			}

			public override void SetFrom( ITagSet set )
			{
				// Renderers copy their GameObject's tags every frame and they rarely change, so compare the cached
				// tokens first instead of walking the tag strings
				if ( set is GameTags or TagSet or TokenBasedTagSet && SceneObject.native.IsValid && Matches( set.GetTokens() ) )
					return;

				base.SetFrom( set );
			}

			bool Matches( IReadOnlySet<uint> tokens )
			{
				var count = SceneObject.native.GetTagCount();
				if ( count != tokens.Count ) return false;

				for ( var i = 0; i < count; i++ )
				{
					if ( !tokens.Contains( SceneObject.native.GetTagAt( i ) ) ) return false;
				}

				return true;
			}

			public override bool Has( string tag )
			{
				if ( !SceneObject.native.IsValid ) return false;
				return SceneObject.native.HasTag( StringToken.FindOrCreate( tag ) );
			}

			public override void Remove( string tag )
			{
				if ( !SceneObject.native.IsValid ) return;
				SceneObject.native.RemoveTag( StringToken.FindOrCreate( tag ) );
				SceneObject.NotifyChanged( Rendering.SceneObjectChange.Tags );
			}

			public override void RemoveAll()
			{
				if ( !SceneObject.native.IsValid ) return;
				SceneObject.native.RemoveAllTags();
				SceneObject.NotifyChanged( Rendering.SceneObjectChange.Tags );
			}
		}
	}
}
