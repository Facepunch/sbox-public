using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// Records rendering commands for deferred playback on the render thread.
/// </summary>
[Expose]
public sealed unsafe partial class CommandList
{
	readonly Lock _lock = new Lock();

	private string _debugName;
	private ProfilingSampler _sampler = DefaultSampler;

	static readonly ProfilingSampler DefaultSampler = new( "CommandList" );

	public string DebugName
	{
		get => _debugName;
		set
		{
			// Building a sampler encodes the name for native, so don't redo it for the same value
			if ( _debugName == value )
				return;

			_debugName = value;
			_sampler = string.IsNullOrEmpty( value ) ? DefaultSampler : new ProfilingSampler( value );
		}
	}

	public bool Enabled { get; set; }
	public Flag Flags { get; set; }

	public CommandList()
	{
		Enabled = true;

		Attributes = new AttributeAccess( this, GetLocalAttributes );
	}

	public CommandList( string debugName ) : this()
	{
		DebugName = debugName;
	}

	/// <summary>
	/// Holds the function and state data for a single command. 
	/// Target: fit within 2 cache lines (128 bytes).
	/// </summary>
	struct Entry
	{
		public delegate*< ref Entry, CommandList, void > Execute;

		// These should store REFERENCE types only. If you store value types here
		// they will be boxed and allocate. It's not the end of the world, but it's something.
		public object Object1;
		public object Object2;
		public object Object3;
		public object Object4;
		public object Object5;

		public StringToken Token;

		// One inline exact integer, or an offset into the recorded payload.
		public int Count;

		/// <summary>
		/// Identifies a deferred view without boxing or allocating a key object.
		/// </summary>
		public long ViewId;

		public Vector4 Data1;
		public Vector4 Data2;
		public Vector4 Data3;

		// Integral commands reuse the vector-width storage without a floating-point conversion.
		public (int X, int Y, int Z, int W) Ints
		{
			get => Unsafe.As<Vector4, (int, int, int, int)>( ref Data1 );
			set => Data1 = Unsafe.As<(int, int, int, int), Vector4>( ref value );
		}

		public (uint X, uint Y, uint Z, uint W) UInts
		{
			get => Unsafe.As<Vector4, (uint, uint, uint, uint)>( ref Data1 );
			set => Data1 = Unsafe.As<(uint, uint, uint, uint), Vector4>( ref value );
		}

		public (int X, int Y, int Z, int W) Ints2
		{
			get => Unsafe.As<Vector4, (int, int, int, int)>( ref Data2 );
			set => Data2 = Unsafe.As<(int, int, int, int), Vector4>( ref value );
		}
		// Matrix attributes are written across Data1..Data4 through a reinterpret of Data1.
#pragma warning disable CS0649
		public Vector4 Data4;
#pragma warning restore CS0649
	}

	/// <summary>
	/// What a list has recorded: its entries in order and the data they carry. A list keeps one, and a second while a render
	/// thread's frame may replay it (<see cref="Writing"/>).
	/// </summary>
	sealed class Recorded
	{
		public readonly List<Entry> Entries = new List<Entry>( 8 );

		// Struct payloads too big for an Entry, written here instead of boxed. Reused across Reset.
		public byte[] Payload = [];
		public int PayloadUsed;

		// Struct payloads that hold references (view setups), kept in typed lists instead of boxed. Cleared on Reset.
		public List<ViewSetup> ViewSetups;
		public List<ReflectionSetup> ReflectionSetups;
		public List<RefractionSetup> RefractionSetups;
		public List<RendererSetup> RendererSetups;

		public void Clear()
		{
			Entries.Clear();
			PayloadUsed = 0;
			ViewSetups?.Clear();
			ReflectionSetups?.Clear();
			RefractionSetups?.Clear();
			RendererSetups?.Clear();
		}

		/// <summary>Become a copy of <paramref name="other"/>, keeping this set's storage: nothing allocates once it's grown.</summary>
		public void CopyFrom( Recorded other )
		{
			Clear();
			Entries.AddRange( other.Entries );
			if ( Payload.Length < other.PayloadUsed ) Payload = new byte[other.Payload.Length];
			other.Payload.AsSpan( 0, other.PayloadUsed ).CopyTo( Payload );
			PayloadUsed = other.PayloadUsed;
			Copy( other.ViewSetups, ref ViewSetups );
			Copy( other.ReflectionSetups, ref ReflectionSetups );
			Copy( other.RefractionSetups, ref RefractionSetups );
			Copy( other.RendererSetups, ref RendererSetups );
		}

		static void Copy<T>( List<T> from, ref List<T> to )
		{
			if ( from is null || from.Count == 0 ) return;
			to ??= new List<T>( from.Count );
			to.AddRange( from );
		}
	}

	/// <summary>
	/// An ordered list of entries that will execute on the render thread, and what they carry. Recording and resetting are
	/// serialized by <see cref="_lock"/>; a replay reads the set it took under it (<see cref="Playback"/>).
	/// </summary>
	Recorded _recorded = new();

	/// <summary>
	/// What the render thread's frame replays while the main thread records the next frame into <see cref="_recorded"/>: the
	/// list as it was when the frame was handed over (<see cref="RenderSync.Epoch"/> <see cref="_splitEpoch"/>). Null until
	/// the main thread first records into a list a render thread's frame may replay.
	/// </summary>
	Recorded _held;
	long _splitEpoch = -1;

	// What the entries executing now read their payloads and setups from: the set being replayed
	Recorded _playing;

	/// <summary>
	/// Called under <see cref="_lock"/> before anything changes what's recorded. While a render thread's frame may be replaying
	/// the list (<see cref="RenderSync.Overlapping"/>), the main thread's first change since that frame was handed over keeps
	/// the recorded set for the frame and goes on in the other, as a copy of it or, when <paramref name="clearing"/>, empty: the
	/// frame replays the list as it was handed over, as a frame replays a list on the main thread before the game records the
	/// next one. A list the game leaves alone is never copied. Nothing in serial mode, or on the render thread.
	/// </summary>
	void Writing( bool clearing = false )
	{
		if ( !RenderSync.Overlapping || RenderSync.OnRenderThread ) return;
		var epoch = RenderSync.Epoch;
		if ( _splitEpoch == epoch ) return;
		_splitEpoch = epoch;

		// The other set isn't read in this frame: a replay reads the one recorded until the split, and the held one after.
		// Written in this order for Playback, which reads them in the other order without the lock
		var held = _recorded;
		var fresh = _held ?? new Recorded();
		if ( clearing ) fresh.Clear();
		else fresh.CopyFrom( held );
		Volatile.Write( ref _held, held );
		Volatile.Write( ref _splitEpochPublished, epoch );
		Volatile.Write( ref _recorded, fresh );
		RenderSync.CountListSplit();
	}

	// The epoch of the last split as replays read it (Playback), written after the held set
	long _splitEpochPublished = -1;

	/// <summary>
	/// The set a replay on this thread reads: on the render thread, the list as its frame was handed over (the held set once the
	/// main thread has recorded since); anywhere else, what's recorded now. Without the lock: the main thread may hold it while
	/// it waits for the render thread (a panel's painting, uploading its tables).
	/// </summary>
	Recorded Playback()
	{
		var recorded = Volatile.Read( ref _recorded );
		if ( !RenderSync.OnRenderThread ) return recorded;
		return Volatile.Read( ref _splitEpochPublished ) == RenderSync.Epoch ? Volatile.Read( ref _held ) : recorded;
	}

	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	void AddEntry( delegate*< ref Entry, CommandList, void > execute, Entry data )
	{
		data.Execute = execute;

		lock ( _lock )
		{
			Writing();
			_recorded.Entries.Add( data );
		}
	}

	/// <summary>
	/// Add an entry with a struct payload, read back with <see cref="ReadPayload{T}"/>. Uses <see cref="Entry.Count"/>.
	/// </summary>
	void AddEntry<T>( delegate*< ref Entry, CommandList, void > execute, Entry data, in T payload ) where T : unmanaged
	{
		data.Execute = execute;

		lock ( _lock )
		{
			Writing();
			var recorded = _recorded;
			var size = Unsafe.SizeOf<T>();
			if ( recorded.PayloadUsed + size > recorded.Payload.Length )
				Array.Resize( ref recorded.Payload, Math.Max( Math.Max( recorded.Payload.Length * 2, 256 ), recorded.PayloadUsed + size ) );

			MemoryMarshal.Write( recorded.Payload.AsSpan( recorded.PayloadUsed ), in payload );
			data.Count = recorded.PayloadUsed;
			recorded.PayloadUsed += size;
			recorded.Entries.Add( data );
		}
	}

	/// <summary>Add an entry with bytes copied into the payload, at <see cref="Entry.Count"/>.</summary>
	void AddEntryBytes( delegate*< ref Entry, CommandList, void > execute, Entry data, ReadOnlySpan<byte> bytes )
	{
		data.Execute = execute;

		lock ( _lock )
		{
			Writing();
			var recorded = _recorded;
			if ( recorded.PayloadUsed + bytes.Length > recorded.Payload.Length )
				Array.Resize( ref recorded.Payload, Math.Max( Math.Max( recorded.Payload.Length * 2, 256 ), recorded.PayloadUsed + bytes.Length ) );

			bytes.CopyTo( recorded.Payload.AsSpan( recorded.PayloadUsed ) );
			data.Count = recorded.PayloadUsed;
			recorded.PayloadUsed += bytes.Length;
			recorded.Entries.Add( data );
		}
	}

	// Read while the entry executes, from the set being replayed
	T ReadPayload<T>( in Entry entry ) where T : unmanaged => MemoryMarshal.Read<T>( (_playing ?? _recorded).Payload.AsSpan( entry.Count ) );

	// A setup payload of an executing entry, from the set being replayed
	ViewSetup PlayedViewSetup( in Entry entry ) => (_playing ?? _recorded).ViewSetups[entry.Count];
	ReflectionSetup PlayedReflectionSetup( in Entry entry ) => (_playing ?? _recorded).ReflectionSetups[entry.Count];
	RefractionSetup PlayedRefractionSetup( in Entry entry ) => (_playing ?? _recorded).RefractionSetups[entry.Count];
	RendererSetup PlayedRendererSetup( in Entry entry ) => (_playing ?? _recorded).RendererSetups[entry.Count];

	/// <summary>
	/// Add an entry with a struct payload that holds references, kept in the store <paramref name="store"/> picks from the
	/// recorded set (made the first time) at <see cref="Entry.Count"/>.
	/// </summary>
	void AddEntry<T>( delegate*< ref Entry, CommandList, void > execute, Entry data, Func<Recorded, List<T>> store, in T payload ) where T : struct
	{
		data.Execute = execute;

		lock ( _lock )
		{
			Writing();
			var list = store( _recorded );
			data.Count = list.Count;
			list.Add( payload );
			_recorded.Entries.Add( data );
		}
	}

	static readonly Func<Recorded, List<ViewSetup>> ViewSetupStore = static r => r.ViewSetups ??= new List<ViewSetup>( 2 );
	static readonly Func<Recorded, List<ReflectionSetup>> ReflectionSetupStore = static r => r.ReflectionSetups ??= new List<ReflectionSetup>( 2 );
	static readonly Func<Recorded, List<RefractionSetup>> RefractionSetupStore = static r => r.RefractionSetups ??= new List<RefractionSetup>( 2 );
	static readonly Func<Recorded, List<RendererSetup>> RendererSetupStore = static r => r.RendererSetups ??= new List<RendererSetup>( 2 );

	/// <summary>
	/// This lives for the lifetime of the command list and is 
	/// used to store temporary render targets and other state.
	/// </summary>
	private class State
	{
		public Dictionary<string, RenderTarget> renderTargets = new();

		/// <summary>
		/// Resolved view metadata for this playback, shared with inserted command lists.
		/// </summary>
		public Dictionary<long, ViewData> views = new();

		public Stack<(RenderTarget Target, NativeEngine.RenderViewport Viewport)> renderTargetStack = new();
		public Stack<OpenScope> openScopes = new();

		/// <summary>
		/// Should be called at the end of usage
		/// </summary>
		public void Reset()
		{
			// We just clear the list - RenderTargets get freed and 
			// re-added to the pool automatically.
			renderTargets.Clear();
			views.Clear();
			renderTargetStack.Clear();

			// Execution closes these itself, this is only here so a pooled state never carries one forward
			openScopes.Clear();
		}

		/// <summary>
		/// Sneaky way for externals to get render target
		/// </summary>
		public RenderTarget GetRenderTarget( string name )
		{
			if ( renderTargets.TryGetValue( name, out var target ) )
				return target;

			return default;
		}
	}

	State state;

	public void Reset()
	{
		// Serialize against execution: clearing the entry list or the render-target cache while
		// another thread is mid-execute would corrupt them. The lock makes that safe.
		lock ( _lock )
		{
			Writing( clearing: true );
			if ( _resources is not null )
			{
				foreach ( var resource in _resources )
					resource.Reset();
			}

			// The playback state (the draw attributes a replay fills, its grabs) is the replay's: while a frame handed over may be
			// replaying the list on the render thread it's left to that replay, which starts each from empty
			if ( !RenderSync.Overlapping )
			{
				Attributes.ClearRenderTargets();
				drawAttributeAccess?.ClearRenderTargets();
				drawAttributes?.Clear( false );
			}

			_recorded.Clear();
		}
	}

	public void Blit( Material material, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.Blit( (Material)entry.Object1, (RenderAttributes)entry.Object2 );
		}

		AddEntry( &Execute, new Entry { Object1 = material, Object2 = attributes } );
	}

	public void DrawQuad( Rect rect, Material material, Color color )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawQuad( new Rect( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w ), (Material)entry.Object1, new Color( entry.Data2.x, entry.Data2.y, entry.Data2.z, entry.Data2.w ) );
		}

		AddEntry( &Execute, new Entry { Data1 = new Vector4( rect.Left, rect.Top, rect.Width, rect.Height ), Object1 = material, Data2 = new Vector4( color.r, color.g, color.b, color.a ) } );
	}

	public void DrawScreenQuad( Material material, Color color )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawQuad( Graphics.Viewport, (Material)entry.Object1, new Color( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w ) );
		}

		AddEntry( &Execute, new Entry { Object1 = material, Data1 = new Vector4( color.r, color.g, color.b, color.a ) } );
	}

	[Obsolete]
	public void Set( StringToken token, float f ) => Attributes.Set( token, f );

	[Obsolete] public void Set( StringToken token, double f ) => Set( token, (float)f );

	[Obsolete]
	public void Set( StringToken token, Vector2 vector2 ) => Attributes.Set( token, vector2 );

	[Obsolete]
	public void Set( StringToken token, Vector3 vector3 ) => Attributes.Set( token, vector3 );

	[Obsolete]
	public void Set( StringToken token, Vector4 vector4 ) => Attributes.Set( token, vector4 );

	[Obsolete]
	public void Set( StringToken token, int i ) => Attributes.Set( token, i );

	[Obsolete]
	public void Set( StringToken token, bool b ) => Attributes.Set( token, b );

	[Obsolete]
	public void Set( StringToken token, Matrix matrix ) => Attributes.Set( token, matrix );

	[Obsolete]
	public void Set( StringToken token, GpuBuffer buffer ) => Attributes.Set( token, buffer );

	[Obsolete]
	public void Set( StringToken token, Texture texture ) => Attributes.Set( token, texture );

	[Obsolete]
	public void SetCombo( StringToken token, int value ) => Attributes.SetCombo( token, value );

	[Obsolete]
	public void SetCombo( StringToken token, bool value ) => Attributes.SetCombo( token, value );

	[Obsolete]
	public void SetCombo<T>( StringToken token, T t ) where T : unmanaged, Enum => Attributes.SetCombo( token, t );

	[Obsolete]
	public void SetConstantBuffer<T>( StringToken token, T data ) where T : unmanaged => Attributes.SetData( token, data );

	[Obsolete]
	public void SetGlobal( StringToken token, GpuBuffer buffer ) => Attributes.Set( token, buffer );

	[Obsolete]
	public void SetGlobal( StringToken token, int i ) => Attributes.Set( token, i );

	[Obsolete]
	public void SetGlobal( StringToken token, bool b ) => Attributes.Set( token, b );

	[Obsolete]
	public void SetGlobal( StringToken token, float f ) => Attributes.Set( token, f );

	[Obsolete] public void SetGlobal( StringToken token, double f ) => SetGlobal( token, (float)f );

	[Obsolete]
	public void SetGlobal( StringToken token, Vector2 vector2 ) => Attributes.Set( token, vector2 );

	[Obsolete]
	public void SetGlobal( StringToken token, Vector3 vector3 ) => Attributes.Set( token, vector3 );

	[Obsolete]
	public void SetGlobal( StringToken token, Vector4 vector4 ) => Attributes.Set( token, vector4 );

	[Obsolete]
	public void SetGlobal( StringToken token, Matrix matrix ) => Attributes.Set( token, matrix );

	[Obsolete]
	public void SetGlobal( StringToken token, Texture texture ) => Attributes.Set( token, texture );

	/// <summary>
	/// Takes a copy of the framebuffer and returns a handle to it
	/// </summary>
	/// <param name="token"></param>
	/// <param name="withMips">Generates mipmaps on the grabbed texture filtered with gaussian blur for each mip</param>
	/// <returns></returns>
	[Obsolete]
	public RenderTargetHandle GrabFrameTexture( string token, bool withMips = false ) => Attributes.GrabFrameTexture( token, withMips );

	/// <summary>
	/// Takes a copy of the depthbuffer and returns a handle to it
	/// </summary>
	/// <param name="token"></param>
	/// <returns></returns>
	[Obsolete]
	public RenderTargetHandle GrabDepthTexture( string token ) => Attributes.GrabDepthTexture( token );

	/// <summary>
	/// Run this CommandList here
	/// </summary>
	public void InsertList( CommandList otherBuffer )
	{
		if ( otherBuffer == this ) return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
			var other = (CommandList)entry.Object1;
			if ( !other.Enabled )
				return;

			// Inserted lists share the parent's render targets and view metadata.
			commandList.ExecuteInserted( other );
		}

		AddEntry( &Execute, new Entry { Object1 = otherBuffer } );
	}

	/// <summary>
	/// Run this command list
	/// </summary>
	internal void ExecuteOnRenderThread()
	{
		if ( !Enabled )
			return;

		// One execution at a time: the render-target stack is per-list state.
		lock ( _playLock )
		{
			var listScope = OpenScopeFor( _sampler );

			try
			{
				Replay( null );
			}
			catch ( System.Exception e )
			{
				if ( BorealisGpuErrors.ShouldLog( e ) ) Log.Warning( e, $"Error when executing CommandList {_debugName}" );
			}
			finally
			{
				CloseScope( listScope );
			}
		}
	}

	/// <summary>
	/// Command buffer flags allow us to skip command buffers if the camera 
	/// doesn't want a particular thing. Like post processing.
	/// </summary>
	public enum Flag
	{
		None = 0,
		PostProcess = 2,
		Hud = 4,
	}

	/// <summary>
	/// Draws a single model at the given Transform immediately.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transform">Transform to draw the model at</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModel( Model model, Transform transform, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var position = new Vector3( entry.Data1.x, entry.Data1.y, entry.Data1.z );
			var scale = new Vector3( entry.Data1.w, entry.Data2.x, entry.Data2.y );
			var rotation = new Rotation( entry.Data2.z, entry.Data2.w, entry.Data3.x, entry.Data3.y );
			var t = new Transform { Position = position, Scale = scale, Rotation = rotation };

			Graphics.DrawModel( (Model)entry.Object1, t, (RenderAttributes)entry.Object2 );
		}

		AddEntry( &Execute, new Entry
		{
			Object1 = model,
			Object2 = attributes,
			Data1 = new Vector4( transform.Position.x, transform.Position.y, transform.Position.z, transform.Scale.x ),
			Data2 = new Vector4( transform.Scale.y, transform.Scale.z, transform.Rotation.x, transform.Rotation.y ),
			Data3 = new Vector4( transform.Rotation.z, transform.Rotation.w, 0, 0 )
		} );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing at a specific LOD level.
	///
	/// Use `GetTransformMatrix( int instance )` in shaders to access the instance transform.
	///
	/// There is a limit of 1,048,576 transform slots per frame when using this method.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transforms">Instance transform data to draw</param>
	/// <param name="lodLevel">LOD level to render (0 = highest detail)</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModelInstanced( Model model, Span<Transform> transforms, int lodLevel, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawModelInstanced( (Model)entry.Object1, ((Transform[])entry.Object5).AsSpan(), entry.Count, (RenderAttributes)entry.Object2 );
		}

		var transformsCopy = transforms.ToArray();
		AddEntry( &Execute, new Entry { Object1 = model, Object5 = transformsCopy, Count = lodLevel, Object2 = attributes } );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing, assuming standard implemented shaders.
	///
	/// Use `GetTransformMatrix( int instance )` in shaders to access the instance transform.
	///
	/// There is a limit of 1,048,576 transform slots per frame when using this method.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transforms">Instance transform data to draw</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModelInstanced( Model model, Span<Transform> transforms, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawModelInstanced( (Model)entry.Object1, ((Transform[])entry.Object5).AsSpan(), (RenderAttributes)entry.Object2 );
		}

		// We need to copy the transforms to the heap to make sure they still exist when the action is executed.
		// We also discussed using a list/array as parameter, but that could lead to issues if the list/array is modified after the call.
		var transformsCopy = transforms.ToArray();
		AddEntry( &Execute, new Entry { Object1 = model, Object5 = transformsCopy, Object2 = attributes } );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing with the number of instances being provided by indirect draw arguments.
	/// Use `SV_InstanceID` semantic in shaders to access the rendered instance.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="buffer">The GPU buffer containing the DrawIndirectArguments</param>
	/// <param name="bufferOffset">Optional offset in the GPU buffer</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModelInstancedIndirect( Model model, GpuBuffer buffer, int bufferOffset = 0, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawModelInstancedIndirect( (Model)entry.Object1, (GpuBuffer)entry.Object2, entry.Count, (RenderAttributes)entry.Object3 );
		}

		AddEntry( &Execute, new Entry { Object1 = model, Object2 = buffer, Count = bufferOffset, Object3 = attributes } );
	}

	/// <summary>
	/// Draws instances of a model using GPU instancing, with per-instance transforms read from
	/// <paramref name="transformBuffer"/> and the instance count provided by indirect draw arguments.
	/// Feeds the standard `GetTransformMatrix()` path, so normal/custom material shaders render unchanged.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transformBuffer">Per-instance transforms, indexed 0..count-1</param>
	/// <param name="indirectArgs">Buffer containing the DrawIndexedInstancedArguments</param>
	/// <param name="argsOffset">Optional byte offset into the indirect args buffer</param>
	/// <param name="lodLevel">LOD level to render (0 = highest detail)</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModelInstancedIndirect( Model model, GpuBuffer transformBuffer, GpuBuffer indirectArgs, int argsOffset = 0, int lodLevel = 0, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawModelInstancedIndirect( (Model)entry.Object1, (GpuBuffer)entry.Object2, (GpuBuffer)entry.Object3, entry.Ints.X, entry.Ints.Y, (RenderAttributes)entry.Object4 );
		}

		AddEntry( &Execute, new Entry { Object1 = model, Object2 = transformBuffer, Object3 = indirectArgs, Ints = (argsOffset, lodLevel, 0, 0), Object4 = attributes } );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing.
	/// This is similar to <see cref="DrawModelInstancedIndirect(Model, GpuBuffer, int, RenderAttributes)"/>,
	/// except the count is provided from the CPU rather than via a GPU buffer.
	///
	/// Use `SV_InstanceID` semantic in shaders to access the rendered instance.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="count">The number of instances to draw</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public void DrawModelInstanced( Model model, int count, RenderAttributes attributes = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawModelInstanced( (Model)entry.Object1, entry.Count, (RenderAttributes)entry.Object2 );
		}

		AddEntry( &Execute, new Entry { Object1 = model, Count = count, Object2 = attributes } );
	}

	/// <summary>
	/// Draws geometry using a vertex buffer and material.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="startVertex">The starting vertex index for rendering.</param>
	/// <param name="vertexCount">The number of vertices to render. If 0, uses all vertices in the buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void Draw<T>( GpuBuffer<T> vertexBuffer, Material material, int startVertex = 0, int vertexCount = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles ) where T : unmanaged
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.Draw( (GpuBuffer<T>)entry.Object1, (Material)entry.Object2, entry.Ints.X, entry.Ints.Y, (RenderAttributes)entry.Object3, (Graphics.PrimitiveType)entry.Ints.Z );
		}

		AddEntry( &Execute, new Entry { Object1 = vertexBuffer, Object2 = material, Ints = (startVertex, vertexCount, (int)primitiveType, 0), Object3 = attributes } );
	}

	/// <summary>
	/// Draws indexed geometry using vertex and index buffers.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="startIndex">The starting index for rendering.</param>
	/// <param name="indexCount">The number of indices to render. If 0, uses all indices in the buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void DrawIndexed<T>( GpuBuffer<T> vertexBuffer, GpuBuffer indexBuffer, Material material, int startIndex = 0, int indexCount = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles ) where T : unmanaged
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.Draw( (GpuBuffer<T>)entry.Object1, (GpuBuffer)entry.Object2, (Material)entry.Object3, entry.Ints.X, entry.Ints.Y, (RenderAttributes)entry.Object4, (Graphics.PrimitiveType)entry.Ints.Z );
		}

		AddEntry( &Execute, new Entry { Object1 = vertexBuffer, Object2 = indexBuffer, Object3 = material, Ints = (startIndex, indexCount, (int)primitiveType, 0), Object4 = attributes } );
	}

	/// <summary>
	/// Draws instanced geometry using a vertex buffer and indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Optional element offset into the indirect buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void DrawInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles ) where T : unmanaged
		=> DrawInstancedIndirect( vertexBuffer, material, indirectBuffer, bufferOffset, attributes, primitiveType, 1 );

	/// <summary>
	/// Draws instanced geometry using a vertex buffer, executing one or more indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Element offset into the indirect buffer.</param>
	/// <param name="attributes">Render attributes to apply only for this draw call, or null.</param>
	/// <param name="primitiveType">The type of primitives to render.</param>
	/// <param name="drawCount">Number of draw argument structs to read and execute.</param>
	/// <param name="stride">Byte stride between draw argument structs. Use this when your indirect buffer packs extra per-draw userdata. Must be a multiple of 4 and at least the size of the draw argument struct. 0 uses the natural size.</param>
	public void DrawInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset, RenderAttributes attributes, Graphics.PrimitiveType primitiveType, uint drawCount, uint stride = 0 ) where T : unmanaged
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var (offset, primitive, count, stride) = entry.UInts;
			Graphics.DrawInstancedIndirect( (GpuBuffer<T>)entry.Object1, (Material)entry.Object2, (GpuBuffer)entry.Object3, offset, (RenderAttributes)entry.Object4, (Graphics.PrimitiveType)primitive, count, stride );
		}

		AddEntry( &Execute, new Entry { Object1 = vertexBuffer, Object2 = material, Object3 = indirectBuffer, UInts = (bufferOffset, (uint)primitiveType, drawCount, stride), Object4 = attributes } );
	}

	/// <summary>
	/// Draws instanced geometry using indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <remarks>
	/// Vertex data is accessed in shader through buffer attribute and SV_VertexID.
	/// </remarks>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Optional element offset into the indirect buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void DrawInstancedIndirect( Material material, GpuBuffer indirectBuffer, uint bufferOffset = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles )
		=> DrawInstancedIndirect( material, indirectBuffer, bufferOffset, attributes, primitiveType, 1 );

	/// <summary>
	/// Draws instanced geometry, executing one or more indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <remarks>
	/// Vertex data is accessed in shader through buffer attribute and SV_VertexID.
	/// </remarks>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Element offset into the indirect buffer.</param>
	/// <param name="attributes">Render attributes to apply only for this draw call, or null.</param>
	/// <param name="primitiveType">The type of primitives to render.</param>
	/// <param name="drawCount">Number of draw argument structs to read and execute.</param>
	/// <param name="stride">Byte stride between draw argument structs. Use this when your indirect buffer packs extra per-draw userdata. Must be a multiple of 4 and at least the size of the draw argument struct. 0 uses the natural size.</param>
	public void DrawInstancedIndirect( Material material, GpuBuffer indirectBuffer, uint bufferOffset, RenderAttributes attributes, Graphics.PrimitiveType primitiveType, uint drawCount, uint stride = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var (offset, primitive, count, stride) = entry.UInts;
			Graphics.DrawInstancedIndirect( (Material)entry.Object1, (GpuBuffer)entry.Object2, offset, (RenderAttributes)entry.Object3, (Graphics.PrimitiveType)primitive, count, stride );
		}

		AddEntry( &Execute, new Entry { Object1 = material, Object2 = indirectBuffer, UInts = (bufferOffset, (uint)primitiveType, drawCount, stride), Object3 = attributes } );
	}

	/// <summary>
	/// Draws instanced indexed geometry using indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Optional element offset into the indirect buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void DrawIndexedInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles ) where T : unmanaged
		=> DrawIndexedInstancedIndirect( vertexBuffer, indexBuffer, material, indirectBuffer, bufferOffset, attributes, primitiveType, 1 );

	/// <summary>
	/// Draws instanced indexed geometry, executing one or more indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <typeparam name="T">The vertex type used for vertex layout.</typeparam>
	/// <param name="vertexBuffer">The GPU buffer containing vertex data.</param>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Element offset into the indirect buffer.</param>
	/// <param name="attributes">Render attributes to apply only for this draw call, or null.</param>
	/// <param name="primitiveType">The type of primitives to render.</param>
	/// <param name="drawCount">Number of draw argument structs to read and execute.</param>
	/// <param name="stride">Byte stride between draw argument structs. Use this when your indirect buffer packs extra per-draw userdata. Must be a multiple of 4 and at least the size of the draw argument struct. 0 uses the natural size.</param>
	public void DrawIndexedInstancedIndirect<T>( GpuBuffer<T> vertexBuffer, GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset, RenderAttributes attributes, Graphics.PrimitiveType primitiveType, uint drawCount, uint stride = 0 ) where T : unmanaged
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var (offset, primitive, count, stride) = entry.UInts;
			Graphics.DrawIndexedInstancedIndirect( (GpuBuffer<T>)entry.Object1, (GpuBuffer)entry.Object2, (Material)entry.Object3, (GpuBuffer)entry.Object4, offset, (RenderAttributes)entry.Object5, (Graphics.PrimitiveType)primitive, count, stride );
		}

		AddEntry( &Execute, new Entry { Object1 = vertexBuffer, Object2 = indexBuffer, Object3 = material, Object4 = indirectBuffer, UInts = (bufferOffset, (uint)primitiveType, drawCount, stride), Object5 = attributes } );
	}

	/// <summary>
	/// Draws instanced indexed geometry using indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <remarks>
	/// Vertex data is accessed in shader through buffer attribute and SV_VertexID.
	/// </remarks>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Optional element offset into the indirect buffer.</param>
	/// <param name="attributes">Optional render attributes to apply only for this draw call.</param>
	/// <param name="primitiveType">The type of primitives to render. Defaults to triangles.</param>
	public void DrawIndexedInstancedIndirect( GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset = 0, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles )
		=> DrawIndexedInstancedIndirect( indexBuffer, material, indirectBuffer, bufferOffset, attributes, primitiveType, 1 );

	/// <summary>
	/// Draws instanced indexed geometry, executing one or more indirect draw arguments stored in a GPU buffer.
	/// </summary>
	/// <remarks>
	/// Vertex data is accessed in shader through buffer attribute and SV_VertexID.
	/// </remarks>
	/// <param name="indexBuffer">The GPU buffer containing index data.</param>
	/// <param name="material">The material to use for rendering.</param>
	/// <param name="indirectBuffer">The GPU buffer containing indirect draw arguments.</param>
	/// <param name="bufferOffset">Element offset into the indirect buffer.</param>
	/// <param name="attributes">Render attributes to apply only for this draw call, or null.</param>
	/// <param name="primitiveType">The type of primitives to render.</param>
	/// <param name="drawCount">Number of draw argument structs to read and execute.</param>
	/// <param name="stride">Byte stride between draw argument structs. Use this when your indirect buffer packs extra per-draw userdata. Must be a multiple of 4 and at least the size of the draw argument struct. 0 uses the natural size.</param>
	public void DrawIndexedInstancedIndirect( GpuBuffer indexBuffer, Material material, GpuBuffer indirectBuffer, uint bufferOffset, RenderAttributes attributes, Graphics.PrimitiveType primitiveType, uint drawCount, uint stride = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var (offset, primitive, count, stride) = entry.UInts;
			Graphics.DrawIndexedInstancedIndirect( (GpuBuffer)entry.Object1, (Material)entry.Object2, (GpuBuffer)entry.Object3, offset, (RenderAttributes)entry.Object4, (Graphics.PrimitiveType)primitive, count, stride );
		}

		AddEntry( &Execute, new Entry { Object1 = indexBuffer, Object2 = material, Object3 = indirectBuffer, UInts = (bufferOffset, (uint)primitiveType, drawCount, stride), Object4 = attributes } );
	}

	/// <summary>
	/// Draws indexed geometry with instancing. Each instance shares the same index buffer.
	/// </summary>
	public void DrawIndexedInstanced( GpuBuffer indexBuffer, Material material, int instanceCount, RenderAttributes attributes = null, Graphics.PrimitiveType primitiveType = Graphics.PrimitiveType.Triangles )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.DrawIndexedInstanced( (GpuBuffer)entry.Object1, (Material)entry.Object2, entry.Ints.X, (RenderAttributes)entry.Object3, (Graphics.PrimitiveType)entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object1 = indexBuffer, Object2 = material, Ints = (instanceCount, (int)primitiveType, 0, 0), Object3 = attributes } );
	}

	/// <summary>
	/// Get a screen sized temporary render target. You should release the returned handle when you're done to return the textures to the pool.
	/// </summary>
	/// <param name="name">The name of the render target handle.</param>
	/// <param name="sizeFactor">Divide the screen size by this factor. 2 would be half screen sized. 1 for full screen sized.</param>
	/// <param name="format">The format for the color buffer. If set to default we'll use whatever the current pipeline is using.</param>
	/// <param name="numMips">Number of mips you want in this texture. You probably don't want this unless you want to generate mips in a second pass.</param>
	/// <returns>A RenderTarget that is ready to render to.</returns>
	public RenderTargetHandle GetRenderTarget( string name, ImageFormat format, int numMips = 1, int sizeFactor = 1 )
		=> RecordTemporaryTarget( name, new TemporaryTarget( 0, 0, sizeFactor, format, ImageFormat.None, MultisampleAmount.MultisampleNone, numMips, "", false ) );

	/// <summary>
	/// Get a screen sized temporary render target. You should release the returned handle when you're done to return the textures to the pool.
	/// </summary>
	/// <param name="name">The name of the render target handle.</param>
	/// <param name="sizeFactor">Divide the screen size by this factor. 2 would be half screen sized. 1 for full screen sized.</param>
	/// <param name="colorFormat">The format for the color buffer. If set to default we'll use whatever the current pipeline is using.</param>
	/// <param name="depthFormat">The format for the depth buffer.</param>
	/// <param name="msaa">The number of msaa samples you'd like. Msaa render textures are a pain in the ass so you're probably gonna regret trying to use this.</param>
	/// <param name="numMips">Number of mips you want in this texture. You probably don't want this unless you want to generate mips in a second pass.</param>
	/// <returns>A RenderTarget that is ready to render to.</returns>
	public RenderTargetHandle GetRenderTarget( string name, int sizeFactor = 1, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1 )
		=> RecordTemporaryTarget( name, new TemporaryTarget( 0, 0, sizeFactor, colorFormat, depthFormat, msaa, numMips, name, false ) );

	/// <summary>
	/// Get a temporary render target. You should release the returned handle when you're done to return the textures to the pool.
	/// </summary>
	/// <param name="name">The name of the render target handle.</param>
	/// <param name="width">Width of the render target you want.</param>
	/// <param name="height">Height of the render target you want.</param>
	/// <param name="colorFormat">The format for the color buffer. If set to default we'll use whatever the current pipeline is using.</param>
	/// <param name="depthFormat">The format for the depth buffer.</param>
	/// <param name="msaa">The number of msaa samples you'd like. Msaa render textures are a pain in the ass so you're probably gonna regret trying to use this.</param>
	/// <param name="numMips">Number of mips you want in this texture. You probably don't want this unless you want to generate mips in a second pass.</param>
	/// <returns>A RenderTarget that is ready to render to.</returns>
	public RenderTargetHandle GetRenderTarget( string name, int width, int height, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1 )
		=> GetRenderTarget( name, width, height, colorFormat, depthFormat, msaa, numMips, engineOwned: false );

	/// <summary>
	/// As above, from the engine's own pool. The UI painter's layers hold menu pixels.
	/// </summary>
	internal RenderTargetHandle GetRenderTarget( string name, int width, int height, ImageFormat colorFormat, ImageFormat depthFormat, MultisampleAmount msaa, int numMips, bool engineOwned )
		=> RecordTemporaryTarget( name, new TemporaryTarget( width, height, null, colorFormat, depthFormat, msaa, numMips, name, engineOwned ) );

	readonly record struct TemporaryTarget( int Width, int Height, int? SizeFactor, ImageFormat Color, ImageFormat Depth,
		MultisampleAmount Msaa, int Mips, string PoolName, bool EngineOwned );

	RenderTargetHandle RecordTemporaryTarget( string name, TemporaryTarget parameters )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var (width, height, factor, color) = entry.Ints;
			var (depth, msaa, mips, engine) = entry.Ints2;
			var poolName = (string)entry.Object2;
			var target = entry.Count == 0
				? Sandbox.RenderTarget.GetTemporary( width, height, (ImageFormat)color, (ImageFormat)depth, (MultisampleAmount)msaa, mips, poolName, engine != 0 )
				: Sandbox.RenderTarget.GetTemporary( factor, (ImageFormat)color, (ImageFormat)depth, (MultisampleAmount)msaa, mips, poolName, engine != 0 );
			commandList.state.renderTargets[(string)entry.Object5] = target;
		}

		AddEntry( &Execute, new Entry
		{
			Object2 = parameters.PoolName,
			Object5 = name,
			Ints = (parameters.Width, parameters.Height, parameters.SizeFactor.GetValueOrDefault(), (int)parameters.Color),
			Ints2 = ((int)parameters.Depth, (int)parameters.Msaa, parameters.Mips, parameters.EngineOwned ? 1 : 0),
			Count = parameters.SizeFactor.HasValue ? 1 : 0
		} );
		return new RenderTargetHandle { Name = name };
	}

	/// <summary>
	/// We're no longer using this RT, return it to the pool
	/// </summary>
	public void ReleaseRenderTarget( RenderTargetHandle handle )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.renderTargets.Remove( (string)entry.Object5, out var target ) )
			{
				target.Dispose();
			}
		}

		AddEntry( &Execute, new Entry { Object5 = handle.Name } );
	}

	/// <summary>
	/// Set the current render target. Setting this will bind the render target and change the viewport to match it.
	/// </summary>
	public void SetRenderTarget( RenderTargetHandle handle )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( !commandList.state.renderTargets.TryGetValue( (string)entry.Object5, out var target ) )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.RenderTarget = target;
		}

		AddEntry( &Execute, new Entry { Object5 = handle.Name } );
	}

	/// <summary>
	/// Set the current render target. Setting this will bind the render target and change the viewport to match it.
	/// </summary>
	public void SetRenderTarget( RenderTarget target )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.RenderTarget = (RenderTarget)entry.Object1;
		}

		AddEntry( &Execute, new Entry { Object1 = target } );
	}

	/// <summary>
	/// Save the render target and viewport active when this command executes.
	/// </summary>
	internal void PushRenderTarget()
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			commandList.state.renderTargetStack.Push( (Graphics.RenderTarget, Graphics.Backend.GetViewport()) );
		}

		AddEntry( &Execute, default );
	}

	/// <summary>
	/// Restore the most recently saved render target and viewport.
	/// </summary>
	internal void PopRenderTarget()
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var saved = commandList.state.renderTargetStack.Pop();
			Graphics.RenderTarget = saved.Target;
			Graphics.Backend.SetViewport( saved.Viewport );
		}

		AddEntry( &Execute, default );
	}

	/// <summary>
	/// Set the current render target. Setting this will bind the render target and change the viewport to match it.
	/// </summary>
	public void ClearRenderTarget()
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.RenderTarget = null;
		}

		AddEntry( &Execute, default );
	}

	/// <summary>
	/// Set the color texture from this named render target to this attribute
	/// </summary>
	[Obsolete]
	public void Set( StringToken token, RenderTargetHandle.ColorTextureRef buffer, int mip = -1 ) => Attributes.Set( token, buffer, mip );

	/// <summary>
	/// Set the color texture from this named render target to this attribute
	/// </summary>
	[Obsolete]
	public void SetGlobal( StringToken token, RenderTargetHandle.ColorIndexRef buffer ) => Attributes.Set( token, buffer );

	/// <summary>
	/// Transitions the finished color texture to pixel shader reads and publishes it in the current view's pipeline slot.
	/// </summary>
	internal void PublishPipelineTexture( PipelineTextureSlot slot, RenderTargetHandle.ColorTextureRef buffer )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			// Borealis.Gpu's table is its backend's, written in the block's list; a headless process has none
			if ( Graphics.Backend is BorealisGpuRenderBackend gpu )
			{
				gpu.ResourceBarrierTransition( target.ColorTarget, ResourceState.PixelShaderResource, -1 );
				gpu.SetPipelineTexture( entry.Count, target.ColorTarget );
			}
		}

		// Dont write out of bounds of the pipeline slots
		if ( slot < 0 || slot >= PipelineTextureSlot.Count )
		{
			Log.Warning( $"[{DebugName ?? "CommandList"}] Invalid pipeline texture slot: {(int)slot}" );
			return;
		}

		AddEntry( &Execute, new Entry { Object5 = buffer.Name, Count = (int)slot } );
	}


	/// <inheritdoc cref="ComputeShader.Dispatch(int, int, int)"/>
	public void DispatchCompute( ComputeShader compute, int threadsX, int threadsY, int threadsZ )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((ComputeShader)entry.Object1).DispatchWithAttributes( Graphics.Attributes, entry.Ints.X, entry.Ints.Y, entry.Ints.Z );
		}

		AddEntry( &Execute, new Entry { Object1 = compute, Ints = (threadsX, threadsY, threadsZ, 0) } );
	}

	/// <inheritdoc cref="ComputeShader.DispatchIndirect(GpuBuffer, uint)"/>
	public void DispatchComputeIndirect( ComputeShader compute, GpuBuffer indirectBuffer, uint indirectElementOffset = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((ComputeShader)entry.Object1).DispatchIndirectWithAttributes( Graphics.Attributes, (GpuBuffer)entry.Object2, unchecked((uint)entry.Count) );
		}

		AddEntry( &Execute, new Entry { Object1 = compute, Object2 = indirectBuffer, Count = unchecked((int)indirectElementOffset) } );
	}

	/// <summary>
	/// A handle to the viewport size
	/// </summary>
	public RenderTargetHandle.SizeHandle ViewportSize => new RenderTargetHandle.SizeHandle { Name = "$vp", IsViewport = true };

	/// <summary>
	/// A handle to the viewport size divided by a factor, rounded up so a thread for every <paramref name="divisor"/> pixels
	/// covers them all. Useful for dispatching at half or quarter resolution.
	/// </summary>
	public RenderTargetHandle.SizeHandle ViewportSizeScaled( int divisor ) => new RenderTargetHandle.SizeHandle { Name = "$vp", IsViewport = true, Divisor = Math.Max( 1, divisor ) };

	/// <summary>
	/// Dispatch a compute shader
	/// </summary>
	public void DispatchCompute( ComputeShader compute, RenderTargetHandle.SizeHandle dimension )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var xyz = commandList.GetDimension( (string)entry.Object5, entry.Count, entry.Ints.X != 0 );
			if ( !xyz.HasValue ) return;

			((ComputeShader)entry.Object1).DispatchWithAttributes( Graphics.Attributes, xyz.Value.x, xyz.Value.y, xyz.Value.z );
		}

		AddEntry( &Execute, new Entry { Object1 = compute, Object5 = dimension.Name, Count = dimension.Divisor, Ints = (dimension.IsViewport ? 1 : 0, 0, 0, 0) } );
	}

	/// <summary>
	/// Called during rendering, convert RenderTargetHandle.SizeHandle to a dimension
	/// </summary>
	Vector3Int? GetDimension( string name, int divisor, bool viewport )
	{
		Vector3Int result;

		if ( viewport )
		{
			result = new Vector3Int( Graphics.Viewport.Width.CeilToInt(), Graphics.Viewport.Height.CeilToInt(), 1 );
		}
		else
		{
			var rt = state.renderTargets.GetValueOrDefault( name );
			if ( rt is null ) return default;

			result = new Vector3Int( rt.Width, rt.Height, 1 );
		}

		if ( divisor > 1 )
		{
			result.x = Math.Max( 1, (result.x + divisor - 1) / divisor );
			result.y = Math.Max( 1, (result.y + divisor - 1) / divisor );
		}

		return result;
	}

	/// <summary>
	/// Clear the current drawing context to given color.
	/// </summary>
	/// <param name="color">Color to clear to.</param>
	/// <param name="clearColor">Whether to clear the color buffer at all.</param>
	/// <param name="clearDepth">Whether to clear the depth buffer.</param>
	/// <param name="clearStencil">Whether to clear the stencil buffer.</param>
	public void Clear( Color color, bool clearColor = true, bool clearDepth = true, bool clearStencil = true )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var color = new Color( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w );
			var clearColor = ((int)entry.Data2.x != 0);
			var clearDepth = ((int)entry.Data2.y != 0);
			var clearStencil = ((int)entry.Data2.z != 0);

			Graphics.Clear( color, clearColor, clearDepth, clearStencil );
		}

		AddEntry( &Execute, new Entry { Data1 = new Vector4( color.r, color.g, color.b, color.a ), Data2 = new Vector4( clearColor ? 1 : 0, clearDepth ? 1 : 0, clearStencil ? 1 : 0, 0 ) } );
	}

	/// <summary>
	/// Clears the given texture to a solid color.
	/// </summary>
	/// <param name="texture">The texture to clear.</param>
	/// <param name="color">The color to clear to. Defaults to transparent black.</param>
	public void Clear( Texture texture, Color color = default )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.Backend.ClearTexture( (Texture)entry.Object1, new Color( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w ) );
		}

		AddEntry( &Execute, new Entry { Object1 = texture, Data1 = new Vector4( color.r, color.g, color.b, color.a ) } );
	}

	/// <summary>
	/// Clears the color texture of the given render target handle to a solid color.
	/// </summary>
	/// <param name="handle">The render target handle whose color texture to clear.</param>
	/// <param name="color">The color to clear to. Defaults to transparent black.</param>
	public void Clear( RenderTargetHandle handle, Color color = default )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.Backend.ClearTexture( target.ColorTarget, new Color( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w ) );
		}

		AddEntry( &Execute, new Entry { Object5 = handle.ColorTexture.Name, Data1 = new Vector4( color.r, color.g, color.b, color.a ) } );
	}

	/// <summary>
	/// Fills the given GPU buffer with a repeated uint32 value.
	/// </summary>
	/// <param name="buffer">The buffer to clear.</param>
	/// <param name="value">The uint32 value to fill with. Defaults to zero.</param>
	public void Clear( GpuBuffer buffer, uint value = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((GpuBuffer)entry.Object1).Clear( unchecked((uint)entry.Count) );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Count = unchecked((int)value) } );
	}

	/// <summary>
	/// Resets the hidden append/structured-buffer counter of <paramref name="buffer"/> to <paramref name="value"/>.
	/// </summary>
	/// <param name="buffer">An <see cref="GpuBuffer.UsageFlags.Append"/> or structured buffer.</param>
	/// <param name="value">The counter value to set. Defaults to zero.</param>
	public void SetCounterValue( GpuBuffer buffer, uint value = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((GpuBuffer)entry.Object1).SetCounterValue( unchecked((uint)entry.Count) );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Count = unchecked((int)value) } );
	}

	/// <summary>
	/// Copies the hidden append-buffer counter of <paramref name="buffer"/> into <paramref name="destBuffer"/>.
	/// Useful for feeding a survivor count into an indirect draw/dispatch argument buffer.
	/// </summary>
	/// <param name="buffer">The <see cref="GpuBuffer.UsageFlags.Append"/> buffer to read the counter from.</param>
	/// <param name="destBuffer">The buffer to write the counter into.</param>
	/// <param name="destBufferOffset">Byte offset into <paramref name="destBuffer"/> to write at.</param>
	public void CopyStructureCount( GpuBuffer buffer, GpuBuffer destBuffer, int destBufferOffset = 0 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((GpuBuffer)entry.Object1).CopyStructureCount( (GpuBuffer)entry.Object2, entry.Count );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Object2 = destBuffer, Count = destBufferOffset } );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Texture Resource.
	/// Transitions the texture resource to a new pipeline stage and access state.
	/// </summary>
	/// <param name="texture">The texture to transition.</param>
	/// <param name="state">The new resource state for the texture.</param>
	/// <param name="mip">The mip level to transition (-1 for all mips).</param>
	public void ResourceBarrierTransition( Texture texture, ResourceState state, int mip = -1 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.ResourceBarrierTransition( (Texture)entry.Object1, (ResourceState)entry.Ints.X, entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object1 = texture, Ints = ((int)state, mip, 0, 0) } );
	}

	/// <summary>
	/// Executes a barrier transition for the color texture of the given render target handle.
	/// </summary>
	/// <param name="texture">The render target color handle.</param>
	/// <param name="state">The new resource state for the texture.</param>
	/// <param name="mip">The mip level to transition (-1 for all mips).</param>
	public void ResourceBarrierTransition( RenderTargetHandle.ColorTextureRef texture, ResourceState state, int mip = -1 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.ResourceBarrierTransition( target.ColorTarget, (ResourceState)entry.Ints.X, entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object5 = texture.Name, Ints = ((int)state, mip, 0, 0) } );
	}

	/// <summary>
	/// Executes a barrier transition for the depth texture of the given render target handle.
	/// </summary>
	/// <param name="texture">The render target depth handle.</param>
	/// <param name="state">The new resource state for the texture.</param>
	/// <param name="mip">The mip level to transition (-1 for all mips).</param>
	public void ResourceBarrierTransition( RenderTargetHandle.DepthTextureRef texture, ResourceState state, int mip = -1 )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.ResourceBarrierTransition( target.DepthTarget, (ResourceState)entry.Ints.X, entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object5 = texture.Name, Ints = ((int)state, mip, 0, 0) } );
	}

	/// <summary>
	/// Executes a barrier transition for the color texture of the given render target handle.
	/// </summary>
	/// <param name="handle">The render target handle.</param>
	/// <param name="state">The new resource state for the texture.</param>
	/// <param name="mip">The mip level to transition (-1 for all mips).</param>
	public void ResourceBarrierTransition( RenderTargetHandle handle, ResourceState state, int mip = -1 )
	{
		ResourceBarrierTransition( handle.ColorTexture, state, mip );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource to a new pipeline stage and access state.
	/// </summary>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="state">The new resource state for the buffer.</param>
	public void ResourceBarrierTransition( GpuBuffer buffer, ResourceState state )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.ResourceBarrierTransition( (GpuBuffer)entry.Object1, (ResourceState)entry.Count );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Count = (int)state } );
	}

	/// <summary>
	/// Executes a barrier transition for the given GPU Buffer Resource.
	/// Transitions the buffer resource from a known source state to a specified destination state.
	/// </summary>
	/// <param name="buffer">The GPU buffer to transition.</param>
	/// <param name="before">The current resource state of the buffer.</param>
	/// <param name="after">The desired resource state of the buffer after the transition.</param>
	public void ResourceBarrierTransition( GpuBuffer buffer, ResourceState before, ResourceState after )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.ResourceBarrierTransition( (GpuBuffer)entry.Object1, (ResourceState)entry.Ints.X, (ResourceState)entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Ints = ((int)before, (int)after, 0, 0) } );
	}

	/// <summary>
	/// Issues a UAV barrier for the given texture, ensuring writes from prior shader invocations
	/// are visible to subsequent ones without changing the resource layout.
	/// </summary>
	/// <param name="texture">The texture to barrier.</param>
	public void UavBarrier( Texture texture )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.UavBarrier( (Texture)entry.Object1 );
		}

		AddEntry( &Execute, new Entry { Object1 = texture } );
	}

	/// <summary>
	/// Issues a UAV barrier for the color texture of the given render target handle, ensuring writes
	/// from prior shader invocations are visible to subsequent ones without changing the resource layout.
	/// Use this for a UAV (RWTexture) that is written in one pass and read back as a UAV in a later pass,
	/// where the layout doesn't change and a plain transition wouldn't emit a barrier.
	/// </summary>
	/// <param name="texture">The render target color handle.</param>
	public void UavBarrier( RenderTargetHandle.ColorTextureRef texture )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( commandList.state.GetRenderTarget( (string)entry.Object5 ) is not { } target )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.UavBarrier( target.ColorTarget );
		}

		AddEntry( &Execute, new Entry { Object5 = texture.Name } );
	}

	/// <summary>
	/// Issues a UAV barrier for the color texture of the given render target handle.
	/// </summary>
	/// <param name="handle">The render target handle.</param>
	public void UavBarrier( RenderTargetHandle handle ) => UavBarrier( handle.ColorTexture );

	/// <summary>
	/// Issues a UAV barrier for the given GPU buffer, ensuring writes from prior shader invocations
	/// are visible to subsequent ones.
	/// </summary>
	/// <param name="buffer">The buffer to barrier.</param>
	public void UavBarrier( GpuBuffer buffer )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.UavBarrier( (GpuBuffer)entry.Object1 );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer } );
	}

	/// <summary>
	/// Sets up per-object lighting for the given scene object. Writes lighting state
	/// into <see cref="Graphics.Attributes"/> at execution time.
	/// </summary>
	internal void SetupLighting( SceneObject obj, RenderAttributes attr = null )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.SetupLighting( (SceneObject)entry.Object1, (RenderAttributes)entry.Object2 );
		}

		AddEntry( &Execute, new Entry { Object1 = obj, Object2 = attr } );
	}

	/// <summary>
	/// Uploads data from an array to a GPU buffer at execution time.
	/// Zero-copy: the array reference is stored directly without copying.
	/// The caller must ensure the array contents remain stable until the command list
	/// has finished executing (i.e. until the next <see cref="Reset"/> call).
	/// While a frame handed over may replay the list on the render thread (<see cref="RenderSync.Overlapping"/>), the main
	/// thread goes on writing the array for the next frame (a particle system's sprites, a pooled array given back): the
	/// bytes are copied into the list as they are when recorded instead.
	/// </summary>
	internal void SetBufferData<T>( GpuBuffer<T> buffer, T[] data, int sourceOffset = 0, int count = -1, int elementOffset = 0 ) where T : unmanaged
	{
		if ( count < 0 ) count = data.Length - sourceOffset;

		static void ExecuteCopied( ref Entry entry, CommandList commandList )
		{
			var buf = (GpuBuffer<T>)entry.Object1;
			var bytes = (commandList._playing ?? commandList._recorded).Payload.AsSpan( entry.Count, entry.Ints.Y * Unsafe.SizeOf<T>() );
			buf.SetData( (ReadOnlySpan<T>)MemoryMarshal.Cast<byte, T>( bytes ), entry.Ints.X );
		}

		if ( RenderSync.Overlapping && !RenderSync.OnRenderThread )
		{
			AddEntryBytes( &ExecuteCopied, new Entry { Object1 = buffer, Ints = (elementOffset, count, 0, 0) }, MemoryMarshal.AsBytes( data.AsSpan( sourceOffset, count ) ) );
			return;
		}

		static void Execute( ref Entry entry, CommandList commandList )
		{
			var buf = (GpuBuffer<T>)entry.Object1;
			var arr = (T[])entry.Object2;
			var (offset, length, srcOffset, _) = entry.Ints;
			buf.SetData( arr.AsSpan( srcOffset, length ), offset );
		}

		AddEntry( &Execute, new Entry { Object1 = buffer, Object2 = data, Ints = (elementOffset, count, sourceOffset, 0) } );
	}

	/// <summary>
	/// Records an action to execute on the render thread. Cache the delegate to avoid allocations when recording.
	/// </summary>
	internal void AddAction( Action a )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			((Action)entry.Object1)?.Invoke();
		}

		AddEntry( &Execute, new Entry { Object1 = a } );
	}

	/// <summary>
	/// Sneaky way for externals to get render target
	/// </summary>
	internal RenderTarget GetRenderTarget( string name ) => state?.GetRenderTarget( name );

	/// <summary>
	/// Generates a mip-map chain for the specified render target.
	/// This will generate mipmaps for the color texture of the render target.
	/// </summary>
	public void GenerateMipMaps( RenderTargetHandle handle, Graphics.DownsampleMethod method = Graphics.DownsampleMethod.GaussianBlur )
		=> GenerateMipMaps( handle, method, 0 );

	/// <summary>
	/// Generates the mips below <paramref name="initialMip"/>, for when the levels above it were written directly.
	/// </summary>
	internal void GenerateMipMaps( RenderTargetHandle handle, Graphics.DownsampleMethod method, int initialMip )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			if ( !commandList.state.renderTargets.TryGetValue( (string)entry.Object5, out var target ) )
			{
				Log.Warning( $"[{commandList.DebugName ?? "CommandList"}] Unknown rt: {(string)entry.Object5}" );
				return;
			}

			Graphics.GenerateMipMaps( target.ColorTarget, (Graphics.DownsampleMethod)entry.Ints.X, entry.Ints.Y );
		}

		AddEntry( &Execute, new Entry { Object5 = handle.Name, Ints = ((int)method, initialMip, 0, 0) } );
	}

	/// <summary>
	/// Generates a mip-map chain for the specified render target.
	/// This will generate mipmaps for the color texture of the render target.
	/// </summary>
	public void GenerateMipMaps( RenderTarget target, Graphics.DownsampleMethod method = Graphics.DownsampleMethod.GaussianBlur )
	{
		static void Execute( ref Entry entry, CommandList commandList )
			=> Graphics.GenerateMipMaps( ((RenderTarget)entry.Object1).ColorTarget, (Graphics.DownsampleMethod)entry.Count );

		AddEntry( &Execute, new Entry { Object1 = target, Count = (int)method } );
	}

	/// <summary>
	/// Generates a mip-map chain for the specified texture.
	/// This will generate mipmaps for the color texture of the texture.
	/// </summary>
	public void GenerateMipMaps( Texture texture, Graphics.DownsampleMethod method = Graphics.DownsampleMethod.GaussianBlur )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			Graphics.GenerateMipMaps( (Texture)entry.Object1, (Graphics.DownsampleMethod)entry.Count );
		}

		AddEntry( &Execute, new Entry { Object1 = texture, Count = (int)method } );
	}

	/// <summary>
	/// Draws text within a rectangle using a prepared <see cref="TextRendering.Scope"/>.
	/// </summary>
	/// <param name="scope">The text rendering scope.</param>
	/// <param name="rect">The rectangle to draw the text in.</param>
	/// <param name="flags">Text alignment flags (optional).</param>
	/// <param name="angleDegrees">Rotation angle in degrees (optional).</param>
	public void DrawText( TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.LeftTop, float angleDegrees = 0f )
	{
		// Resolve the TextBlock at entry-add time so we store a class reference instead of
		// boxing the Scope struct and TextFlag enum into object fields.
		var tb = TextRendering.GetOrCreateTextBlock( scope, flags );
		if ( tb is not null ) DrawText( tb, rect, flags, angleDegrees );
	}

	/// <summary>Draws an already resolved text block, for callers that measured it first.</summary>
	internal void DrawText( TextRendering.TextBlock block, Rect rect, TextFlag flags, float angleDegrees = 0f )
	{
		static void Execute( ref Entry entry, CommandList commandList )
		{
			var position = new Rect( entry.Data1.x, entry.Data1.y, entry.Data1.z, entry.Data1.w );
			Graphics.DrawText( (TextRendering.TextBlock)entry.Object1, position, (TextFlag)entry.Count, entry.Data2.x );
		}

		AddEntry( &Execute, new Entry
		{
			Object1 = block,
			Data1 = new Vector4( rect.Left, rect.Top, rect.Width, rect.Height ),
			Count = (int)flags,
			Data2 = new Vector4( angleDegrees, 0, 0, 0 )
		} );
	}
}
