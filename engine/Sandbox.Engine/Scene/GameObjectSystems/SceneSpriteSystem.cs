namespace Sandbox;

using Sandbox.Rendering;
using System.Buffers;
using System.Collections.Concurrent;

public sealed class SceneSpriteSystem : GameObjectSystem<SceneSpriteSystem>
{
	private readonly record struct SystemOffset( IBatchedParticleSpriteRenderer System, int Offset, int ParticleCount );

	Dictionary<RenderGroupKey, SpriteBatchSceneObject> RenderGroups = [];

	public SceneSpriteSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 1, UpdateSprites, "UpdateSprites" ); // We want to upload after particles update
	}

	public override void Dispose()
	{
		if ( _sharedSprites != null )
		{
			ArrayPool<SpriteBatchSceneObject.SpriteData>.Shared.Return( _sharedSprites );
			_sharedSprites = null;
		}
		base.Dispose();
	}

	private readonly ConcurrentBag<Guid> _activeParticleEmitters = new();
	private readonly ConcurrentBag<(Guid id, RenderGroupKey group, int offset, int count, int splotCount)> _particleProcessingResults = new();
	private HashSet<Guid> _registeredSpriteRenderers = new();
	private SpriteBatchSceneObject.SpriteData[] _sharedSprites;

	internal unsafe void UpdateParticleSprites()
	{
		var spriteRenderers = Scene.GetAllComponents<IBatchedParticleSpriteRenderer>();

		// Clear
		while ( _activeParticleEmitters.TryTake( out _ ) ) { }
		while ( _particleProcessingResults.TryTake( out _ ) ) { }

		// Calculate total size needed and ensure shared block is large enough
		int totalParticles = 0;
		foreach ( var renderer in spriteRenderers )
		{
			var particleRenderer = (ParticleRenderer)renderer;
			totalParticles += particleRenderer.ParticleEffect.Particles.Count;
		}

		// Clean up if there are no particles
		if ( totalParticles == 0 )
		{
			foreach ( var rg in RenderGroups )
			{
				var keysToRemove = rg.Value.SpriteGroups.Keys.ToList();
				foreach ( var key in keysToRemove )
				{
					rg.Value.SpriteGroups.Remove( key );
				}
			}
			return;
		}

		// Here we allocate one big chunk of memory for all particle systems, each writting at a separate offset
		if ( _sharedSprites == null || _sharedSprites.Length < totalParticles )
		{
			if ( _sharedSprites != null ) ArrayPool<SpriteBatchSceneObject.SpriteData>.Shared.Return( _sharedSprites );
			_sharedSprites = ArrayPool<SpriteBatchSceneObject.SpriteData>.Shared.Rent( totalParticles );
		}

		// Calculate write offsets for each particle system	
		var systemOffsets = new SystemOffset[spriteRenderers.Count()];
		int currentOffset = 0;
		int systemIndex = 0;

		foreach ( var particleSystem in spriteRenderers )
		{
			var particleRenderer = (ParticleRenderer)particleSystem;
			int particleCount = particleRenderer.ParticleEffect.Particles.Count;
			systemOffsets[systemIndex] = new( particleSystem, currentOffset, particleCount );
			currentOffset += particleCount;
			systemIndex++;
			if ( particleSystem is ParticleSpriteRenderer psr )
			{
				psr.AdvanceFrame();
			}
		}

		// Parallel processing to write simulated particles to the data block that will be copied to the GPU
		// Process all batched particle renderers
		Parallel.ForEach( systemOffsets, systemInfo =>
		{
			if ( systemInfo.ParticleCount == 0 ) return;

			var particleRenderer = (ParticleRenderer)systemInfo.System;
			var tags = particleRenderer.Tags;

			var particleSystemID = particleRenderer.Id;
			_activeParticleEmitters.Add( particleSystemID );

			RenderGroupKey rendergroup = GetRenderGroupKey( systemInfo.System, (GameTags)particleRenderer.Tags, particleRenderer.RenderOptions );

			// This is a very hot codepath, beware!
			// Create span from the managed array starting at the correct offset with the correct length
			var destSpan = _sharedSprites.AsSpan( systemInfo.Offset, systemInfo.ParticleCount );

			// Call the common ProcessParticlesDirectly interface method
			var result = systemInfo.System.ProcessParticlesDirectly( destSpan );

			if ( result.SpriteCount == 0 ) return;

			_particleProcessingResults.Add( (particleSystemID, rendergroup, systemInfo.Offset, result.SpriteCount, result.SplotCount) );
		} );

		// Cleanup inactive particle emitters
		var activeIds = new HashSet<Guid>( _activeParticleEmitters );
		foreach ( var rg in RenderGroups )
		{
			var keysToRemove = rg.Value.SpriteGroups.Keys.Where( id => !activeIds.Contains( id ) ).ToList();
			foreach ( var key in keysToRemove )
			{
				rg.Value.SpriteGroups.Remove( key );
			}
		}

		// Register buffers to corresponding render groups
		foreach ( var (id, rendergroup, offset, count, splotCount) in _particleProcessingResults )
		{
			foreach ( var rg in RenderGroups )
			{
				rg.Value.SpriteGroups.Remove( id );
			}

			// Create render group if needed
			if ( !RenderGroups.ContainsKey( rendergroup ) )
			{
				CreateRenderGroup( rendergroup );
			}

			// Register in correct render group using shared block with offset and precomputed splot count
			RenderGroups[rendergroup].RegisterSprite( id, _sharedSprites, offset, count, splotCount );
		}

		// Final cleanup for systems that no longer exist
		foreach ( var rg in RenderGroups )
		{
			var keysToRemove = rg.Value.SpriteGroups.Keys.Where( id => !activeIds.Contains( id ) ).ToList();
			foreach ( var key in keysToRemove )
			{
				rg.Value.UnregisterSpriteGroup( key );
			}
		}
	}

	internal void UpdateSpriteRenderers()
	{
		if ( Application.IsHeadless )
			return;

		var allSprites = Scene.GetAllComponents<SpriteRenderer>();
		var currentEnabledSprites = new HashSet<Guid>( allSprites.Count() );

		foreach ( var sprite in allSprites )
		{
			if ( sprite.Enabled && sprite.GameObject.Active )
			{
				// This is used to clean up inactive sprites
				currentEnabledSprites.Add( sprite.Id );

				if ( _registeredSpriteRenderers.Contains( sprite.Id ) )
				{
					UpdateSprite( sprite.Id, sprite );
				}
				else
				{
					RegisterSprite( sprite.Id, sprite );
					_registeredSpriteRenderers.Add( sprite.Id );
				}
			}
		}

		// Animate all sprites in parallel
		Parallel.ForEach( allSprites, sprite =>
		{
			lock ( sprite )
			{
				sprite.AdvanceFrame();
			}
		} );

		// Registered sprites who are not enabled
		var spritesToRemove = _registeredSpriteRenderers.Except( currentEnabledSprites ).ToArray();
		foreach ( var spriteId in spritesToRemove )
		{
			UnregisterSprite( spriteId );
			_registeredSpriteRenderers.Remove( spriteId );
		}
	}

	private void UpdateSprites()
	{
		if ( Application.IsHeadless )
			return;

		UpdateSpriteRenderers();
		UpdateParticleSprites();

		foreach ( var rg in RenderGroups )
		{
			rg.Value.UploadOnHost();
		}
	}

	private static RenderGroupKey GetRenderGroupKey( ISpriteRenderGroup component, GameTags tags, RenderOptions renderOptions )
	{
		var flags = InstanceGroupFlags.None;

		// Non-opaque and sorted needs transparency
		if ( !component.Opaque && component.IsSorted )
		{
			flags |= InstanceGroupFlags.Transparent;
		}

		// Shadows
		if ( component.Shadows && !component.Additive )
		{
			flags |= InstanceGroupFlags.CastShadow;
		}

		if ( component.Additive )
		{
			flags |= InstanceGroupFlags.Additive;
		}

		if ( component.Opaque )
		{
			flags |= InstanceGroupFlags.Opaque;
		}

		RenderGroupKey renderGroupKey = new()
		{
			GroupFlags = flags,
			RenderLayer = renderOptions.Clone(),
			Tags = [.. tags.TryGetAll()]
		};
		return renderGroupKey;
	}

	/// <summary>
	/// Find the component's current render group - might be outdated if component has changed.
	/// Returns null if not present in any
	/// </summary>
	private RenderGroupKey? FindCurrentRenderGroup( Guid componentId )
	{
		foreach ( var rg in RenderGroups )
		{
			if ( rg.Value.ContainsSprite( componentId ) )
			{
				return rg.Key;
			}
		}

		return null;
	}

	private bool IsPresentInRenderGroup( Guid componentId, RenderGroupKey renderGroup )
	{
		return RenderGroups[renderGroup].ContainsSprite( componentId );
	}

	private void InsertInRenderGroup( Guid componentId, SpriteRenderer component, RenderGroupKey renderGroup )
	{
		Assert.True( RenderGroups.ContainsKey( renderGroup ) );
		RenderGroups[renderGroup].RegisterSprite( componentId, component );
	}

	private void RemoveFromRenderGroup( Guid componentId, RenderGroupKey renderGroup )
	{
		Assert.True( IsPresentInRenderGroup( componentId, renderGroup ) );
		RenderGroups[renderGroup].UnregisterSprite( componentId );
	}

	private SpriteBatchSceneObject CreateRenderGroup( RenderGroupKey renderGroupKey )
	{
		var renderGroupObject = new SpriteBatchSceneObject( Scene );
		renderGroupObject.Flags.CastShadows = (renderGroupKey.GroupFlags & InstanceGroupFlags.CastShadow) != 0;
		renderGroupObject.Flags.ExcludeGameLayer = (renderGroupKey.GroupFlags & InstanceGroupFlags.CastOnlyShadow) != 0;
		renderGroupObject.Sorted = (renderGroupKey.GroupFlags & InstanceGroupFlags.Transparent) != 0;
		renderGroupObject.Additive = (renderGroupKey.GroupFlags & InstanceGroupFlags.Additive) != 0;
		renderGroupObject.Opaque = (renderGroupKey.GroupFlags & InstanceGroupFlags.Opaque) != 0;
		renderGroupObject.Tags.SetFrom( new TagSet( renderGroupKey.Tags ) );
		renderGroupKey.RenderLayer.Apply( renderGroupObject );

		RenderGroups.Add( renderGroupKey, renderGroupObject );
		return renderGroupObject;
	}

	internal void RegisterSprite( Guid componentId, SpriteRenderer component )
	{
		var key = GetRenderGroupKey( component, component.Tags as GameTags, component.RenderOptions );
		if ( !RenderGroups.ContainsKey( key ) )
			CreateRenderGroup( key );

		InsertInRenderGroup( componentId, component, key );
	}

	internal void UpdateSprite( Guid componentId, SpriteRenderer component )
	{
		// If found in old renderGroup, we unregister it and register it in the new one
		if ( FindCurrentRenderGroup( componentId ) is RenderGroupKey oldRenderGroup )
		{
			var newRenderGroup = GetRenderGroupKey( component, (GameTags)component.Tags, component.RenderOptions );
			if ( !oldRenderGroup.Equals( newRenderGroup ) )
			{
				RemoveFromRenderGroup( componentId, oldRenderGroup );
				RegisterSprite( componentId, component );
			}
			else
			{
				// Same render group, just update the sprite data
				RenderGroups[oldRenderGroup].UpdateSprite( componentId, component );
			}
		}
		else
		{
			// Sprite not found in any render group, register it
			RegisterSprite( componentId, component );
		}
	}

	internal void UnregisterSprite( Guid componentId )
	{
		if ( FindCurrentRenderGroup( componentId ) is RenderGroupKey rg )
		{
			RenderGroups[rg].UnregisterSprite( componentId );
		}
	}

	/// <summary>
	/// A key that defines a render group used in SceneSpriteSystem. Each permutation of this object represent a different sprite batch.
	/// </summary>
	readonly record struct RenderGroupKey( InstanceGroupFlags GroupFlags, HashSet<string> Tags, RenderOptions RenderLayer )
	{
		public bool Equals( RenderGroupKey other )
		{
			return GroupFlags == other.GroupFlags && Tags.SetEquals( other.Tags ) && RenderLayer.Equals( other.RenderLayer );
		}

		public override int GetHashCode()
		{
			HashCode hash = new();
			hash.Add( GroupFlags );

			// Deterministic hashing for tags based on value instead of reference
			foreach ( var tag in Tags.Order() )
			{
				hash.Add( tag );
			}

			hash.Add( RenderLayer );
			return hash.ToHashCode();
		}
	}

	[Flags]
	internal enum InstanceGroupFlags
	{
		None = 0,
		CastShadow = 1 << 0,
		CastOnlyShadow = 1 << 1,
		Transparent = 1 << 2,
		Additive = 1 << 3,
		Opaque = 1 << 4
	}
}
