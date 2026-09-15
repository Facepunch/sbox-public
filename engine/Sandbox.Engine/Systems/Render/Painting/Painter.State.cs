using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Fill for closed shapes. Defaults to Fill.None.
	/// </summary>
	public Fill Fill
	{
		get => ActiveContext.State.Fill;
		set => ActiveContext.State.Fill = value;
	}

	/// <summary>
	/// Stroke for shapes and lines. Defaults to Stroke.None. Open paths always use centered alignment.
	/// </summary>
	public Stroke Stroke
	{
		get => ActiveContext.State.Stroke;
		set
		{
			if ( !Enum.IsDefined( value.Alignment ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			if ( !Enum.IsDefined( value.Style ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			ActiveContext.State.Stroke = value;
		}
	}

	/// <summary>
	/// Alpha multiplier for each draw, clamped to [0, 1]. Defaults to 1.
	/// </summary>
	public float Opacity
	{
		get => ActiveContext.State.Opacity;
		set
		{
			if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			ActiveContext.State.Opacity = Math.Clamp( value, 0, 1 );
		}
	}

	/// <summary>
	/// Style used by Text and MeasureText. Defaults to TextStyle.Default.
	/// </summary>
	public TextStyle TextStyle
	{
		get => ActiveContext.State.TextStyle;
		set
		{
			value.Validate();
			ActiveContext.State.TextStyle = value;
		}
	}

	/// <summary>
	/// Blend mode for subsequent draws. Defaults to BlendMode.Normal.
	/// </summary>
	public BlendMode BlendMode
	{
		get => ActiveContext.State.OverrideBlendMode;
		set
		{
			if ( !Enum.IsDefined( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			ActiveContext.State.OverrideBlendMode = value;
		}
	}

	/// <summary>
	/// Saves drawing state until the returned scope is disposed.
	/// </summary>
	public StateScope Scope() => new( ActiveContext );

	/// <summary>
	/// Restores saved drawing state on disposal. Dispose nested scopes in reverse order.
	/// </summary>
	public ref struct StateScope
	{
		Painter.Context _context;
		readonly State _state;
		readonly long _recording;

		internal StateScope( Painter.Context context )
		{
			_context = context;
			_state = context.State;
			_recording = context.Recording;
		}

		public void Dispose()
		{
			if ( _context is null ) return;
			if ( _context.IsActive && _context.Recording == _recording ) _context.State = _state;
			_context = null;
		}
	}

	/// <summary>
	/// Drawing properties saved by Scope().
	/// </summary>
	internal struct State
	{
		public float Opacity = 1f;
		public BlendMode OverrideBlendMode = BlendMode.Normal;
		public Matrix Transform = Matrix.Identity;

		/// <summary>
		/// Last clip in the command buffer's clip chain, or -1 for no drawing clip.
		/// </summary>
		public int ClipIndex = -1;

		/// <summary>
		/// Whether the transform preserves area. Singular transforms cannot produce shadow clips.
		/// </summary>
		public readonly bool HasArea => (double)Transform.M11 * Transform.M22 != (double)Transform.M12 * Transform.M21;

		public Stroke Stroke;
		public Vector4 FillInsets;
		public Texture FillMask;
		public Rect FillMaskRect;
		public Fill Fill;
		public TextStyle TextStyle = TextStyle.Default;

		public State() { }
	}
}
