namespace Sandbox;

public abstract partial class SerializedProperty
{
	/// <summary>
	/// Allows easily creating SerializedProperty classes that wrap other properties.
	/// </summary>
	public abstract class Proxy : SerializedProperty
	{
		protected abstract SerializedProperty ProxyTarget { get; }

		public override SerializedObject Parent => ProxyTarget.Parent;
		public override bool IsProperty => ProxyTarget.IsProperty;
		public override bool IsField => ProxyTarget.IsField;
		public override bool IsMethod => ProxyTarget.IsMethod;
		public override string Name => ProxyTarget.Name;
		public override string DisplayName => ProxyTarget.DisplayName;
		public override string Description => ProxyTarget.Description;
		public override string GroupName => ProxyTarget.GroupName;
		public override int Order => ProxyTarget.Order;
		public override bool IsEditable => ProxyTarget.IsEditable;
		public override bool IsPublic => ProxyTarget.IsPublic;
		public override Type PropertyType => ProxyTarget.PropertyType;
		public override string SourceFile => ProxyTarget.SourceFile;
		public override int SourceLine => ProxyTarget.SourceLine;
		public override bool HasChanges => ProxyTarget.HasChanges;

		public override bool IsValid => ProxyTarget.IsValid();

		public override ref AsAccessor As => ref base.As;

		public override bool TryGetAsObject( out SerializedObject obj ) => ProxyTarget.TryGetAsObject( out obj );
		public override T GetValue<T>( T defaultValue = default ) => ProxyTarget.GetValue( defaultValue );
		public override void SetValue<T>( T value ) => ProxyTarget.SetValue( value );
		public override IEnumerable<Attribute> GetAttributes() => ProxyTarget.GetAttributes();
	}
}
