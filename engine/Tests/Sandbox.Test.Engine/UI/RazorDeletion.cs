using Sandbox.UI;

namespace Sandbox.Test;

[TestClass]
public class RazorDeletion
{
	public class DeleteProbe : Panel
	{
		public int DeleteCalls;
		public int DeletedCalls;
		public override void Delete( bool immediate = false )
		{
			DeleteCalls++;
			base.Delete( immediate );
		}
		public override void OnDeleted() => DeletedCalls++;
	}

	public class ThrowingDeleteProbe : DeleteProbe
	{
		public override void Delete( bool immediate = false )
		{
			DeleteCalls++;
			throw new System.InvalidOperationException( "Expected Delete override failure" );
		}
	}

	[TestMethod]
	public void ThrowingDeleteDoesNotAbortTreeOrPendingCleanup()
	{
		foreach ( var pending in new[] { false, true } )
		{
			var root = new DeleteProbe();
			var tree = new PanelRenderTreeBuilder( root );
			var treeField = typeof( Panel ).GetField( "renderTree", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public );
			treeField.SetValue( root, tree );
			tree.Start();
			tree.OpenElement<DeleteProbe>( 0 );
			tree.OpenElement<ThrowingDeleteProbe>( 1 );
			tree.OpenElement<DeleteProbe>( 2 );
			tree.CloseElement();
			tree.CloseElement();
			tree.OpenElement<DeleteProbe>( 3 );
			tree.CloseElement();
			tree.CloseElement();
			tree.Finish();
			var owner = (DeleteProbe)root.Children.Single();
			var failing = (ThrowingDeleteProbe)owner.Children.First();
			var nested = (DeleteProbe)failing.Children.Single();
			var sibling = (DeleteProbe)owner.Children.Last();
			if ( pending )
			{
				tree.Start();
				tree.Finish();
				Assert.AreEqual( 0, failing.DeleteCalls );
			}
			root.Delete( true );
			foreach ( var panel in new DeleteProbe[] { root, owner, failing, nested, sibling } )
			{
				Assert.IsFalse( panel.IsValid );
				Assert.AreEqual( 1, panel.DeletedCalls );
				Assert.IsFalse( panel.Children.Any() );
			}
			Assert.AreEqual( 1, failing.DeleteCalls );
			Assert.AreEqual( 1, sibling.DeleteCalls );
			Assert.IsNull( treeField.GetValue( root ) );
			var pendingField = typeof( Panel ).GetField( "renderTreeDeletion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
			Assert.IsNull( pendingField.GetValue( owner ) );
			Assert.IsNull( pendingField.GetValue( failing ) );
			tree.Clear( immediate: true );
			Assert.AreEqual( 1, failing.DeleteCalls );
		}
	}

	[TestMethod]
	public void PhysicalContentWaitsForParentAndRunsVirtualDeleteOnce()
	{
		var root = new Panel();
		var tree = new PanelRenderTreeBuilder( root );
		tree.Start();
		tree.OpenElement<Panel>( 0 );
		tree.OpenElement<DeleteProbe>( 1 );
		tree.OpenElement<DeleteProbe>( 2 );
		tree.CloseElement();
		tree.CloseElement();
		tree.CloseElement();
		tree.Finish();
		var parent = root.Children.Single();
		var child = (DeleteProbe)parent.Children.Single();
		var grandchild = (DeleteProbe)child.Children.Single();
		tree.Start();
		tree.Finish();
		Assert.IsTrue( parent.IsDeleting );
		Assert.AreEqual( 0, child.DeleteCalls );
		Assert.AreEqual( 0, grandchild.DeleteCalls );
		parent.Delete( true );
		Assert.AreEqual( 1, child.DeleteCalls );
		Assert.AreEqual( 1, grandchild.DeleteCalls );
		Assert.AreEqual( 1, child.DeletedCalls );
		Assert.AreEqual( 1, grandchild.DeletedCalls );
		Assert.IsFalse( child.IsValid );
		Assert.IsFalse( grandchild.IsValid );
		tree.Clear();
		root.Delete( true );
		Assert.AreEqual( 1, child.DeleteCalls );
	}

	[TestMethod]
	public void ReparentedAndDetachedLogicalContentDeletesIndependently()
	{
		foreach ( var detached in new[] { false, true } )
		{
			var root = new Panel();
			var popup = new Panel();
			var tree = new PanelRenderTreeBuilder( root );
			tree.Start();
			tree.OpenElement<Panel>( 0 );
			tree.OpenElement<DeleteProbe>( 1 );
			tree.CloseElement();
			tree.CloseElement();
			tree.Finish();
			var parent = root.Children.Single();
			var row = (DeleteProbe)parent.Children.Single();
			row.Parent = detached ? null : popup;
			tree.Start();
			tree.Finish();
			Assert.IsTrue( row.IsDeleting );
			Assert.AreEqual( 1, row.DeleteCalls );
			parent.Delete( true );
			Assert.IsTrue( row.IsValid );
			row.Delete( true );
			Assert.AreEqual( 1, row.DeletedCalls );
			Assert.IsFalse( row.IsValid );
			root.Delete( true );
			popup.Delete( true );
		}
	}

	[TestMethod]
	public void ReparentingPendingContentDoesNotCancelLogicalDeletion()
	{
		foreach ( var detached in new[] { false, true } )
		{
			var root = new Panel();
			var liveParent = new Panel();
			var tree = new PanelRenderTreeBuilder( root );
			tree.Start();
			tree.OpenElement<Panel>( 0 );
			tree.OpenElement<DeleteProbe>( 1 );
			tree.CloseElement();
			tree.CloseElement();
			tree.Finish();
			var owner = root.Children.Single();
			var child = (DeleteProbe)owner.Children.Single();
			tree.Start();
			tree.Finish();
			child.Parent = detached ? null : liveParent;
			Assert.AreEqual( 0, child.DeleteCalls );
			owner.Delete( true );
			Assert.IsTrue( liveParent.IsValid );
			Assert.IsFalse( child.IsValid );
			Assert.AreEqual( 1, child.DeleteCalls );
			Assert.AreEqual( 1, child.DeletedCalls );
			liveParent.Delete( true );
			root.Delete( true );
			tree.Clear( immediate: true );
			Assert.AreEqual( 1, child.DeleteCalls );
			Assert.AreEqual( 1, child.DeletedCalls );
		}
	}

	[TestMethod]
	public void ImmediateTreeClearCallsOverridesWithoutStartingOutros()
	{
		var root = new Panel();
		var tree = new PanelRenderTreeBuilder( root );
		tree.Start();
		tree.OpenElement<DeleteProbe>( 0 );
		tree.OpenElement<DeleteProbe>( 1 );
		tree.CloseElement();
		tree.CloseElement();
		tree.Finish();
		var parent = (DeleteProbe)root.Children.Single();
		var child = (DeleteProbe)parent.Children.Single();
		tree.Clear( immediate: true );
		tree.Clear( immediate: true );
		Assert.AreEqual( 1, parent.DeleteCalls );
		Assert.AreEqual( 1, child.DeleteCalls );
		Assert.IsFalse( parent.IsValid );
		Assert.IsFalse( child.IsValid );
		root.Delete( true );
	}
}
