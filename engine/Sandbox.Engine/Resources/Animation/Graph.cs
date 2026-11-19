using NativeEngine;

namespace Sandbox.Animation;

/// <summary>
/// A compiled animation graph (.ag). Can be loaded from disk or created
/// procedurally with <see cref="GraphBuilder"/>.
/// </summary>
public sealed class Graph : Resource, IDisposable
{
	internal HAnimGraph2 native;

	/// <summary>
	/// Graphs loaded from disk or provided by mounts are shared. Only graphs owned by
	/// their creator can be released through Dispose.
	/// </summary>
	private bool shared;

	/// <summary>
	/// Build an animation graph at runtime, node by node.
	/// </summary>
	public static GraphBuilder Builder => new();

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Whether the animation graph is invalid, or has not yet loaded.
	/// </summary>
	public override bool IsError => native.IsNull || !native.IsStrongHandleValid();

	/// <summary>
	/// Animation graph resource name.
	/// </summary>
	public string Name => ResourcePath;

	private Graph( HAnimGraph2 native, string name, bool procedural )
	{
		this.native = native;
		shared = !procedural;
		SetResourceIdentity( name, procedural ? null : native.GetGuid() );
		if ( !NativeGlue.Resources.IsAnonymousResource( native.GetBindingPtr() ) )
			Game.Resources.RegisterWeak( this );
	}

	/// <summary>
	/// Load an animation graph from the given file.
	/// </summary>
	public static Graph Load( string filename ) => Load( (ResourceId)filename );

	internal static Graph Load( ResourceId id )
	{
		ThreadSafe.AssertIsMainThread();

		if ( id.Guid is Guid guid && Game.Resources.TryGet<Graph>( guid, out var resource ) )
			return resource;

		var path = FixPath( id.ResolvePath() );
		if ( string.IsNullOrWhiteSpace( path ) && id.Guid is null )
			return null;

		if ( Mounting.MountUtility.IsMountPath( path ) )
			return Mounting.Directory.TryLoad( path, out var mounted ) ? mounted as Graph : null;

		return FromNative( NativeGlue.Resources.GetAnimGraph2( path, id.Guid ?? Guid.Empty ), path );
	}

	public static Task<Graph> LoadAsync( string filename )
	{
		ThreadSafe.AssertIsMainThread();
		return ResourceLibrary.LoadAsync<Graph>( FixPath( filename ) );
	}

	internal override void OnMounted( string path )
	{
		ThreadSafe.AssertIsMainThread();
		if ( shared )
		{
			if ( ResourcePath != FixPath( path ) )
				throw new InvalidOperationException( $"Mounted animation graph '{path}' must be created by its loader, not shared from '{ResourcePath}'." );
			if ( Game.Resources.Get<Graph>( ResourcePath ) != this )
				Game.Resources.RegisterWeak( this );
			return;
		}

		RegisterWeakResourceId( path );
		shared = true;
	}

	/// <summary>
	/// Create a procedural animation graph from a serialized graph definition. The graph is
	/// anonymous - it can never be found by name, so the name is only used to identify it in
	/// logs and profiles and doesn't have to be unique. Dispose it when you're done with it.
	/// </summary>
	internal static unsafe Graph Create( string name, byte[] definition )
	{
		ThreadSafe.AssertIsMainThread();
		fixed ( byte* data = definition )
			return FromNative( NativeGlue.Resources.CreateCompiledAnimGraph2( name, (IntPtr)data, definition.Length ), name, procedural: true );
	}

	/// <summary>
	/// Try to make it so only one Graph class exists for each graph
	/// </summary>
	internal static Graph FromNative( HAnimGraph2 native, string name, bool procedural = false )
	{
		if ( native.IsNull )
			return null;

		if ( !native.IsStrongHandleValid() )
		{
			native.DestroyStrongHandle();
			return null;
		}

		var instanceId = native.GetBindingPtr().ToInt64();
		if ( NativeResourceCache.TryGetValue<Graph>( instanceId, out var graph ) )
		{
			native.DestroyStrongHandle();
			if ( !NativeGlue.Resources.IsAnonymousResource( graph.native.GetBindingPtr() ) )
				Game.Resources.RegisterWeak( graph );
			return graph;
		}

		graph = new Graph( native, name, procedural );
		NativeResourceCache.Add( instanceId, graph );

		return graph;
	}

	internal override void Destroy()
	{
		if ( !native.IsNull )
		{
			var n = native;
			native = default;

			// Evict from NativeResourceCache so a new wrapper can be created if the same
			// native pointer is reused
			NativeResourceCache.Remove( n.GetBindingPtr().ToInt64() );

			MainThread.Queue( () => n.DestroyStrongHandle() );
		}

		base.Destroy();
	}

	/// <summary>
	/// Releases the caller's handle to a graph built at runtime. A renderer's main graph assignment
	/// only keeps a weak handle, so keep that graph alive while it is playing. External graph slots
	/// retain their own strong handles until disconnected (or until a recording using them is cleared).
	/// Graphs loaded from disk or mounts are shared, so this does nothing for those.
	/// </summary>
	public void Dispose()
	{
		if ( shared )
			return;

		Destroy();
	}

	/// <summary>
	/// Number of animation slots referenced by this graph. Slots are resolved
	/// against a model's animations by name at bind time.
	/// </summary>
	public int AnimationSlotCount => native.GetNumAnimationSlots();

	/// <summary>
	/// Name of the animation in the given slot.
	/// </summary>
	/// <param name="index">Index of the animation slot, starting at 0.</param>
	/// <exception cref="ArgumentOutOfRangeException">The index is outside the animation slot range.</exception>
	public string GetAnimationSlotName( int index )
	{
		if ( index < 0 || index >= AnimationSlotCount )
			throw new ArgumentOutOfRangeException( nameof( index ) );

		return native.GetAnimationSlotName( index );
	}

	/// <summary>
	/// The variation of the graph this resource was compiled for. Each variation animates its own
	/// model and can give any node its own data, and is compiled into its own resource - so playing
	/// a variation is loading its resource, named "mygraph.ag+variation.ag".
	/// </summary>
	public string VariationId => native.GetVariationID();

	/// <summary>
	/// The model this variation was authored against.
	/// </summary>
	public string ModelName => native.GetModelName();

	/// <summary>
	/// Describes a control parameter on a <see cref="Graph"/> - an
	/// input that game code sets at runtime.
	/// </summary>
	public sealed record Parameter( string Name, Type Type, float MinValue, float MaxValue, IReadOnlyList<string> Options )
	{
		/// <summary>
		/// Whether this float parameter has a bounded range.
		/// </summary>
		public bool HasRange => MinValue < MaxValue;
	}

	List<Parameter> _parameters;

	/// <summary>
	/// Parameter descriptions exposed to tools. Undescribed parameters remain accessible at runtime.
	/// </summary>
	public IReadOnlyList<Parameter> Parameters => _parameters ??= ReadParameters();

	internal override void OnReloaded()
	{
		_parameters = null;
		base.OnReloaded();
	}

	List<Parameter> ReadParameters()
	{
		var count = native.GetNumParameters();
		var list = new List<Parameter>( count );

		for ( var i = 0; i < count; i++ )
		{
			var name = native.GetParameterName( i );
			var kind = native.GetParameterType( i );

			list.Add( kind switch
			{
				(int)GraphValueKind.Bool => new( name, typeof( bool ), 0f, 0f, [] ),
				(int)GraphValueKind.Id => new( name, typeof( string ), 0f, 0f,
					[.. Enumerable.Range( 0, native.GetParameterOptionCount( i ) ).Select( o => native.GetParameterOption( i, o ) )] ),
				(int)GraphValueKind.Float => new( name, typeof( float ), native.GetParameterMin( i ), native.GetParameterMax( i ), [] ),
				(int)GraphValueKind.Vector => new( name, typeof( Vector3 ), 0f, 0f, [] ),
				(int)GraphValueKind.Target => new( name, typeof( Transform ), 0f, 0f, [] ),
				_ => throw new InvalidOperationException( $"Unsupported animation parameter type '{kind}' for '{name}'." ),
			} );
		}

		return list;
	}
}
