using System.Collections.Frozen;

namespace Sandbox
{
	public partial class SceneObject
	{
		/// <summary>
		/// List of tags for this scene object.
		/// </summary>
		public ITagSet Tags { get; private set; }

		/// <summary>The tags as string tokens, in the order they were added (as native's <c>CSceneObject::m_tags</c>).</summary>
		internal IReadOnlyList<uint> TagTokens => ((Internal.SceneObjectTags)Tags).Tokens;
	}

	namespace Internal
	{
		/// <summary>
		/// A scene object's tags, kept in managed code. The first time anything asks they're read from the native object, which
		/// has those native code gave it by then (a map's world geometry); from then on managed code owns them. The native object
		/// still gets every change: render-mesh traces (<c>ISceneWorld::MeshTrace</c>) filter by its tags.
		/// </summary>
		internal class SceneObjectTags : ITagSet
		{
			readonly SceneObject SceneObject;
			readonly List<uint> tokens = new();
			bool read;

			internal SceneObjectTags( SceneObject sceneObject )
			{
				SceneObject = sceneObject;
			}

			/// <summary>The tags as string tokens, in the order they were added.</summary>
			internal List<uint> Tokens
			{
				get
				{
					if ( !read && SceneObject.native.IsValid )
					{
						read = true;
						var count = SceneObject.native.GetTagCount();
						for ( var i = 0; i < count; i++ ) tokens.Add( SceneObject.native.GetTagAt( i ) );
					}

					return tokens;
				}
			}

			public override void Add( string tag )
			{
				if ( !SceneObject.native.IsValid ) return;
				var token = StringToken.FindOrCreate( tag );
				if ( Tokens.Contains( token ) ) return;
				tokens.Add( token );
				SceneObject.native.AddTag( token );
				SceneObject.TagsChanged();
			}

			public override IEnumerable<string> TryGetAll()
			{
				if ( !SceneObject.native.IsValid )
					yield break;

				foreach ( var token in Tokens.ToArray() )
					yield return StringToken.GetValue( token );
			}

			/// <summary>
			/// Try to get all tags in the set.
			/// </summary>
			public override IReadOnlySet<uint> GetTokens()
			{
				return Tokens.ToFrozenSet();
			}

			public override bool Has( string tag )
			{
				if ( !SceneObject.native.IsValid ) return false;
				return Tokens.Contains( StringToken.FindOrCreate( tag ) );
			}

			public override void Remove( string tag )
			{
				if ( !SceneObject.native.IsValid ) return;
				var token = StringToken.FindOrCreate( tag );
				if ( !Tokens.Remove( token ) ) return;
				SceneObject.native.RemoveTag( token );
				SceneObject.TagsChanged();
			}

			public override void RemoveAll()
			{
				if ( !SceneObject.native.IsValid ) return;
				read = true;
				tokens.Clear();
				SceneObject.native.RemoveAllTags();
				SceneObject.TagsChanged();
			}
		}
	}
}
