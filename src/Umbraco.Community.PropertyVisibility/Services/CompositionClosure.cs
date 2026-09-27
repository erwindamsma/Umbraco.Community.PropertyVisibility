using Umbraco.Cms.Core.Models;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     The content types a content type is composed of, directly or transitively.
/// </summary>
/// <remarks>
///     Walks <see cref="IContentTypeComposition.ContentTypeComposition" />, which Umbraco fills from the content type
///     cache: the composition objects are full content types with their own groups, property types and compositions, so
///     no lookup is needed. A parent document type is in that list too (Umbraco adds the parent as a composition). The
///     walk visits each alias once (aliases are unique, case-insensitively), so a composition reached along two paths is
///     listed once and a cycle ends the walk instead of looping.
/// </remarks>
internal static class CompositionClosure
{
	/// <summary>
	///     Returns every content type <paramref name="contentType" /> is composed of, without the type itself, each once,
	///     depth first in the order of <see cref="IContentTypeComposition.ContentTypeComposition" />.
	/// </summary>
	/// <param name="contentType">The content type.</param>
	/// <returns>The compositions; empty when the type has none.</returns>
	public static IReadOnlyList<IContentTypeComposition> Of(IContentTypeComposition contentType)
	{
		ArgumentNullException.ThrowIfNull(contentType);

		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!string.IsNullOrEmpty(contentType.Alias))
		{
			seen.Add(contentType.Alias);
		}

		var closure = new List<IContentTypeComposition>();
		var pending = new Stack<IContentTypeComposition>();
		PushChildren(contentType, pending);

		while (pending.Count > 0)
		{
			IContentTypeComposition composition = pending.Pop();
			if (string.IsNullOrEmpty(composition.Alias) || !seen.Add(composition.Alias))
			{
				continue;
			}

			closure.Add(composition);
			PushChildren(composition, pending);
		}

		return closure;
	}

	// Pushed in reverse, so they are popped in the order Umbraco lists them.
	private static void PushChildren(IContentTypeComposition contentType, Stack<IContentTypeComposition> pending)
	{
		IContentTypeComposition?[] children = contentType.ContentTypeComposition?.ToArray() ?? [];
		for (var index = children.Length - 1; index >= 0; index--)
		{
			if (children[index] is { } child)
			{
				pending.Push(child);
			}
		}
	}
}
