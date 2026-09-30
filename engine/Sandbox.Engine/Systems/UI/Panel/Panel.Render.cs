using Sandbox.Engine;
using Sandbox.Rendering;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.UI;

public partial class Panel
{
	sealed class DrawCallbacks( bool hasCallback )
	{
		internal readonly bool HasCallback = hasCallback;
	}

	static readonly ConditionalWeakTable<Type, DrawCallbacks> _drawCallbacks = new();
	bool _hasDrawCallback;

	void UpdateDrawCallbacks()
	{
		_hasDrawCallback = _drawCallbacks.GetValue( GetType(), static type =>
		{
			for ( var current = type; current != typeof( Panel ); current = current.BaseType )
			{
				foreach ( var method in current.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly ) )
				{
					if ( method.Name == nameof( OnDraw ) && method.GetBaseDefinition().DeclaringType == typeof( Panel ) )
						return new DrawCallbacks( true );
				}
			}

			return new DrawCallbacks( false );
		} ).HasCallback;
	}

	internal Matrix RenderTransform => GlobalMatrixInverted ?? Matrix.Identity;
	internal float CachedRenderOpacity = 1.0f;
	internal BlendMode CachedOverrideBlendMode = BlendMode.Normal;

	/// <summary>
	/// Does nothing. Drawing is regenerated on every command-list build, so there is no dirty state to mark.
	/// </summary>
	[Obsolete( "Drawing is regenerated every frame. This does nothing." )]
	public void MarkRenderDirty()
	{
	}

	/// <summary>
	/// Legacy custom drawing hook. Prefer overriding <see cref="OnDraw(Painter)"/> and using its supplied painter.
	/// <example>
	/// <code>
	/// public override void OnDraw()
	/// {
	///     var r = Box.RectInner;
	///     Draw.Rect( r, Color.Blue.WithAlpha( 0.2f ), cornerRadius: 4 );
	///     Draw.Text( "Score: 100", r, 16, Color.White, TextFlag.Center );
	/// }
	/// </code>
	/// </example>
	/// </summary>
	[Obsolete( "Override OnDraw(Painter painter) and use the supplied Painter instead." )]
	public virtual void OnDraw()
	{
	}

	/// <summary>
	/// Draws this panel when its render command list is built. Coordinates start at (0, 0); painter.Bounds is the panel's size.
	/// The default implementation calls OnDraw().
	/// </summary>
#pragma warning disable CS0618 // Preserve dispatch to existing parameterless overrides.
	public virtual void OnDraw( Painter painter ) => OnDraw();
#pragma warning restore CS0618

	[Obsolete( "Override OnDraw(Painter painter) instead." )]
	public virtual void DrawContent( ref RenderState state )
	{
	}

	[Obsolete( "Override OnDraw(Painter painter) instead." )]
	public virtual void DrawBackground( ref RenderState state )
	{
	}

}
