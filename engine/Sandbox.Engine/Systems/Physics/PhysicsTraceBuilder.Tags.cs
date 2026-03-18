namespace Sandbox
{
	public partial struct PhysicsTraceBuilder
	{
		internal unsafe PhysicsTraceBuilder WithOptionalTag( StringToken tag )
		{
			for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				if ( request.TagAny[i] == tag.Value ) return this;

				if ( request.TagAny[i] == 0 )
				{
					request.TagAny[i] = tag.Value;
					return this;
				}
			}

			return this;
		}

		/// <summary>
		/// Only return entities with this tag. Subsequent calls to this will add multiple requirements
		/// and they'll all have to be met (ie, the entity will need all tags).
		/// </summary>
		public unsafe PhysicsTraceBuilder WithTag( StringToken ident )
		{
			for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				if ( request.TagRequire[i] == ident.Value ) return this;

				if ( request.TagRequire[i] == 0 )
				{
					request.TagRequire[i] = ident.Value;
					return this;
				}
			}

			return this;
		}

		/// <summary>
		/// Only return with all of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithAllTags( params string[] tags )
		{
			var t = this;

			foreach ( var tag in tags )
			{
				t = t.WithTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Only return with all of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithAllTags( ITagSet tags )
		{
			if ( tags is null ) return this;

			var t = this;

			foreach ( var tag in tags.TryGetAll() )
			{
				t = t.WithTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Only return entities with any of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithAnyTags( params string[] tags )
		{
			var t = this;

			foreach ( var tag in tags )
			{
				t = t.WithOptionalTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Only return entities with any of these tags (using ints)
		/// </summary>
		internal unsafe PhysicsTraceBuilder WithAnyTags( IReadOnlySet<uint> tags )
		{
			var t = this;

			// fast path, can just copy over
			if ( request.TagAny[0] == 0 )
			{
				var c = Math.Min( PhysicsTrace.Request.NumTagFields, tags.Count );

				int i = 0;
				foreach ( var tag in tags.Take( PhysicsTrace.Request.NumTagFields ) )
				{
					t.request.TagAny[i++] = tag;
				}

				return t;
			}

			foreach ( var te in tags )
			{
				for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
				{
					if ( t.request.TagAny[i] == te ) return this;

					if ( t.request.TagAny[i] == 0 )
					{
						t.request.TagAny[i] = te;
					}
				}
			}

			return t;
		}

		/// <summary>
		/// Only return with any of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithAnyTags( ITagSet tags )
		{
			if ( tags is null ) return this;

			var t = this;

			foreach ( var tag in tags.TryGetAll() )
			{
				t = t.WithOptionalTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Only return without this tag
		/// </summary>
		public unsafe PhysicsTraceBuilder WithoutTag( StringToken tag )
		{
			for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
			{
				if ( request.TagExclude[i] == tag.Value ) return this;

				if ( request.TagExclude[i] == 0 )
				{
					request.TagExclude[i] = tag.Value;
					return this;
				}
			}

			return this;
		}

		/// <summary>
		/// Only return without any of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithoutTags( params string[] tags )
		{
			var t = this;

			foreach ( var tag in tags )
			{
				t = t.WithoutTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Only return without any of these tags
		/// </summary>
		public unsafe PhysicsTraceBuilder WithoutTags( ITagSet tags )
		{
			if ( tags is null ) return this;

			var t = this;

			foreach ( var tag in tags.TryGetAll() )
			{
				t = t.WithoutTag( tag );
			}

			return t;
		}

		/// <summary>
		/// Use the collision rules of the given tag.
		/// </summary>
		/// <param name="tag">Which tag this trace will adopt the collision rules of.</param>
		/// <param name="asTrigger">If true, trace against triggers only. Otherwise, trace for collisions (default).</param>
		public readonly PhysicsTraceBuilder WithCollisionRules( string tag, bool asTrigger = false )
		{
			var t = this;
			var maxHitResult = asTrigger ? CollisionRules.Result.Trigger : CollisionRules.Result.Collide;
			StringToken tagToken = tag;

			foreach ( var other in targetWorld.CollisionRules.RuntimeTags )
			{
				var result = targetWorld.CollisionRules.GetCollisionRule( other, tagToken );
				t = result <= maxHitResult ? t.WithOptionalTag( other ) : t.WithoutTag( other );
			}

			if ( asTrigger )
			{
				t.HitTriggers();
			}

			return t;
		}

		/// <summary>
		/// Use the collision rules for the given set of tags.
		/// </summary>
		/// <param name="tags">Which tags this trace will adopt the collision rules of.</param>
		/// <param name="asTrigger">If true, trace against triggers only. Otherwise, trace for collisions (default).</param>
		public readonly PhysicsTraceBuilder WithCollisionRules( IEnumerable<string> tags, bool asTrigger = false )
		{
			var t = this;
			var maxHitResult = asTrigger ? CollisionRules.Result.Trigger : CollisionRules.Result.Collide;
			var collisionRules = targetWorld.CollisionRules;

			foreach ( var other in collisionRules.RuntimeTags )
			{
				var result = CollisionRules.Result.Collide;
				foreach ( var tag in tags )
				{
					var r = collisionRules.GetCollisionRule( other, tag );
					if ( r > result ) result = r;
				}
				t = result <= maxHitResult ? t.WithOptionalTag( other ) : t.WithoutTag( other );
			}

			if ( asTrigger )
			{
				t.HitTriggers();
			}

			return t;
		}
	}
}
