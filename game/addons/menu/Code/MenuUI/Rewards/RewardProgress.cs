using Sandbox;

namespace MenuProject;

/// <summary>
/// Progress maths for reward drops, shared by the rail entry and the rewards page - both
/// draw the same number, one as a bar and one as an arc.
/// </summary>
public static class RewardProgress
{
	extension( RewardFacet facet )
	{
		/// <summary>How far this objective has come, 0 to 1.</summary>
		public float Progress
		{
			get
			{
				if ( facet.Met ) return 1f;
				if ( facet.Required <= 0 ) return 0f;

				return (float)Math.Clamp( facet.Current / facet.Required, 0, 1 );
			}
		}
	}

	extension( RewardWindow window )
	{
		/// <summary>How many objectives are met.</summary>
		public int MetCount => window.Facets.Count( x => x.Met );

		/// <summary>Every objective is met.</summary>
		public bool IsComplete => window.Facets.Length > 0 && window.MetCount == window.Facets.Length;

		/// <summary>
		/// Every objective averaged - one number stands in for all of them. A window with no
		/// objectives is however eligible it says it is.
		/// </summary>
		public float Progress
		{
			get
			{
				if ( window.Facets.Length == 0 ) return window.IsEligible ? 1f : 0f;

				return window.Facets.Sum( x => x.Progress ) / window.Facets.Length;
			}
		}
	}
}
