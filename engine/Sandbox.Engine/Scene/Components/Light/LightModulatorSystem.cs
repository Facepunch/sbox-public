using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// Caches scene light/rectangle interactions and publishes compact GPU candidate lists.
/// Static scenes reuse both the lists and GPU uploads across frames and camera views.
/// </summary>
internal sealed class LightModulatorSystem : GameObjectSystem<LightModulatorSystem>
{
	readonly List<LightModulatorData> rectangles = new();
	readonly List<LightModulatorData> previousRectangles = new();
	readonly List<LightState> lights = new();
	readonly List<LightState> previousLights = new();
	List<uint> candidates = new();
	List<uint> previousCandidates = new();
	readonly Dictionary<SceneLight, LightCandidates> ranges = new();
	GpuBuffer<LightModulatorData> rectangleBuffer;
	GpuBuffer<uint> candidateBuffer;

	/// <summary>
	/// Number of local-light/rectangle candidate pairs in the current scene.
	/// </summary>
	public int CandidateCount => candidates.Count;

	/// <summary>
	/// Number of list rebuilds, for diagnosing motion versus static-frame caching.
	/// </summary>
	public int RebuildCount { get; private set; }

	/// <summary>
	/// Number of rectangle buffer uploads, for diagnosing appearance updates independently of candidate rebuilds.
	/// </summary>
	public int RectangleUploadCount { get; private set; }

	/// <summary>
	/// Prepare the scene snapshot immediately before camera rendering.
	/// </summary>
	public LightModulatorSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.PrepareRender, 100, Refresh, "Light modulators" );
	}

	readonly record struct LightState( SceneLight Light, Vector3 Position, float Radius, int BoundsVersion );
	readonly record struct LightCandidates( Vector3 Position, float Radius, int Offset, int Count, int Rebuild );

	/// <summary>
	/// Refresh the scene's modulation snapshot before rendering, including captures without a scene tick.
	/// Unchanged data reuses the existing candidate lists and GPU uploads.
	/// </summary>
	internal void Refresh()
	{
		if ( !Graphics.IsAvailable )
			return;

		ThreadSafe.AssertIsMainThread();

		rectangles.Clear();
		foreach ( var rectangle in Scene.Query<LightModulator>() )
		{
			if ( rectangle.BuildData( out var data ) )
			{
				rectangles.Add( data );
			}
		}

		if ( rectangles.Count > 0 || previousRectangles.Count > 0 )
		{
			lock ( Scene.SceneWorld.InternalSceneObjects )
			{
				if ( LightsChanged() )
				{
					lights.Clear();
					foreach ( var light in Scene.SceneWorld.InternalSceneLights )
					{
						// Directional shaders evaluate all rectangles directly, without a candidate range.
						if ( light is not SceneDirectionalLight && light.IsValid() )
							lights.Add( new( light, light.Position, light.Radius, light.BoundsVersion ) );
					}
				}
			}
		}
		else
		{
			lights.Clear();
		}

		// Appearance changes only need new rectangle data. Candidate lists depend on geometry and lights.
		var rectanglesChanged = !MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( rectangles ) )
			.SequenceEqual( MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( previousRectangles ) ) );
		var boundsChanged = rectanglesChanged && RectangleBoundsChanged();
		var candidatesChanged = boundsChanged || !CollectionsMarshal.AsSpan( lights ).SequenceEqual( CollectionsMarshal.AsSpan( previousLights ) );

		if ( candidatesChanged )
		{
			RebuildCandidates( boundsChanged );
			if ( !CollectionsMarshal.AsSpan( candidates ).SequenceEqual( CollectionsMarshal.AsSpan( previousCandidates ) ) )
			{
				Upload( ref candidateBuffer, candidates, "LightModulatorCandidates" );
				Scene.RenderAttributes.Set( "LightModulatorCandidateCount", candidates.Count );
			}

			previousLights.Clear();
			previousLights.AddRange( lights );
		}

		if ( rectanglesChanged )
		{
			Upload( ref rectangleBuffer, rectangles, "LightModulatorRectangles" );
			if ( rectangles.Count > 0 )
				RectangleUploadCount++;

			Scene.RenderAttributes.Set( "LightModulatorRectangleCount", rectangles.Count );
			Scene.RenderAttributes.Set( "LightModulators_Enabled", rectangles.Count > 0 ? 1 : 0 ); // Specialization constant: lit shaders leave modulators out when 0
			previousRectangles.Clear();
			previousRectangles.AddRange( rectangles );
		}
	}

	/// <summary>
	/// Inspect only local-light identity and spatial versions before reading any native light data.
	/// Invalid lights are excluded immediately, even before their deferred native deletion completes.
	/// </summary>
	bool LightsChanged()
	{
		int index = 0;
		foreach ( var light in Scene.SceneWorld.InternalSceneLights )
		{
			if ( light is SceneDirectionalLight || !light.IsValid() )
				continue;

			if ( index >= lights.Count || lights[index].Light != light || lights[index].BoundsVersion != light.BoundsVersion )
				return true;

			index++;
		}

		return index != lights.Count;
	}

	/// <summary>
	/// Compare the fields used by IntersectsLight at each GPU rectangle index.
	/// Adding, removing or reordering different bounds also invalidates candidate indices.
	/// </summary>
	bool RectangleBoundsChanged()
	{
		if ( rectangles.Count != previousRectangles.Count )
			return true;

		for ( int i = 0; i < rectangles.Count; i++ )
		{
			var current = rectangles[i];
			var previous = previousRectangles[i];
			if ( current.Infinite != previous.Infinite || !current.Origin.Equals( previous.Origin ) )
				return true;
			if ( current.Infinite != 0 )
			{
				if ( !current.Normal.Equals( previous.Normal ) ) return true;
				continue;
			}
			// Reciprocal axes can change by very little while moving the edges of a large pane substantially.
			if ( !current.U.Equals( previous.U ) || !current.V.Equals( previous.V )
				|| current.HalfWidth != previous.HalfWidth || current.HalfHeight != previous.HalfHeight )
				return true;
		}

		return false;
	}

	/// <summary>
	/// Rebuild local-light ranges when rectangle bounds or the light snapshot changes.
	/// </summary>
	void RebuildCandidates( bool boundsChanged )
	{
		RebuildCount++;
		(candidates, previousCandidates) = (previousCandidates, candidates);
		candidates.Clear();
		foreach ( var state in lights )
		{
			var first = candidates.Count;
			ref var range = ref CollectionsMarshal.GetValueRefOrAddDefault( ranges, state.Light, out var known );
			if ( known && !boundsChanged && range.Position.Equals( state.Position ) && range.Radius.Equals( state.Radius ) )
			{
				candidates.AddRange( CollectionsMarshal.AsSpan( previousCandidates ).Slice( range.Offset, range.Count ) );
			}
			else
			{
				for ( int i = 0; i < rectangles.Count; i++ )
				{
					if ( rectangles[i].IntersectsLight( state.Position, state.Radius ) )
						candidates.Add( (uint)i );
				}
			}

			var count = candidates.Count - first;
			range = new( state.Position, state.Radius, first, count, RebuildCount );
			state.Light.SetModulatorRange( count > 0 ? (uint)first : 0, (uint)count );
		}

		foreach ( var state in previousLights )
		{
			if ( ranges[state.Light].Rebuild == RebuildCount ) continue;
			ranges.Remove( state.Light );
			if ( state.Light.IsValid() )
				state.Light.SetModulatorRange( 0, 0 );
		}
	}

	void Upload<T>( ref GpuBuffer<T> buffer, List<T> values, string name ) where T : unmanaged
	{
		if ( values.Count > 0 )
		{
			if ( buffer is null || buffer.ElementCount < values.Count )
			{
				buffer?.Dispose();
				buffer = new GpuBuffer<T>( Math.Max( 64, values.Count * 2 ), debugName: name );
			}

			buffer.SetData( values );
		}

		Scene.RenderAttributes.Set( name, buffer );
	}

	/// <summary>
	/// Remove candidate ranges before releasing the scene-owned GPU buffers.
	/// </summary>
	public override void Dispose()
	{
		foreach ( var state in previousLights )
		{
			if ( state.Light.IsValid() )
				state.Light.SetModulatorRange( 0, 0 );
		}

		Scene.RenderAttributes.Set( "LightModulatorRectangleCount", 0 );
		Scene.RenderAttributes.Set( "LightModulators_Enabled", 0 );
		Scene.RenderAttributes.Set( "LightModulatorCandidateCount", 0 );
		Scene.RenderAttributes.Set( "LightModulatorRectangles", (GpuBuffer)null );
		Scene.RenderAttributes.Set( "LightModulatorCandidates", (GpuBuffer)null );
		rectangleBuffer?.Dispose();
		candidateBuffer?.Dispose();
		base.Dispose();
	}
}
