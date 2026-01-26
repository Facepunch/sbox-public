using Sandbox.UI;

namespace UITest;

[TestClass]
public class StyleSetting
{
	[TestMethod]
	public void SetWidth()
	{
		Styles styles = new Styles();
		styles.Set( "width", "140px" );

		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 140, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		styles = new Styles();
		styles.Set( "width: 140px" );

		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 140, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		styles = new Styles();
		styles.Set( " width : 140px ; " );

		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 140, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );
	}

	[TestMethod]
	public void SetHeight()
	{
		Styles styles = new Styles();
		styles.Set( "height", "140px" );

		Assert.IsTrue( styles.Height.HasValue );
		Assert.AreEqual( 140, styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Height.Value.Unit );
	}

	[TestMethod]
	public void SetDisplay()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "display", "flex" ) );
			Assert.AreEqual( DisplayMode.Flex, styles.Display );
		}

		{
			Styles styles = new Styles();
			Assert.IsFalse( styles.Set( "display", "bullshit" ) );
			Assert.AreEqual( null, styles.Display );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "display", "none" ) );
			Assert.AreEqual( DisplayMode.None, styles.Display );
		}
	}

	[TestMethod]
	public void SetPosition()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "position", "absolute" ) );
			Assert.AreEqual( PositionMode.Absolute, styles.Position );
		}

		{
			Styles styles = new Styles();
			Assert.IsFalse( styles.Set( "position", "bullshit" ) );
			Assert.AreEqual( null, styles.Position );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "position", "relative" ) );
			Assert.AreEqual( PositionMode.Relative, styles.Position );
		}
	}

	[TestMethod]
	public void SetOverflow()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "overflow", "visible" ) );
			Assert.AreEqual( OverflowMode.Visible, styles.Overflow );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "overflow", "hidden" ) );
			Assert.AreEqual( OverflowMode.Hidden, styles.Overflow );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "overflow", "scroll" ) );
			Assert.AreEqual( OverflowMode.Scroll, styles.Overflow );
		}
	}

	[TestMethod]
	public void SetFlexGrow()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-grow", "1" ) );
			Assert.AreEqual( 1.0f, styles.FlexGrow );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-grow", "2" ) );
			Assert.AreEqual( 2.0f, styles.FlexGrow );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-grow", "0.5" ) );
			Assert.AreEqual( 0.5f, styles.FlexGrow );
		}

	}

	[TestMethod]
	public void SetFlexShrink()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-shrink", "1" ) );
			Assert.AreEqual( 1.0f, styles.FlexShrink );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-shrink", "2" ) );
			Assert.AreEqual( 2.0f, styles.FlexShrink );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-shrink", "0.5" ) );
			Assert.AreEqual( 0.5f, styles.FlexShrink );
		}

	}

	[TestMethod]
	public void SetFlexWrap()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-wrap", "wrap" ) );
			Assert.AreEqual( Wrap.Wrap, styles.FlexWrap );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-wrap", "nowrap" ) );
			Assert.AreEqual( Wrap.NoWrap, styles.FlexWrap );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-wrap", "wrap-reverse" ) );
			Assert.AreEqual( Wrap.WrapReverse, styles.FlexWrap );
		}
	}

	[TestMethod]
	public void SetJustifyContent()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "center" ) );
			Assert.AreEqual( Justify.Center, styles.JustifyContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "flex-end" ) );
			Assert.AreEqual( Justify.FlexEnd, styles.JustifyContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "flex-start" ) );
			Assert.AreEqual( Justify.FlexStart, styles.JustifyContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "space-around" ) );
			Assert.AreEqual( Justify.SpaceAround, styles.JustifyContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "space-between" ) );
			Assert.AreEqual( Justify.SpaceBetween, styles.JustifyContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "justify-content", "space-evenly" ) );
			Assert.AreEqual( Justify.SpaceEvenly, styles.JustifyContent );
		}
	}

	[TestMethod]
	public void SetAlignContent()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "auto" ) );
			Assert.AreEqual( Align.Auto, styles.AlignContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "flex-start" ) );
			Assert.AreEqual( Align.FlexStart, styles.AlignContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "flex-end" ) );
			Assert.AreEqual( Align.FlexEnd, styles.AlignContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "stretch" ) );
			Assert.AreEqual( Align.Stretch, styles.AlignContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "space-between" ) );
			Assert.AreEqual( Align.SpaceBetween, styles.AlignContent );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-content", "space-around" ) );
			Assert.AreEqual( Align.SpaceAround, styles.AlignContent );
		}
	}

	[TestMethod]
	public void SetAlignItems()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-items", "auto" ) );
			Assert.AreEqual( Align.Auto, styles.AlignItems );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-items", "flex-start" ) );
			Assert.AreEqual( Align.FlexStart, styles.AlignItems );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-items", "flex-end" ) );
			Assert.AreEqual( Align.FlexEnd, styles.AlignItems );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-items", "center" ) );
			Assert.AreEqual( Align.Center, styles.AlignItems );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-items", "baseline" ) );
			Assert.AreEqual( Align.Baseline, styles.AlignItems );
		}
	}


	[TestMethod]
	public void SetAlignSelf()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-self", "auto" ) );
			Assert.AreEqual( Align.Auto, styles.AlignSelf );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-self", "flex-start" ) );
			Assert.AreEqual( Align.FlexStart, styles.AlignSelf );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-self", "flex-end" ) );
			Assert.AreEqual( Align.FlexEnd, styles.AlignSelf );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-self", "center" ) );
			Assert.AreEqual( Align.Center, styles.AlignSelf );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "align-self", "baseline" ) );
			Assert.AreEqual( Align.Baseline, styles.AlignSelf );
		}
	}

	[TestMethod]
	public void SetAspectRatio()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "aspect-ratio", "1" ) );
			Assert.AreEqual( 1.0f, styles.AspectRatio );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "aspect-ratio", "2" ) );
			Assert.AreEqual( 2.0f, styles.AspectRatio );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "aspect-ratio", "0.1" ) );
			Assert.AreEqual( 0.1f, styles.AspectRatio );
		}
	}

	[TestMethod]
	public void SetFlexDirection()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-direction", "row" ) );
			Assert.AreEqual( FlexDirection.Row, styles.FlexDirection );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-direction", "column" ) );
			Assert.AreEqual( FlexDirection.Column, styles.FlexDirection );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-direction", "row-reverse" ) );
			Assert.AreEqual( FlexDirection.RowReverse, styles.FlexDirection );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "flex-direction", "column-reverse" ) );
			Assert.AreEqual( FlexDirection.ColumnReverse, styles.FlexDirection );
		}
	}

	[TestMethod]
	public void SetPadding()
	{
		Styles styles = new Styles();
		styles.Set( "padding", "10px" );

		Assert.IsTrue( styles.PaddingLeft.HasValue );
		Assert.IsTrue( styles.PaddingRight.HasValue );
		Assert.IsTrue( styles.PaddingTop.HasValue );
		Assert.IsTrue( styles.PaddingBottom.HasValue );

		Assert.AreEqual( 10, styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 10, styles.PaddingRight.Value.Value );
		Assert.AreEqual( 10, styles.PaddingTop.Value.Value );
		Assert.AreEqual( 10, styles.PaddingBottom.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingLeft.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingBottom.Value.Unit );

		styles = new Styles();
		styles.Set( "padding", "10px 20px" );

		Assert.AreEqual( 20, styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 20, styles.PaddingRight.Value.Value );
		Assert.AreEqual( 10, styles.PaddingTop.Value.Value );
		Assert.AreEqual( 10, styles.PaddingBottom.Value.Value );

		styles = new Styles();
		styles.Set( "padding", "10px 20px 5px" );

		Assert.AreEqual( 20, styles.PaddingLeft.Value.Value );
		Assert.AreEqual( 20, styles.PaddingRight.Value.Value );
		Assert.AreEqual( 10, styles.PaddingTop.Value.Value );
		Assert.AreEqual( 5, styles.PaddingBottom.Value.Value );

		styles = new Styles();
		styles.Set( "padding", "1px 2px 3px 4px" );

		Assert.AreEqual( 1, styles.PaddingTop.Value.Value );
		Assert.AreEqual( 2, styles.PaddingRight.Value.Value );
		Assert.AreEqual( 3, styles.PaddingBottom.Value.Value );
		Assert.AreEqual( 4, styles.PaddingLeft.Value.Value );

		styles = new Styles();
		styles.Set( "padding", "0 2px 0 4px" );

		Assert.AreEqual( 0, styles.PaddingTop.Value.Value );
		Assert.AreEqual( 2, styles.PaddingRight.Value.Value );
		Assert.AreEqual( 0, styles.PaddingBottom.Value.Value );
		Assert.AreEqual( 4, styles.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void SetPaddingLeft()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "padding-left", "10px" ) );
		Assert.IsTrue( styles.PaddingLeft.HasValue );
		Assert.AreEqual( 10, styles.PaddingLeft.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingLeft.Value.Unit );
	}

	[TestMethod]
	public void SetPaddingTop()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "padding-top", "10px" ) );
		Assert.IsTrue( styles.PaddingTop.HasValue );
		Assert.AreEqual( 10, styles.PaddingTop.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingTop.Value.Unit );
	}

	[TestMethod]
	public void SetPaddingRight()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "padding-right", "10px" ) );
		Assert.IsTrue( styles.PaddingRight.HasValue );
		Assert.AreEqual( 10, styles.PaddingRight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingRight.Value.Unit );
	}

	[TestMethod]
	public void SetPaddingBottom()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "padding-bottom", "10px" ) );
		Assert.IsTrue( styles.PaddingBottom.HasValue );
		Assert.AreEqual( 10, styles.PaddingBottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.PaddingBottom.Value.Unit );
	}


	[TestMethod]
	public void SetMargin()
	{
		Styles styles = new Styles();
		styles.Set( "margin", "10px" );

		Assert.IsTrue( styles.MarginLeft.HasValue );
		Assert.IsTrue( styles.MarginRight.HasValue );
		Assert.IsTrue( styles.MarginTop.HasValue );
		Assert.IsTrue( styles.MarginBottom.HasValue );

		Assert.AreEqual( 10, styles.MarginLeft.Value.Value );
		Assert.AreEqual( 10, styles.MarginRight.Value.Value );
		Assert.AreEqual( 10, styles.MarginTop.Value.Value );
		Assert.AreEqual( 10, styles.MarginBottom.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, styles.MarginLeft.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginBottom.Value.Unit );

		styles = new Styles();
		styles.Set( "margin", "10px 20px" );

		Assert.AreEqual( 20, styles.MarginLeft.Value.Value );
		Assert.AreEqual( 20, styles.MarginRight.Value.Value );
		Assert.AreEqual( 10, styles.MarginTop.Value.Value );
		Assert.AreEqual( 10, styles.MarginBottom.Value.Value );

		styles = new Styles();
		styles.Set( "margin", "10px 20px 30px" );

		Assert.AreEqual( 20, styles.MarginLeft.Value.Value );
		Assert.AreEqual( 20, styles.MarginRight.Value.Value );
		Assert.AreEqual( 10, styles.MarginTop.Value.Value );
		Assert.AreEqual( 30, styles.MarginBottom.Value.Value );

		styles = new Styles();
		styles.Set( "margin", "10px 20px 30px 40px" );

		Assert.AreEqual( 10, styles.MarginTop.Value.Value );
		Assert.AreEqual( 20, styles.MarginRight.Value.Value );
		Assert.AreEqual( 30, styles.MarginBottom.Value.Value );
		Assert.AreEqual( 40, styles.MarginLeft.Value.Value );
	}

	[TestMethod]
	public void SetMarginLeft()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "margin-left", "10px" ) );
		Assert.IsTrue( styles.MarginLeft.HasValue );
		Assert.AreEqual( 10, styles.MarginLeft.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginLeft.Value.Unit );
	}

	[TestMethod]
	public void SetMarginTop()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "margin-top", "10px" ) );
		Assert.IsTrue( styles.MarginTop.HasValue );
		Assert.AreEqual( 10, styles.MarginTop.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginTop.Value.Unit );
	}

	[TestMethod]
	public void SetMarginRight()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "margin-right", "10px" ) );
		Assert.IsTrue( styles.MarginRight.HasValue );
		Assert.AreEqual( 10, styles.MarginRight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginRight.Value.Unit );
	}

	[TestMethod]
	public void SetMarginBottom()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "margin-bottom", "10px" ) );
		Assert.IsTrue( styles.MarginBottom.HasValue );
		Assert.AreEqual( 10, styles.MarginBottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.MarginBottom.Value.Unit );
	}

	[TestMethod]
	public void SetBackgroundColor()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "background-color", "#f0f0" ) );

		Assert.IsTrue( styles.BackgroundColor.HasValue );

		Assert.AreEqual( 1.0f, styles.BackgroundColor.Value.r );
		Assert.AreEqual( 0.0f, styles.BackgroundColor.Value.g );
		Assert.AreEqual( 1.0f, styles.BackgroundColor.Value.b );
		Assert.AreEqual( 0.0f, styles.BackgroundColor.Value.a );
	}

	[TestMethod]
	public void SetColor()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "color", "#f0f0" ) );

		Assert.IsTrue( styles.FontColor.HasValue );

		Assert.AreEqual( 1.0f, styles.FontColor.Value.r );
		Assert.AreEqual( 0.0f, styles.FontColor.Value.g );
		Assert.AreEqual( 1.0f, styles.FontColor.Value.b );
		Assert.AreEqual( 0.0f, styles.FontColor.Value.a );
	}

	[TestMethod]
	public void SetFontSize()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "font-size", "20px" ) );

		Assert.IsTrue( styles.FontSize.HasValue );

		Assert.AreEqual( 20, styles.FontSize.Value );
	}

	[TestMethod]
	public void SetFontWeight()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "font-weight", "500" ) );

		Assert.IsTrue( styles.FontWeight.HasValue );
		Assert.AreEqual( 500, styles.FontWeight.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "font-weight", "normal" ) );

		Assert.IsTrue( styles.FontWeight.HasValue );
		Assert.AreEqual( 400, styles.FontWeight.Value );
	}

	[TestMethod]
	public void SetTransition()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "transition", "width 2s ease-in-out .5s" ) );

		Assert.IsNotNull( styles.Transitions );
		Assert.AreEqual( 1, styles.Transitions.List.Count );
		Assert.AreEqual( "width", styles.Transitions.List[0].Property );
		Assert.AreEqual( 2000, styles.Transitions.List[0].Duration );
		Assert.AreEqual( "ease-in-out", styles.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 500, styles.Transitions.List[0].Delay );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "transition", "width 10ms" ) );

		Assert.IsNotNull( styles.Transitions );
		Assert.AreEqual( 1, styles.Transitions.List.Count );
		Assert.AreEqual( "width", styles.Transitions.List[0].Property );
		Assert.AreEqual( 10, styles.Transitions.List[0].Duration );
		// Each property has an initial value, defined in the property’s definition table
		// https://w3c.github.io/csswg-drafts/css-transitions/#transition-timing-function-property
		Assert.AreEqual( "ease", styles.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 0, styles.Transitions.List[0].Delay.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "transition", "width 10ms 2s ease-out, height 2s ease-in" ) );

		Assert.IsNotNull( styles.Transitions );
		Assert.AreEqual( 2, styles.Transitions.List.Count );
		Assert.AreEqual( "width", styles.Transitions.List[0].Property );
		Assert.AreEqual( 10, styles.Transitions.List[0].Duration );
		Assert.AreEqual( "ease-out", styles.Transitions.List[0].TimingFunction );
		Assert.AreEqual( 2000, styles.Transitions.List[0].Delay );
		Assert.AreEqual( "height", styles.Transitions.List[1].Property );
		Assert.AreEqual( 2000, styles.Transitions.List[1].Duration );
		Assert.AreEqual( 0, styles.Transitions.List[1].Delay.Value );
		Assert.AreEqual( "ease-in", styles.Transitions.List[1].TimingFunction );
	}

	[TestMethod]
	public void SetBorderRadius()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-radius", "123px" ) );
		Assert.IsTrue( styles.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderTopRightRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, styles.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopLeftRadius.Value.Unit );
		Assert.AreEqual( 123, styles.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopRightRadius.Value.Unit );
		Assert.AreEqual( 123, styles.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomLeftRadius.Value.Unit );
		Assert.AreEqual( 123, styles.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomRightRadius.Value.Unit );


		styles = new Styles();
		Assert.IsTrue( styles.Set( "border-radius", "10px 20px 30px 40px" ) );
		Assert.IsTrue( styles.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderTopRightRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 10, styles.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopLeftRadius.Value.Unit );
		Assert.AreEqual( 20, styles.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopRightRadius.Value.Unit );
		Assert.AreEqual( 30, styles.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomRightRadius.Value.Unit );
		Assert.AreEqual( 40, styles.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomLeftRadius.Value.Unit );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border-radius", "10px 20px" ) );
		Assert.IsTrue( styles.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderTopRightRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 10, styles.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopLeftRadius.Value.Unit );
		Assert.AreEqual( 20, styles.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopRightRadius.Value.Unit );
		Assert.AreEqual( 10, styles.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomRightRadius.Value.Unit );
		Assert.AreEqual( 20, styles.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomLeftRadius.Value.Unit );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border-radius", "10px 0 30px 40px" ) );
		Assert.IsTrue( styles.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderTopRightRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 10, styles.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopLeftRadius.Value.Unit );
		Assert.AreEqual( 0, styles.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopRightRadius.Value.Unit );
		Assert.AreEqual( 30, styles.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomRightRadius.Value.Unit );
		Assert.AreEqual( 40, styles.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void SetBorderTopLeftRadius()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-top-left-radius", "123px" ) );
		Assert.IsTrue( styles.BorderTopLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderTopRightRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, styles.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void SetBorderTopRightRadius()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-top-right-radius", "123px" ) );
		Assert.IsFalse( styles.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderTopRightRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, styles.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopRightRadius.Value.Unit );
	}

	[TestMethod]
	public void SetBorderBottomLeftRadius()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-bottom-left-radius", "123px" ) );
		Assert.IsFalse( styles.BorderTopLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderTopRightRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, styles.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void SetBorderBottomRightRadius()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-bottom-right-radius", "123px" ) );
		Assert.IsFalse( styles.BorderTopLeftRadius.HasValue );
		Assert.IsFalse( styles.BorderTopRightRadius.HasValue );
		Assert.IsFalse( styles.BorderBottomLeftRadius.HasValue );
		Assert.IsTrue( styles.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, styles.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomRightRadius.Value.Unit );
	}

	[TestMethod]
	public void SetBorderLeftWidth()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-left-width", "10px" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderLeftWidth.Value.Unit );
	}

	[TestMethod]
	public void SetBorderRightWidth()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-right-width", "10px" ) );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderRightWidth.Value.Unit );
	}

	[TestMethod]
	public void SetBorderTopWidth()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-top-width", "10px" ) );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderTopWidth.Value.Unit );
	}

	[TestMethod]
	public void SetBorderImage()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-image", "url( /ui/border.png ) 50 fill stretch" ) );
	}

	[TestMethod]
	public void SetBorderBottomWidth()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-bottom-width", "10px" ) );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.BorderBottomWidth.Value.Unit );
	}

	[TestMethod]
	public void SetBorder()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px solid red" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderBottomColor.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "solid 10px red" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderBottomColor.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px red" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderBottomColor.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.IsFalse( styles.BorderLeftColor.HasValue );
		Assert.IsFalse( styles.BorderTopColor.HasValue );
		Assert.IsFalse( styles.BorderRightColor.HasValue );
		Assert.IsFalse( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "red" ) );
		Assert.IsFalse( styles.BorderLeftWidth.HasValue );
		Assert.IsFalse( styles.BorderTopWidth.HasValue );
		Assert.IsFalse( styles.BorderRightWidth.HasValue );
		Assert.IsFalse( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderBottomColor.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px solid rgba( 0, 0, 0, 0.1 )" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, styles.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), styles.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), styles.BorderTopColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), styles.BorderRightColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), styles.BorderBottomColor.Value );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px rgba( 0, 0, 0, 0.1 )" ) );
		Assert.IsTrue( styles.Set( "border", "none" ) );
		Assert.AreEqual( 0.0f, styles.BorderLeftWidth );
		Assert.AreEqual( 0.0f, styles.BorderTopWidth );
		Assert.AreEqual( 0.0f, styles.BorderRightWidth );
		Assert.AreEqual( 0.0f, styles.BorderBottomWidth );

		styles = new Styles();
		Assert.IsTrue( styles.Set( "border", "10px rgba( 0, 0, 0, 0.1 )" ) );
		Assert.IsTrue( styles.Set( "border", "0" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsTrue( styles.BorderTopWidth.HasValue );
		Assert.IsTrue( styles.BorderRightWidth.HasValue );
		Assert.IsTrue( styles.BorderBottomWidth.HasValue );
		Assert.AreEqual( 0, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 0, styles.BorderTopWidth.Value.Value );
		Assert.AreEqual( 0, styles.BorderRightWidth.Value.Value );
		Assert.AreEqual( 0, styles.BorderBottomWidth.Value.Value );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsTrue( styles.BorderTopColor.HasValue );
		Assert.IsTrue( styles.BorderRightColor.HasValue );
		Assert.IsTrue( styles.BorderBottomColor.HasValue );
	}

	[TestMethod]
	public void SetBorderLeft()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "border-left", "10px solid red" ) );
		Assert.IsTrue( styles.BorderLeftWidth.HasValue );
		Assert.IsFalse( styles.BorderTopWidth.HasValue );
		Assert.IsFalse( styles.BorderRightWidth.HasValue );
		Assert.IsFalse( styles.BorderBottomWidth.HasValue );
		Assert.IsTrue( styles.BorderLeftColor.HasValue );
		Assert.IsFalse( styles.BorderTopColor.HasValue );
		Assert.IsFalse( styles.BorderRightColor.HasValue );
		Assert.IsFalse( styles.BorderBottomColor.HasValue );
		Assert.AreEqual( 10, styles.BorderLeftWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BorderLeftColor.Value );
	}

	[TestMethod]
	public void SetBoxShadow()
	{
		Styles styles = new Styles();
		styles.Set( "box-shadow", "10px 20px red" );

		Assert.AreEqual( 1, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );

		styles = new Styles();
		styles.Set( "box-shadow", "10px 20px 30px red" );

		Assert.AreEqual( 1, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, styles.BoxShadow[0].Blur );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );

		styles = new Styles();
		styles.Set( "box-shadow", "10px 20px 30px 40px red" );

		Assert.AreEqual( 1, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, styles.BoxShadow[0].Blur );
		Assert.AreEqual( 40, styles.BoxShadow[0].Spread );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );

		styles = new Styles();
		styles.Set( "box-shadow", "10px 0 30px 40px red" );

		Assert.AreEqual( 1, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 0, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, styles.BoxShadow[0].Blur );
		Assert.AreEqual( 40, styles.BoxShadow[0].Spread );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );

		styles.Set( "box-shadow", "10px 0 30px 40px red inset" );

		Assert.AreEqual( 1, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 0, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( 30, styles.BoxShadow[0].Blur );
		Assert.AreEqual( 40, styles.BoxShadow[0].Spread );
		Assert.AreEqual( true, styles.BoxShadow[0].Inset );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );
	}

	[TestMethod]
	public void SetBoxShadowMultiple()
	{
		Styles styles = new Styles();
		styles.Set( "box-shadow", "10px 20px red, 5px 10px black" );

		Assert.AreEqual( 2, styles.BoxShadow.Count );
		Assert.AreEqual( 10, styles.BoxShadow[0].OffsetX );
		Assert.AreEqual( 20, styles.BoxShadow[0].OffsetY );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), styles.BoxShadow[0].Color );

		Assert.AreEqual( 5, styles.BoxShadow[1].OffsetX );
		Assert.AreEqual( 10, styles.BoxShadow[1].OffsetY );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), styles.BoxShadow[1].Color );

		styles.Set( "box-shadow", "1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black, 1px 1px black" );

		Assert.AreEqual( 10, styles.BoxShadow.Count );
	}

	[TestMethod]
	public void SetCursor()
	{
		Styles styles = new Styles();
		styles.Set( "cursor", "boobs" );

		Assert.IsTrue( styles.Cursor != null );
		Assert.AreEqual( "boobs", styles.Cursor );
	}

	[TestMethod]
	public void SetPointerEvents()
	{
		Styles styles = new Styles();
		styles.Set( "pointer-events", "none" );

		Assert.IsTrue( styles.PointerEvents != null );
		Assert.AreEqual( PointerEvents.None, styles.PointerEvents );
	}

	[TestMethod]
	public void SetOpacity()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "opacity", "0.1" ) );

		Assert.IsTrue( styles.Opacity.HasValue );
		Assert.AreEqual( 0.1f, styles.Opacity.Value );
	}

	[TestMethod]
	public void SetTextDecoration()
	{
		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "text-decoration", "underline" ) );

			Assert.IsTrue( styles.TextDecorationLine.HasValue );
			Assert.AreEqual( TextDecoration.Underline, styles.TextDecorationLine.Value & TextDecoration.Underline );
			Assert.AreEqual( TextDecoration.None, styles.TextDecorationLine.Value & TextDecoration.LineThrough );
		}

		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "text-decoration", "line-through" ) );

			Assert.IsTrue( styles.TextDecorationLine.HasValue );
			Assert.AreEqual( TextDecoration.None, styles.TextDecorationLine.Value & TextDecoration.Underline );
			Assert.AreEqual( TextDecoration.LineThrough, styles.TextDecorationLine.Value & TextDecoration.LineThrough );
		}


		{
			Styles styles = new Styles();
			Assert.IsTrue( styles.Set( "text-decoration", "line-through underline" ) );

			Assert.IsTrue( styles.TextDecorationLine.HasValue );
			Assert.AreEqual( TextDecoration.Underline, styles.TextDecorationLine.Value & TextDecoration.Underline );
			Assert.AreEqual( TextDecoration.LineThrough, styles.TextDecorationLine.Value & TextDecoration.LineThrough );
		}
	}

	[TestMethod]
	public void SetFontStyle()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "font-style", "italic" ) );

		Assert.IsTrue( styles.FontStyle.HasValue );
		Assert.AreEqual( FontStyle.Italic, styles.FontStyle.Value & FontStyle.Italic );
	}

	[TestMethod]
	public void BackdropFilterBlur()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "blur( 100px )" ) );
		Assert.IsTrue( styles.BackdropFilterBlur.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterBlur.Value.Unit );
		Assert.AreEqual( 100.0f, styles.BackdropFilterBlur.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterBrightness()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "brightness( 100 )" ) );
		Assert.IsTrue( styles.BackdropFilterBrightness.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterBrightness.Value.Unit );
		Assert.AreEqual( 100.0f, styles.BackdropFilterBrightness.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterContrast()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "contrast( 100 )" ) );
		Assert.IsTrue( styles.BackdropFilterContrast.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterContrast.Value.Unit );
		Assert.AreEqual( 100.0f, styles.BackdropFilterContrast.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterSaturate()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "saturate( 100 )" ) );
		Assert.IsTrue( styles.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterSaturate.Value.Unit );
		Assert.AreEqual( 100.0f, styles.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterGrayscale()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "grayscale( 1 )" ) );
		Assert.IsTrue( styles.BackdropFilterSaturate.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterSaturate.Value.Unit );
		Assert.AreEqual( 0.0f, styles.BackdropFilterSaturate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterHueRotate()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "hue-rotate( 90deg )" ) );
		Assert.IsTrue( styles.BackdropFilterHueRotate.HasValue );
		Assert.AreEqual( LengthUnit.Pixels, styles.BackdropFilterHueRotate.Value.Unit );
		Assert.AreEqual( 90, styles.BackdropFilterHueRotate.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterInvert()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "invert(70%)" ) );
		Assert.IsTrue( styles.BackdropFilterInvert.HasValue );
		Assert.AreEqual( LengthUnit.Percentage, styles.BackdropFilterInvert.Value.Unit );
		Assert.AreEqual( 70.0f, styles.BackdropFilterInvert.Value.Value );
	}

	[TestMethod]
	public void BackdropFilterMultiple()
	{
		Styles styles = new Styles();
		Assert.IsTrue( styles.Set( "backdrop-filter", "blur( 100px ) invert(70%)" ) );

		Assert.IsTrue( styles.BackdropFilterInvert.HasValue );
		Assert.AreEqual( LengthUnit.Percentage, styles.BackdropFilterInvert.Value.Unit );
		Assert.AreEqual( 70.0f, styles.BackdropFilterInvert.Value.Value );

		Assert.IsTrue( styles.BackdropFilterInvert.HasValue );
		Assert.AreEqual( LengthUnit.Percentage, styles.BackdropFilterInvert.Value.Unit );
		Assert.AreEqual( 70.0f, styles.BackdropFilterInvert.Value.Value );
	}
}
