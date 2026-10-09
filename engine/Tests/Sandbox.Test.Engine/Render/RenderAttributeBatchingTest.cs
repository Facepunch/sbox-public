using NativeEngine;
using Sandbox.Rendering;
using System;

namespace RenderTests;

[TestClass]
public class RenderAttributeBatchingTest
{
	private sealed class Attributes : IDisposable
	{
		public CRenderAttributes Native { get; } = CRenderAttributes.Create();
		public void Dispose() => Native.DeleteThis();
	}

	private static bool Equal( Attributes a, Attributes b, bool includeParents = true )
		=> CRenderAttributes.AreEqual( a?.Native ?? default, b?.Native ?? default, includeParents );

	[TestMethod]
	public void ValuesMatchRegardlessOfInsertionOrderAndOverflowStorage()
	{
		using var a = new Attributes();
		using var b = new Attributes();
		Assert.IsTrue( Equal( a, null ) );

		for ( int i = 0; i < 24; i++ )
			a.Native.SetFloatValue( $"Value{i}", i );
		for ( int i = 23; i >= 0; i-- )
			b.Native.SetFloatValue( $"Value{i}", i );

		Assert.IsTrue( Equal( a, b ) );
		b.Native.SetFloatValue( "Value12", 100 );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetFloatValue( "Value12", 12 );
		Assert.IsTrue( Equal( a, b ) );
		b.Native.DeleteFloatValue( "Value12" );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetFloatValue( "Value12", 12 );
		Assert.IsTrue( Equal( a, b ) );
	}

	[TestMethod]
	public void AttributeTypesCombosStringsAndBuffersAreCompared()
	{
		using var a = new Attributes();
		using var b = new Attributes();
		a.Native.SetIntValue( "Value", 1 );
		b.Native.SetFloatValue( "Value", 1 );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.Clear( true, true );
		b.Native.SetIntValue( "Value", 1 );
		Assert.IsTrue( Equal( a, b ) );

		a.Native.SetComboValue( "D_TEST", 1 );
		b.Native.SetComboValue( "D_TEST", 0 );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetComboValue( "D_TEST", 1 );
		a.Native.SetStringValue( "Label", "same" );
		b.Native.SetStringValue( "Label", new string( "same".ToCharArray() ) );
		Assert.IsTrue( Equal( a, b ) );
		b.Native.SetStringValue( "Label", "different" );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetStringValue( "Label", "same" );
		a.Native.SetPtrValue( "Buffer", new IntPtr( 1 ) );
		b.Native.SetPtrValue( "Buffer", new IntPtr( 2 ) );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetPtrValue( "Buffer", new IntPtr( 1 ) );
		Assert.IsTrue( Equal( a, b ) );
	}

	[TestMethod]
	public void ParentMutationsAndOverriddenValuesAreHandled()
	{
		using var parent = new Attributes();
		using var a = new Attributes();
		using var b = new Attributes();
		a.Native.SetParent( parent.Native );
		Assert.IsTrue( Equal( a, null ) );
		parent.Native.SetIntValue( "Value", 1 );
		b.Native.SetIntValue( "Value", 1 );
		Assert.IsTrue( Equal( a, b ) );
		Assert.IsFalse( Equal( a, b, includeParents: false ) );
		Assert.IsTrue( Equal( a, null, includeParents: false ) );

		parent.Native.SetIntValue( "Value", 2 );
		Assert.IsFalse( Equal( a, b ) );
		a.Native.SetIntValue( "Value", 1 );
		Assert.IsTrue( Equal( a, b ) );
		parent.Native.SetIntValue( "Value", 3 );
		Assert.IsTrue( Equal( a, b ) );
		a.Native.DeleteIntValue( "Value" );
		Assert.IsFalse( Equal( a, b ) );
		a.Native.SetParent( default );
		Assert.IsTrue( Equal( a, null ) );
	}

	[TestMethod]
	public void MergeAndClearUpdateVectorsAndMatrices()
	{
		using var a = new Attributes();
		using var b = new Attributes();
		a.Native.SetVector4DValue( "Vector", new Vector4( 1, 2, 3, 4 ) );
		a.Native.SetVMatrixValue( "Matrix", Matrix.Identity );
		a.Native.MergeToPtr( b.Native );
		Assert.IsTrue( Equal( a, b ) );
		b.Native.SetVector4DValue( "Vector", new Vector4( 1, 2, 3, 5 ) );
		Assert.IsFalse( Equal( a, b ) );
		a.Native.MergeToPtr( b.Native );
		b.Native.SetVMatrixValue( "Matrix", Matrix.Identity with { M41 = 10 } );
		Assert.IsFalse( Equal( a, b ) );
		a.Native.MergeToPtr( b.Native );
		Assert.IsTrue( Equal( a, b ) );
		a.Native.Clear( false, true );
		Assert.IsFalse( Equal( a, b ) );
		Assert.IsFalse( Equal( b, a ) );
		b.Native.Clear( true, true );
		Assert.IsTrue( Equal( a, b ) );
	}

	[TestMethod]
	public void SamplerOnlyAttributesAreNotEmptyAndClearCorrectly()
	{
		using var a = new Attributes();
		using var b = new Attributes();
		var sampler = new SamplerState { Filter = FilterMode.Point, AddressModeU = TextureAddressMode.Clamp };
		a.Native.SetSamplerValue( "Sampler", new( sampler ) );
		Assert.IsFalse( a.Native.IsEmpty() );
		Assert.IsFalse( Equal( a, null ) );
		Assert.IsFalse( Equal( a, b ) );
		b.Native.SetSamplerValue( "Sampler", new( sampler ) );
		Assert.IsTrue( Equal( a, b ) );
		sampler.AddressModeU = TextureAddressMode.Wrap;
		b.Native.SetSamplerValue( "Sampler", new( sampler ) );
		Assert.IsFalse( Equal( a, b ) );
		a.Native.Clear( false, true );
		b.Native.Clear( true, true );
		Assert.IsTrue( a.Native.IsEmpty() );
		Assert.IsTrue( Equal( a, null ) );
		Assert.IsTrue( Equal( a, b ) );
	}
}
