namespace Sandbox.UI
{
	public partial class Styles
	{
		public override void Add( BaseStyles bs )
		{
			base.Add( bs );

			if ( bs is not Styles a )
				return;

			if ( a.HasTransitions )
			{
				if ( Transitions == null )
					Transitions = new TransitionList();

				Transitions.AddTransitions( a.Transitions );
			}

			BoxShadow.AddFrom( a.BoxShadow );
			TextShadow.AddFrom( a.TextShadow );
			FilterDropShadow.AddFrom( a.FilterDropShadow );

			if ( !a.TextGradient.ColorOffsets.IsDefaultOrEmpty )
			{
				TextGradient = a.TextGradient;
			}

			// The gradient is part of the background-image property, so any rule that
			// set background-image - url(), none, or a gradient - carries its gradient
			// state (possibly empty) over ours. A later url() must clear an earlier gradient.
			if ( a._backgroundImage != null || !a.BackgroundGradient.ColorOffsets.IsDefaultOrEmpty )
			{
				BackgroundGradient = a.BackgroundGradient;
			}
		}

		public override void From( BaseStyles bs )
		{
			base.From( bs );

			if ( bs is not Styles a ) return;

			CopyShadows( bs );

			Transitions?.Clear();

			if ( a.HasTransitions )
			{
				Transitions ??= new TransitionList();

				Transitions.AddTransitions( a.Transitions );
			}

			TextGradient = a.TextGradient;
			BackgroundGradient = a.BackgroundGradient;
		}

		internal void CopyShadows( BaseStyles bs )
		{
			if ( bs is not Styles a )
				return;

			BoxShadow.Clear();
			BoxShadow.IsNone = a.BoxShadow.IsNone;
			BoxShadow.AddRange( a.BoxShadow );

			TextShadow.Clear();
			TextShadow.IsNone = a.TextShadow.IsNone;
			TextShadow.AddRange( a.TextShadow );

			FilterDropShadow.Clear();
			FilterDropShadow.IsNone = a.FilterDropShadow.IsNone;
			FilterDropShadow.AddRange( a.FilterDropShadow );
		}
	}
}
