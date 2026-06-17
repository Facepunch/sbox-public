using System.Text.Json.Serialization;
namespace Sandbox.Services;

public class PackageReviewStats
{
	[JsonPropertyName( "p" )]
	public int PositiveRatings { get; set; }

	[JsonPropertyName( "n" )]
	public int NegativeRatings { get; set; }

	[JsonPropertyName( "o" )]
	public int PromiseRatings { get; set; }

	[JsonPropertyName( "pti" )]
	public Dictionary<int, int> PositiveTags { get; set; } = new();

	[JsonPropertyName( "nti" )]
	public Dictionary<int, int> NegativeTags { get; set; } = new();

	[JsonIgnore]
	public long Count => PositiveRatings + NegativeRatings + PromiseRatings;

	public float ToPercentage()
	{
		var count = Count;
		if ( count == 0 ) return 0;

		// Positives count as 100%, Promises 50%, Negatives 0%, averaged across all reviews.
		float score = (PositiveRatings * 100) + (PromiseRatings * 50);
		score /= count;
		return score;
	}
}
