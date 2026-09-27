namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     How the root node of a request was found.
/// </summary>
public enum RootResolutionSource
{
	/// <summary>The document exists in the tree; its own ancestors were used.</summary>
	Document,

	/// <summary>The document does not exist yet; the parent's ancestors were used.</summary>
	Parent,

	/// <summary>No root could be determined (a new document at the root, or unknown keys).</summary>
	None,

	/// <summary>The document is in the recycle bin.</summary>
	RecycleBin,
}

/// <summary>
///     Result of resolving a request to a root node.
/// </summary>
/// <param name="RootKey">Key of the root node, or <c>null</c> when <paramref name="Source" /> is <see cref="RootResolutionSource.None" /> or <see cref="RootResolutionSource.RecycleBin" />.</param>
/// <param name="Source">How the root was found.</param>
public sealed record RootNodeResolution(Guid? RootKey, RootResolutionSource Source)
{
	/// <summary>
	///     Shared instance for "no root could be determined".
	/// </summary>
	public static readonly RootNodeResolution None = new(null, RootResolutionSource.None);

	/// <summary>
	///     Shared instance for "the document is in the recycle bin".
	/// </summary>
	public static readonly RootNodeResolution RecycleBin = new(null, RootResolutionSource.RecycleBin);
}

/// <summary>
///     Finds the root node a document (or a document about to be created) belongs to, and the root's invariant name.
/// </summary>
public interface IRootNodeResolver
{
	/// <summary>
	///     Resolves the root node for a document, falling back to its parent when the document does not exist yet. A
	///     document in the recycle bin resolves to <see cref="RootNodeResolution.RecycleBin" />, whatever parent key is given.
	/// </summary>
	/// <param name="documentKey">Key of the document being edited; may be a client-generated key for a new document.</param>
	/// <param name="parentKey">Key of the parent the document is (being) created under, when known.</param>
	/// <returns>The resolution; never <c>null</c>.</returns>
	RootNodeResolution Resolve(Guid documentKey, Guid? parentKey);

	/// <summary>
	///     Whether a key is a root node of the content tree (a document at the top level, not in the recycle bin).
	/// </summary>
	/// <param name="key">The document key.</param>
	/// <returns><c>true</c> for a root node; <c>false</c> for a document below a root, a trashed document or an unknown key.</returns>
	bool IsRoot(Guid key);

	/// <summary>
	///     Gets the invariant name of a root node, cached per key until content changes.
	/// </summary>
	/// <param name="rootKey">Key of the root node.</param>
	/// <returns>The name, or <c>null</c> when the node cannot be loaded.</returns>
	string? GetRootName(Guid rootKey);

	/// <summary>
	///     Drops every cached root name so the next lookup reads the name from the database again.
	/// </summary>
	void InvalidateNameCache();
}
