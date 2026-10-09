using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Groups drawing in an offscreen texture for opacity, filtering or masking.
	/// Disposal composites the layer and restores drawing state; dispose nested layers in reverse order.
	/// </summary>
	/// <param name="bounds">Finite, positive source bounds in the parent's drawing coordinates. Clips the group's contents.</param>
	/// <param name="opacity">Opacity of the completed group, from zero to one. Multiplies the parent's drawing opacity.</param>
	/// <param name="filter">Optional filters applied to the completed group. Null leaves it unchanged.</param>
	/// <param name="mask">Optional image mask applied to the completed group.</param>
	public LayerScope BeginLayer( Rect bounds, float opacity = 1, Filter? filter = null, Mask? mask = null )
	{
		if ( !ValidBounds( bounds ) )
			throw new ArgumentOutOfRangeException( nameof( bounds ) );
		if ( !float.IsFinite( opacity ) || opacity < 0 || opacity > 1 )
			throw new ArgumentOutOfRangeException( nameof( opacity ) );

		var effects = filter ?? new Filter();
		effects.Validate();
		if ( mask is Mask m && (m.Texture is null || !ValidBounds( m.Rect ) || !float.IsFinite( m.Rotation )
			|| !Enum.IsDefined( m.Mode ) || !Enum.IsDefined( m.Repeat ) || !Enum.IsDefined( m.Sampling )) )
			throw new ArgumentOutOfRangeException( nameof( mask ) );

		return new LayerScope( ActiveContext, bounds, opacity, effects, mask );
	}

	/// <summary>
	/// Composites a completed layer and restores its parent's drawing state on disposal.
	/// Dispose nested layers in reverse order, before ending the painter.
	/// </summary>
	public ref struct LayerScope
	{
		Context _context;
		readonly State _state;
		readonly long _recording;
		readonly PainterBatcher.Target _destination;
		readonly Rect _bounds;
		readonly Matrix _baseTransform;
		readonly float _inheritedOpacity;
		readonly string _name;
		readonly string _previousLayer;
		readonly Rect _layerBounds;
		readonly Filter _filter;
		readonly Mask? _mask;
		readonly float _opacity;

		internal LayerScope( Context context, Rect bounds, float opacity, Filter filter, Mask? mask )
		{
			_context = context;
			_recording = context.Recording;
			_state = context.State;
			_bounds = context.Bounds;
			_baseTransform = context.BaseTransform;
			_inheritedOpacity = context.InheritedOpacity;
			_opacity = opacity;
			_layerBounds = bounds;
			_filter = filter;
			_mask = mask;

			var output = context.Batcher;
			output.Flush();
			_destination = output.Destination;
			_name = output.NextLayerName();
			_previousLayer = context.ActiveLayer;
			context.ActiveLayer = _name;
			var commands = output.CommandList;
			commands.PushRenderTarget();
			var target = commands.GetRenderTarget( _name, (int)MathF.Ceiling( bounds.Width ), (int)MathF.Ceiling( bounds.Height ), ImageFormat.RGBA8888, ImageFormat.None );
			commands.SetRenderTarget( target );
			commands.Clear( Color.Transparent, clearDepth: false, clearStencil: false );
			output.Destination = new() { GammaOutput = true };
			context.Bounds = bounds;
			context.BaseTransform = Matrix.CreateTranslation( new Vector3( -bounds.Left, -bounds.Top, 0 ) );
			context.InheritedOpacity = 1;
			context.State.Transform = Matrix.Identity;
			context.State.Opacity = 1;
			context.State.OverrideBlendMode = BlendMode.Normal;
			context.State.ClipIndex = -1;
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			if ( _context.Recording != _recording )
			{
				_context = null;
				return;
			}

			if ( !_context.IsActive || _context.ActiveLayer != _name )
				throw new InvalidOperationException( "Dispose layers in reverse order before painting ends." );

			var owner = _context;
			_context = null;
			var output = owner.Batcher;
			var source = new RenderTargetHandle { Name = _name };
			try
			{
				try
				{
					output.Flush();
				}
				finally
				{
					output.CommandList.PopRenderTarget();
					output.Destination = _destination;
					owner.ActiveLayer = _previousLayer;
					owner.Bounds = _bounds;
					owner.BaseTransform = _baseTransform;
					owner.InheritedOpacity = _inheritedOpacity;
					owner.State = _state;
				}

				if ( _state.HasArea && _state.Opacity * _inheritedOpacity * _opacity > 0 )
					owner.Painter.CompositeLayer( source, _layerBounds, _filter, _mask, _opacity );
			}
			finally
			{
				output.CommandList.ReleaseRenderTarget( source );
			}
		}
	}
}
