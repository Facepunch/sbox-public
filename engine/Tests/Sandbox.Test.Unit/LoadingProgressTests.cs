using Sandbox.Menu;

[TestClass]
public class LoadingProgressTests
{
	[TestMethod]
	public void ProgressChangesInvalidatePanelHashes()
	{
		var before = new LoadingProgress { Title = "Downloading game", Fraction = 0.1, Mbps = 12, TotalSize = 1000 };
		var after = before;
		after.Fraction = 0.6;
		Assert.AreNotEqual( before.GetHashCode(), after.GetHashCode() );
		after = before;
		after.Mbps = 30;
		Assert.AreNotEqual( before.GetHashCode(), after.GetHashCode() );
		after = before;
		after.TotalSize = 2000;
		Assert.AreNotEqual( before.GetHashCode(), after.GetHashCode() );
		Assert.AreEqual( before.GetHashCode(), ((LoadingProgress?)before).GetHashCode() );
	}
}
