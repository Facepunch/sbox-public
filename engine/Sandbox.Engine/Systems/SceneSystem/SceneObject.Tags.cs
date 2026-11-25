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
		/// has those native code gave it by then (a map's world geometry); from then on managed code owns them. A native object,
		/// if native code made it, still gets every change.
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
					if ( !read && SceneObject.HasNativeObject )
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
				if ( !SceneObject.IsValid() ) return;
				var token = StringToken.FindOrCreate( tag );
				if ( Tokens.Contains( token ) ) return;
				tokens.Add( token );
				if ( SceneObject.HasNativeObject ) SceneObject.native.AddTag( token );
				SceneObject.TagsChanged();
			}

			public override IEnumerable<string> TryGetAll()
			{
				if ( !SceneObject.IsValid() )
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

			public override void SetFrom( ITagSet set )
			{
				// Renderers copy their GameObject's tags every frame and they rarely change, so compare the cached
				// tokens first instead of walking the tag strings
				if ( set is GameTags or TagSet or TokenBasedTagSet && SceneObject.IsValid() && Matches( set.GetTokens() ) )
					return;

				base.SetFrom( set );
			}

			// The managed tokens are the tags (a native object only mirrors them); they hold no duplicates
			bool Matches( IReadOnlySet<uint> other )
			{
				var own = Tokens;
				if ( own.Count != other.Count ) return false;

				for ( var i = 0; i < own.Count; i++ )
				{
					if ( !other.Contains( own[i] ) ) return false;
				}

				return true;
			}

			public override bool Has( string tag )
			{
				if ( !SceneObject.IsValid() ) return false;
				return Tokens.Contains( StringToken.FindOrCreate( tag ) );
			}

			public override void Remove( string tag )
			{
				if ( !SceneObject.IsValid() ) return;
				var token = StringToken.FindOrCreate( tag );
				if ( !Tokens.Remove( token ) ) return;
				if ( SceneObject.HasNativeObject ) SceneObject.native.RemoveTag( token );
				SceneObject.TagsChanged();
			}

			public override void RemoveAll()
			{
				if ( !SceneObject.IsValid() ) return;
				read = true;
				tokens.Clear();
				if ( SceneObject.HasNativeObject ) SceneObject.native.RemoveAllTags();
				SceneObject.TagsChanged();
			}
		}
	}
}
