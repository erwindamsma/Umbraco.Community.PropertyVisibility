using System.Collections.Concurrent;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Default <see cref="IRootNodeResolver" /> built on the in-memory document navigation structure.
/// </summary>
/// <remarks>
///     <c>TryGetAncestorsOrSelfKeys</c> returns the keys self first and root last, never the virtual root, and
///     <c>false</c> for trashed or unknown keys; the root is therefore the last key. Names are read as entities through
///     <see cref="IEntityService.Get(Guid, UmbracoObjectTypes)" />, without loading the document's property values, and
///     cached per root key until <see cref="InvalidateNameCache" /> is called by the content notification handler. The
///     entity name is the document's invariant name as of the last save, the value <see cref="IContent" /> has as its
///     name: on a culture-variant document, Umbraco sets it to the default culture's name before it saves (to the first
///     culture's name when the default culture has none).
///     A name read that overlaps an invalidation is returned but not kept, so a name read before a rename can never be
///     cached after the invalidation that the rename triggered.
/// </remarks>
public sealed class RootNodeResolver : IRootNodeResolver
{
	private readonly IDocumentNavigationQueryService _navigation;
	private readonly IEntityService _entityService;
	private readonly ConcurrentDictionary<Guid, string?> _names = new();
	private long _generation;

	/// <summary>
	///     Initializes a new instance of the <see cref="RootNodeResolver" /> class.
	/// </summary>
	/// <param name="navigation">The document navigation query service.</param>
	/// <param name="entityService">The entity service, used only to read root names.</param>
	public RootNodeResolver(IDocumentNavigationQueryService navigation, IEntityService entityService)
	{
		_navigation = navigation;
		_entityService = entityService;
	}

	/// <inheritdoc />
	public RootNodeResolution Resolve(Guid documentKey, Guid? parentKey)
	{
		if (TryGetRootKey(documentKey, out Guid rootKey))
		{
			return new RootNodeResolution(rootKey, RootResolutionSource.Document);
		}

		// Before the parent: a document in the recycle bin exists, and its own place wins over a parent key sent with it.
		if (_navigation.TryGetParentKeyInBin(documentKey, out _))
		{
			return RootNodeResolution.RecycleBin;
		}

		if (parentKey is { } parent && parent != Guid.Empty && TryGetRootKey(parent, out rootKey))
		{
			return new RootNodeResolution(rootKey, RootResolutionSource.Parent);
		}

		return RootNodeResolution.None;
	}

	/// <inheritdoc />
	public bool IsRoot(Guid key)
		=> key != Guid.Empty && _navigation.TryGetParentKey(key, out Guid? parentKey) && parentKey is null;

	/// <inheritdoc />
	public string? GetRootName(Guid rootKey)
	{
		if (_names.TryGetValue(rootKey, out var cached))
		{
			return cached;
		}

		var generation = Interlocked.Read(ref _generation);
		var name = _entityService.Get(rootKey, UmbracoObjectTypes.Document)?.Name;

		// Add first, then check: an invalidation between the read and the add either clears the entry itself or bumps the
		// generation, in which case the possibly stale entry is taken out again (only if it is still the value added here).
		_names.TryAdd(rootKey, name);
		if (Interlocked.Read(ref _generation) != generation)
		{
			_names.TryRemove(new KeyValuePair<Guid, string?>(rootKey, name));
		}

		return name;
	}

	/// <inheritdoc />
	public void InvalidateNameCache()
	{
		Interlocked.Increment(ref _generation);
		_names.Clear();
	}

	private bool TryGetRootKey(Guid key, out Guid rootKey)
	{
		if (_navigation.TryGetAncestorsOrSelfKeys(key, out IEnumerable<Guid> keys))
		{
			// Self first, root last; the collection is never empty when the call succeeds, but stay defensive.
			using IEnumerator<Guid> enumerator = keys.GetEnumerator();
			if (enumerator.MoveNext())
			{
				Guid last = enumerator.Current;
				while (enumerator.MoveNext())
				{
					last = enumerator.Current;
				}

				rootKey = last;
				return true;
			}
		}

		rootKey = Guid.Empty;
		return false;
	}
}
