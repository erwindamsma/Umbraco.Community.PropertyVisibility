using System.Reflection;
using System.Text.Json.Serialization;
using NJsonSchema.Generation;

namespace Umbraco.Community.PropertyVisibility.SchemaGenerator;

/// <summary>
///     Sets <c>default</c> from the property initializers of the options model (NJsonSchema itself only reads
///     <c>[DefaultValue]</c>, which the model does not carry), and keeps the model's object schemas closed.
/// </summary>
/// <remarks>
///     Only scalar values become defaults (booleans, numbers, strings, enums). Collections start empty and a <c>null</c>
///     initial value means "not set", so neither is written.
/// </remarks>
/// <param name="modelAssembly">The package assembly; types from elsewhere (the appsettings root wrapper) are left alone.</param>
internal sealed class ModelDefaultsSchemaProcessor(Assembly modelAssembly) : ISchemaProcessor
{
	/// <inheritdoc />
	public void Process(SchemaProcessorContext context)
	{
		var type = context.ContextualType.Type;
		if (type.Assembly != modelAssembly || !type.IsClass || type.GetConstructor(Type.EmptyTypes) is null)
		{
			return;
		}

		// A misspelled key is a load error at runtime (appsettings binding and the rules file both reject unknown keys),
		// so the schema flags it too.
		context.Schema.AllowAdditionalProperties = false;

		var instance = Activator.CreateInstance(type)!;
		foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetMethod is null || property.GetIndexParameters().Length > 0)
			{
				continue;
			}

			var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
			if (!context.Schema.Properties.TryGetValue(name, out var schemaProperty))
			{
				continue;
			}

			var value = property.GetValue(instance);
			if (IsScalar(value))
			{
				schemaProperty.Default = value is Enum ? value.ToString() : value;
			}
		}
	}

	private static bool IsScalar(object? value) =>
		value is bool or string or Enum or byte or short or int or long or float or double or decimal;
}
