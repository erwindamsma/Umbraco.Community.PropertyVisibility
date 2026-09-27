using System.Diagnostics.CodeAnalysis;

namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Case-insensitive lookups that do not depend on the dictionary's comparer.
/// </summary>
/// <remarks>
///     The option dictionaries are created with <see cref="StringComparer.OrdinalIgnoreCase" />, but a configuration
///     binder or a deserializer may replace them with a default-comparer instance; the fallback scan keeps the documented
///     case-insensitive semantics either way.
/// </remarks>
internal static class DictionaryExtensions
{
	/// <summary>
	///     Tries the dictionary's own lookup first, then a case-insensitive scan of the keys.
	/// </summary>
	/// <typeparam name="TValue">The value type.</typeparam>
	/// <param name="dictionary">The dictionary.</param>
	/// <param name="key">The key to find.</param>
	/// <param name="value">The value when found.</param>
	/// <returns><c>true</c> when a key equal to <paramref name="key" /> ignoring case exists.</returns>
	public static bool TryGetValueIgnoreCase<TValue>(
		this Dictionary<string, TValue> dictionary,
		string key,
		[NotNullWhen(true)] out TValue? value)
		where TValue : class
	{
		if (dictionary.TryGetValue(key, out TValue? direct))
		{
			value = direct;
			return true;
		}

		foreach ((var candidate, TValue candidateValue) in dictionary)
		{
			if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
			{
				value = candidateValue;
				return true;
			}
		}

		value = null;
		return false;
	}
}
