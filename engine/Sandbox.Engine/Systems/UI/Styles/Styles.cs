using Sandbox.Engine;

namespace Sandbox.UI;

/// <summary>
/// Represents all supported CSS properties and their currently assigned values.
/// </summary>
[SkipHotload]
public partial class Styles : BaseStyles
{
	internal Dictionary<string, IStyleBlock.StyleProperty> RawValues = new Dictionary<string, IStyleBlock.StyleProperty>( StringComparer.OrdinalIgnoreCase );
	internal GradientInfo TextGradient;

	/// <summary>
	/// Whether this style sheet has any transitions that would need to be run.
	/// </summary>
	public bool HasTransitions => Transitions != null && Transitions.List.Count > 0;

	/// <summary>
	/// List of transitions this style sheet has.
	/// </summary>
	public TransitionList Transitions;

	//
	// Shadows
	//
	public ShadowList BoxShadow = new ShadowList();
	public ShadowList TextShadow = new ShadowList();
	public ShadowList FilterDropShadow = new ShadowList();

	public Styles()
	{

	}

	public override void Dirty()
	{
		// Nothing
	}

	public Length? Padding
	{
		set
		{
			PaddingLeft = value;
			PaddingTop = value;
			PaddingRight = value;
			PaddingBottom = value;
		}
	}

	public Length? Margin
	{
		set
		{
			MarginLeft = value;
			MarginTop = value;
			MarginRight = value;
			MarginBottom = value;
		}
	}

	public Length? BorderWidth
	{
		set
		{
			BorderLeftWidth = value;
			BorderRightWidth = value;
			BorderTopWidth = value;
			BorderBottomWidth = value;
		}
	}

	public Color? BorderColor
	{
		set
		{
			BorderLeftColor = value;
			BorderRightColor = value;
			BorderTopColor = value;
			BorderBottomColor = value;
		}
	}

	public bool HasBorder
	{
		get
		{
			if ( BorderTopWidth.HasValue && BorderTopWidth.Value.Value > 0 ) return true;
			if ( BorderRightWidth.HasValue && BorderRightWidth.Value.Value > 0 ) return true;
			if ( BorderBottomWidth.HasValue && BorderBottomWidth.Value.Value > 0 ) return true;
			if ( BorderLeftWidth.HasValue && BorderLeftWidth.Value.Value > 0 ) return true;

			return false;
		}
	}


	public Margin GetInset( Vector2 size )
	{
		var border = Sandbox.UI.Margin.GetEdges( size, BorderLeftWidth, BorderTopWidth, BorderRightWidth, BorderBottomWidth );
		var padding = Sandbox.UI.Margin.GetEdges( size, PaddingLeft, PaddingTop, PaddingRight, PaddingBottom );

		return border + padding;
	}

	public Margin GetOutset( Vector2 size ) => Sandbox.UI.Margin.GetEdges( size, MarginLeft, MarginTop, MarginRight, MarginBottom );

	public readonly static Styles Default = new Styles
	{
		Padding = 0
	};

	internal bool SetInternal( string styles, string filename, int lineoffset )
	{
		bool success = false;

		Parse p = new( styles, filename, lineoffset );

		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines();
			var property = p.ReadUntilOrEnd( " :;" );
			property = StyleParser.GetPropertyFromAlias( property );

			p = p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd )
				break;

			if ( p.Current != ':' )
				throw new System.Exception( $"Error parsing style: {styles}" );

			p.Pointer++;

			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd )
				throw new System.Exception( $"Error parsing style: {styles}" );

			var line = p.CurrentLine;
			var value = p.ReadUntilOrEnd( ";" );
			p.Pointer++;

			bool wasSuccessful = Set( property, value );
			if ( !wasSuccessful )
			{
				Log.Error( $"{value} is not valid with {property} {p.FileAndLine}" );
			}

			var prop = new IStyleBlock.StyleProperty
			{
				Name = property,
				Value = value,
				OriginalValue = value,
				Line = line,
				IsValid = wasSuccessful
			};

			RawValues[property] = prop;
			success = wasSuccessful || success;

			p = p.SkipWhitespaceAndNewlines();
		}

		return success;
	}

	public bool Set( string styles )
	{
		return SetInternal( styles, null, 0 );
	}

	/// <summary>
	/// Creates a matrix based on this style's "transform" and other related properties
	/// </summary>
	public Matrix BuildTransformMatrix( Vector2 size )
	{
		if ( Transform?.IsEmpty() ?? true )
			return Matrix.Identity;

		var t = Transform.Value;

		Vector2 perspectiveCenter = new Vector2( size.x * 0.5f, size.y * 0.5f );
		Vector2 perspectiveOrigin = perspectiveCenter;

		if ( PerspectiveOriginX.HasValue )
		{
			perspectiveOrigin.x = PerspectiveOriginX.Value.GetPixels( size.x );
		}

		if ( PerspectiveOriginY.HasValue )
		{
			perspectiveOrigin.y = PerspectiveOriginY.Value.GetPixels( size.y );
		}

		return t.BuildTransform( size.x, size.y, perspectiveOrigin - perspectiveCenter );
	}

	/// <summary>
	/// Try to find all panels using this style and mark them dirty so they'll
	/// redraw with the style. This should be called when the style is changed. Which
	/// is only technically when done via the editor.
	/// </summary>
	internal void MarkPanelsDirty()
	{
		foreach ( var panel in GlobalContext.Current.UISystem.RootPanels )
		{
			// Dirty all panels that contain this style
			panel.DirtyStylesWithStyle( this, true );
		}
	}

	public override void LerpProperty( string name, BaseStyles from, BaseStyles to, float delta )
	{
		base.LerpProperty( name, from, to, delta );

		//
		// Properties that aren't auto-generated (i.e. ones not defined in BaseStyles, but are defined in this class)
		//
		if ( from is Styles a && to is Styles b )
		{
			switch ( name )
			{
				case "box-shadow":
					if ( a.BoxShadow != b.BoxShadow )
						BoxShadow.SetFromLerp( a.BoxShadow, b.BoxShadow, delta );
					break;

				case "text-shadow":
					if ( a.TextShadow != b.TextShadow )
						TextShadow.SetFromLerp( a.TextShadow, b.TextShadow, delta );
					break;

				case "filter-drop-shadow":
					if ( a.FilterDropShadow != b.FilterDropShadow )
						FilterDropShadow.SetFromLerp( a.FilterDropShadow, b.FilterDropShadow, delta );
					break;

				case "padding":
					LerpProperty( "padding-left", from, to, delta );
					LerpProperty( "padding-right", from, to, delta );
					LerpProperty( "padding-bottom", from, to, delta );
					LerpProperty( "padding-top", from, to, delta );
					break;

				case "margin":
					LerpProperty( "margin-left", from, to, delta );
					LerpProperty( "margin-right", from, to, delta );
					LerpProperty( "margin-bottom", from, to, delta );
					LerpProperty( "margin-top", from, to, delta );
					break;

				case "border-color":
					LerpProperty( "border-color-left", from, to, delta );
					LerpProperty( "border-color-right", from, to, delta );
					LerpProperty( "border-color-bottom", from, to, delta );
					LerpProperty( "border-color-top", from, to, delta );
					break;

				case "border-width":
					LerpProperty( "border-width-left", from, to, delta );
					LerpProperty( "border-width-right", from, to, delta );
					LerpProperty( "border-width-bottom", from, to, delta );
					LerpProperty( "border-width-top", from, to, delta );
					break;

				case "border-radius":
					LerpProperty( "border-top-left-radius", from, to, delta );
					LerpProperty( "border-top-right-radius", from, to, delta );
					LerpProperty( "border-bottom-right-radius", from, to, delta );
					LerpProperty( "border-bottom-left-radius", from, to, delta );
					break;
			}
		}
	}

	public override void FromLerp( BaseStyles from, BaseStyles to, float delta )
	{
		base.FromLerp( from, to, delta );

		//
		// Shadows
		//
		if ( from is Styles a && to is Styles b )
		{
			if ( a.BoxShadow != b.BoxShadow )
				BoxShadow.SetFromLerp( a.BoxShadow, b.BoxShadow, delta );

			if ( a.TextShadow != b.TextShadow )
				TextShadow.SetFromLerp( a.TextShadow, b.TextShadow, delta );

			if ( a.FilterDropShadow != b.FilterDropShadow )
				FilterDropShadow.SetFromLerp( a.FilterDropShadow, b.FilterDropShadow, delta );
		}
	}
}

