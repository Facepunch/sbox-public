namespace Sandbox;

public sealed class ToolRenderable( Action<ToolRenderContext> render ) : IDisposable
{
	readonly Action<ToolRenderContext> _render = render ?? throw new ArgumentNullException( nameof( render ) );
	readonly Dictionary<SceneWorld, ToolRenderContext> _bindings = [];
	int _version;

	public bool ViewDependent { get; init; }

	internal Transform Transform
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;
			foreach ( var binding in _bindings.Values )
				binding.SetTransform( value );
		}
	} = Transform.Zero;

	internal bool Enabled
	{
		get;
		set
		{
			if ( field == value )
				return;

			field = value;
			foreach ( var binding in _bindings.Values )
				binding.SetEnabled( value );
		}
	} = true;

	public void MarkDirty() => ++_version;

	public void Update( SceneWorld world, ToolRenderView view = default )
	{
		if ( !world.IsValid() )
			throw new ArgumentException( "Scene world is not valid.", nameof( world ) );

		if ( !_bindings.TryGetValue( world, out var binding ) )
		{
			binding = new ToolRenderContext( world );
			_bindings.Add( world, binding );
			world.Destroyed += RemoveWorld;
		}

		if ( binding.Version == _version && !binding.Dirty
			&& (!ViewDependent || binding.View == view) )
			return;

		var version = _version;
		binding.Dirty = true;
		binding.Begin( Transform, Enabled, view );
		try
		{
			_render( binding );
		}
		finally
		{
			binding.End();
		}
		binding.Version = version;
		binding.Dirty = false;
	}

	void RemoveWorld( SceneWorld world )
	{
		world.Destroyed -= RemoveWorld;
		if ( _bindings.Remove( world, out var binding ) )
			binding.Clear();
	}

	public void Dispose()
	{
		foreach ( var (world, binding) in _bindings )
		{
			world.Destroyed -= RemoveWorld;
			binding.Clear();
		}
		_bindings.Clear();
	}
}
