namespace Sandbox.UI
{
	public partial class Styles
	{
		public void ApplyScale( float scale )
		{
			Length.Scale( ref _fontsize, scale, true );
			Length.Scale( ref _textstrokewidth, scale, true );
			Length.Scale( ref _lineheight, scale, true );
			Length.Scale( ref _letterspacing, scale, true );
			Length.Scale( ref _width, scale );
			Length.Scale( ref _minwidth, scale );
			Length.Scale( ref _maxwidth, scale );

			Length.Scale( ref _left, scale );
			Length.Scale( ref _right, scale );
			Length.Scale( ref _top, scale );
			Length.Scale( ref _bottom, scale );

			Length.Scale( ref _height, scale );
			Length.Scale( ref _minheight, scale );
			Length.Scale( ref _maxheight, scale );

			Length.Scale( ref _flexbasis, scale, true );
			Length.Scale( ref _rowgap, scale );
			Length.Scale( ref _columngap, scale );

			Length.Scale( ref _marginleft, scale );
			Length.Scale( ref _margintop, scale );
			Length.Scale( ref _marginright, scale );
			Length.Scale( ref _marginbottom, scale );

			Length.Scale( ref _paddingleft, scale );
			Length.Scale( ref _paddingtop, scale );
			Length.Scale( ref _paddingright, scale );
			Length.Scale( ref _paddingbottom, scale );

			Length.Scale( ref _borderleftwidth, scale );
			Length.Scale( ref _bordertopwidth, scale );
			Length.Scale( ref _borderrightwidth, scale );
			Length.Scale( ref _borderbottomwidth, scale );

			Length.Scale( ref _bordertopleftradius, scale );
			Length.Scale( ref _bordertoprightradius, scale );
			Length.Scale( ref _borderbottomrightradius, scale );
			Length.Scale( ref _borderbottomleftradius, scale );

			Length.Scale( ref _outlinewidth, scale );
			Length.Scale( ref _outlineoffset, scale );

			Length.Scale( ref _transformoriginx, scale );
			Length.Scale( ref _transformoriginy, scale );
			Scale( ref _transform, scale );

			Scale( BoxShadow, scale );
			Scale( TextShadow, scale );
			Scale( FilterDropShadow, scale );
		}

		internal bool CalcVisible()
		{
			if ( Display.HasValue && Display.Value == DisplayMode.None ) return false;
			if ( Opacity <= 0.0f ) return false;

			return true;
		}

		private void Scale( ShadowList shadows, float amount )
		{
			if ( shadows == null ) return;

			for ( int i = 0; i < shadows.Count; i++ )
			{
				shadows[i] = shadows[i].Scale( amount );
			}
		}

		private void Scale( ref PanelTransform? tx, float amount )
		{
			if ( tx == null ) return;

			tx = tx.Value.GetScaled( amount );
		}
	}
}
