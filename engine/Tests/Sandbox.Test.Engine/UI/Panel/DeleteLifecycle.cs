using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Modifies UI System Global + the shared panel clock
public partial class PanelDeleteTest
{
	/// <summary>
	/// Panel subclass that records how many times the OnDeleted hook fired.
	/// </summary>
	private sealed class RecordingPanel : Panel
	{
		public int DeletedCount { get; private set; }

		/// <summary>
		/// Counts hook invocations so tests can assert it fired exactly once.
		/// </summary>
		public override void OnDeleted()
		{
			DeletedCount++;
		}
	}

	/// <summary>
	/// Delete() only marks the panel as deleting (with the :outro pseudo class switched on) and
	/// queues it - the panel stays parented and valid until the deferred deletion pump runs,
	/// which removes it from the parent and fires OnDeleted exactly once.
	/// </summary>
	[TestMethod]
	public void DeferredDeleteMarksThenPumpRemoves()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new RecordingPanel { Parent = root };
		root.Layout();

		p.Delete();

		Assert.IsTrue( p.IsDeleting );
		Assert.IsTrue( p.HasOutro );
		Assert.IsTrue( p.IsValid );
		Assert.AreEqual( 1, root.Children.Count() );
		Assert.AreEqual( 0, p.DeletedCount );

		// A second Delete while one is pending is a no-op
		p.Delete();

		GlobalContext.Current.UISystem.RunDeferredDeletion();

		Assert.IsFalse( p.IsValid );
		Assert.AreEqual( 0, root.Children.Count() );
		Assert.AreEqual( 1, p.DeletedCount );
	}

	/// <summary>
	/// Delete( true ) skips the deferred queue entirely: the panel is unparented, hidden and
	/// torn down immediately, with OnDeleted firing during the call.
	/// </summary>
	[TestMethod]
	public void ImmediateDeleteRemovesInstantly()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var p = new RecordingPanel { Parent = root };
		root.Layout();

		p.Delete( true );

		Assert.IsTrue( p.IsDeleting );
		Assert.IsFalse( p.IsVisible );
		Assert.IsFalse( p.IsValid );
		Assert.AreEqual( 0, root.Children.Count() );
		Assert.AreEqual( 1, p.DeletedCount );
	}

	/// <summary>
	/// Deleting a panel recursively deletes its children: the child's OnDeleted hook fires and
	/// the child is invalidated along with the parent.
	/// </summary>
	[TestMethod]
	public void ChildrenDeletedWithParent()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var parent = root.Add.Panel();
		var child = new RecordingPanel { Parent = parent };
		root.Layout();

		parent.Delete( true );

		Assert.IsFalse( parent.IsValid );
		Assert.IsFalse( child.IsValid );
		Assert.AreEqual( 1, child.DeletedCount );
		Assert.AreEqual( 0, root.Children.Count() );
	}

	/// <summary>
	/// A panel whose :outro rule starts a transition is kept alive by the deferred deletion
	/// pump until the transition has finished, and only then deleted.
	/// </summary>
	[TestMethod]
	public void OutroTransitionDefersDeletionUntilFinished()
	{
		PanelRealTime.TimeNow = 0;
		PanelRealTime.TimeDelta = 0;

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.StyleSheet.Parse( ".box { transition: opacity 1s linear; opacity: 1; width: 50px; height: 50px; } .box:outro { opacity: 0; }" );

		var p = root.Add.Panel( "box" );
		root.Layout();

		p.Delete();
		root.Layout(); // the :outro styles apply and start the opacity transition

		Assert.IsTrue( p.HasActiveTransitions );

		// The pump refuses to delete the panel while its outro transition is running
		GlobalContext.Current.UISystem.RunDeferredDeletion();
		Assert.IsTrue( p.IsValid );
		Assert.AreEqual( 1, root.Children.Count() );

		// Once time passes the end of the transition the pump deletes it
		PanelRealTime.TimeNow = 5;
		root.Layout();
		Assert.IsFalse( p.HasActiveTransitions );

		GlobalContext.Current.UISystem.RunDeferredDeletion();
		Assert.IsFalse( p.IsValid );
		Assert.AreEqual( 0, root.Children.Count() );
	}
}
