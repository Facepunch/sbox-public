using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	PainterBatcher Output => GetActiveContext().Batcher;

	internal Scissoring DestinationClip => Output.Destination.Scissor;
	internal float InheritedOpacity => GetActiveContext().InheritedOpacity;
	internal BlendMode InheritedBlendMode => GetActiveContext().InitialBlendMode;
	internal int InstanceCount => Output.Count;
	internal int DrawCount => Output.DrawCalls;
	internal int FrameGrabCount => Output.FrameGrabs;

	internal void Flush() => Output.Flush();

	internal void SetViewport( Rect bounds, Matrix? worldMatrix = null )
	{
		Output.SetViewport( bounds, worldMatrix );
	}

	internal DestinationScope WithDestination( Rect bounds, float scale, float opacity, BlendMode blendMode, Matrix transform, bool? playbackPaused = null )
	{
		return new DestinationScope( this, bounds, scale, opacity, blendMode, transform, playbackPaused );
	}

	internal ContentScope WithContentOrigin( Vector2 origin ) => new( GetActiveContext(), origin );

	internal ref struct ContentScope
	{
		Context _context;
		readonly Matrix _origin;

		internal Painter Painter => new( _context );

		internal ContentScope( Context context, Vector2 origin )
		{
			_context = context;
			_origin = context.BaseTransform;
			context.ResetDrawingState( context.InitialBlendMode );
			context.BaseTransform = Matrix.CreateTranslation( new Vector3( origin, 0 ) );
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			_context.ResetDrawingState( _context.InitialBlendMode );
			_context.BaseTransform = _origin;
			_context = null;
		}
	}

	internal ref struct DestinationScope
	{
		Context _context;
		readonly PainterBatcher _output;
		readonly Matrix _transform;
		readonly bool? _playbackPaused;
		readonly Rect _bounds;
		readonly float _scale;
		readonly float _opacity;
		readonly Matrix _baseTransform;
		readonly BlendMode _blendMode;

		internal Painter Painter => new( _context );

		internal DestinationScope( Painter painter, Rect bounds, float scale, float opacity, BlendMode blendMode, Matrix transform, bool? playbackPaused )
		{
			_context = painter.GetActiveContext();
			_output = painter.Output;
			_transform = _output.Destination.Transform;
			_playbackPaused = _output.Destination.PlaybackPaused;
			_bounds = _context.Bounds;
			_scale = _context.ScaleToScreen;
			_opacity = _context.InheritedOpacity;
			_baseTransform = _context.BaseTransform;
			_blendMode = _context.InitialBlendMode;
			_context.ResetDrawingState( blendMode );
			_context.Bounds = bounds;
			_context.ScaleToScreen = scale;
			_context.InheritedOpacity = opacity;
			_context.BaseTransform = Matrix.Identity;
			_context.DestinationDepth++;
			_output.Destination.Transform = transform;
			_output.Destination.PlaybackPaused = playbackPaused;
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			_output.Destination.Transform = _transform;
			_output.Destination.PlaybackPaused = _playbackPaused;
			// Drawing state belongs to the panel; only destination settings are inherited.
			_context.ResetDrawingState( _blendMode );
			_context.Bounds = _bounds;
			_context.ScaleToScreen = _scale;
			_context.InheritedOpacity = _opacity;
			_context.BaseTransform = _baseTransform;
			_context.DestinationDepth--;
			_context = null;
		}
	}

	internal DestinationClipScope ClipDestination( Rect rect, BorderRadii radii, Matrix transform ) => new( GetActiveContext(), rect, radii, transform );

	internal ref struct DestinationClipScope
	{
		Context _context;
		readonly long _recording;
		readonly int _index;

		internal DestinationClipScope( Context context, Rect rect, BorderRadii radii, Matrix transform )
		{
			_context = context;
			_recording = context.Recording;
			_index = context.Batcher.PushClip( rect, radii, transform );
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			if ( _context.IsActive && _context.Recording == _recording )
				_context.Batcher.PopClip( _index );
			_context = null;
		}
	}

	internal TargetScope Target( string name, Rect bounds ) => new( this, name, bounds );

	internal ref struct TargetScope
	{
		Context _context;
		readonly long _recording;
		readonly int _index;

		internal TargetScope( Painter painter, string name, Rect bounds )
		{
			_context = painter.GetActiveContext();
			_recording = _context.Recording;
			var output = _context.Batcher;
			output.Flush();
			_index = output.PushTarget();
			var commands = output.CommandList;
			int checkpoint = commands.GetCheckpoint();
			try
			{
				commands.PushRenderTarget();
				var handle = commands.GetRenderTarget( name, (int)bounds.Width, (int)bounds.Height, ImageFormat.RGBA8888, ImageFormat.None );
				commands.SetRenderTarget( handle );
				commands.Clear( Color.Transparent );
				output.Destination.Layered = true;
				// Draws inside keep their screen transforms and clips. This cancels the layer's own share,
				// which the composite applies again, and moves the layer's bounds onto the target.
				output.Destination.LayerMatrix = output.Destination.Transform.Inverted * Matrix.CreateTranslation( bounds.Position * -1.0f );
				output.Destination.WorldPanelCombo = 0;
				output.ApplyDestinationAttributes();
			}
			catch
			{
				output.PopTarget( _index );
				commands.Rewind( checkpoint );
				throw;
			}
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			var context = _context;
			if ( !context.IsActive || context.Recording != _recording )
			{
				_context = null;
				return;
			}

			var output = context.Batcher;
			try
			{
				output.Flush();
			}
			finally
			{
				output.PopTarget( _index );
				_context = null;
				output.CommandList.PopRenderTarget();
				output.ApplyDestinationAttributes();
			}
		}
	}

	internal CommandList NativeCommands( bool objectSpace = false )
	{
		var output = Output;
		output.Flush();
		output.ApplyDestinationAttributes( objectSpace );
		return output.CommandList;
	}

	internal void ApplyDestinationAttributes( bool objectSpace = false ) => Output.ApplyDestinationAttributes( objectSpace );

	internal void FilterBackdrop( Rect rect, Filter filter, BorderRadii radii )
	{
		var data = new BackdropData( rect, filter, radii, InheritedOpacity );
		Output.AddBackdrop( data, InheritedBlendMode );
	}
}
