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
