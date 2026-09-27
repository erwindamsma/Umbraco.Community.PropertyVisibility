using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Services;
using Umbraco.Extensions;
using CoreConstants = Umbraco.Cms.Core.Constants;

namespace Umbraco.Community.PropertyVisibility.Notifications;

/// <summary>
///     When content that involves a root node is saved, moved, trashed or deleted, clears the root name cache of
///     <see cref="IRootNodeResolver" />, so a renamed root is matched by its new name on the next request, and asks for a
///     new configuration analysis (<see cref="ConfigurationAnalysisTrigger" />), so a site that stops matching is logged
///     then.
/// </summary>
/// <remarks>
///     <para>
///         The content service notifications fire only on the server that performed the operation;
///         <see cref="ContentCacheRefresherNotification" /> is raised by the distributed cache on every server, after the
///         navigation structure was updated, so the other servers of a load-balanced backoffice drop their cached names too
///         and notice a root that was created, renamed, moved, trashed or deleted elsewhere.
///     </para>
///     <para>
///         Root involvement: a saved or deleted document at the top level, a move to or from the top level, a trashed
///         document that was at the top level; for a cache refresh, a payload for a current root, a refresh of everything,
///         a payload without a key or a message that is not a payload list. Only these clear the cache: a change below a
///         root cannot change a root's name, so the other saves keep the cached names. A cache refresh whose root keys
///         differ from the ones the last analysis saw (a root moved below a document or deleted elsewhere) also asks for an
///         analysis; the name cached for a key that is no longer a root is never read, and the key's return as a root is
///         itself a root change. When root involvement cannot be determined the cache is cleared anyway. Never throws: a
///         content operation must not fail because of this package.
///     </para>
/// </remarks>
public sealed class RootNodeNameCacheInvalidator :
	INotificationHandler<ContentSavedNotification>,
	INotificationHandler<ContentMovedNotification>,
	INotificationHandler<ContentMovedToRecycleBinNotification>,
	INotificationHandler<ContentDeletedNotification>,
	INotificationHandler<ContentCacheRefresherNotification>
{
	private readonly IRootNodeResolver _rootNodeResolver;
	private readonly IDocumentNavigationQueryService _navigation;
	private readonly ConfigurationAnalysisTrigger _trigger;

	/// <summary>
	///     Initializes a new instance of the <see cref="RootNodeNameCacheInvalidator" /> class.
	/// </summary>
	/// <param name="rootNodeResolver">The resolver whose cache is cleared.</param>
	/// <param name="navigation">The in-memory document tree, to tell whether a refreshed document is a root.</param>
	/// <param name="trigger">Asked for a new configuration analysis when a root node changes.</param>
	public RootNodeNameCacheInvalidator(IRootNodeResolver rootNodeResolver, IDocumentNavigationQueryService navigation, ConfigurationAnalysisTrigger trigger)
	{
		_rootNodeResolver = rootNodeResolver;
		_navigation = navigation;
		_trigger = trigger;
	}

	/// <inheritdoc />
	public void Handle(ContentSavedNotification notification)
		=> Invalidate(() => notification.SavedEntities.Any(IsTopLevel));

	/// <inheritdoc />
	public void Handle(ContentMovedNotification notification)
		=> Invalidate(() => notification.MoveInfoCollection.Any(move => IsTopLevel(move.Entity) || IsTopLevelPath(move.OriginalPath)));

	/// <inheritdoc />
	public void Handle(ContentMovedToRecycleBinNotification notification)
		=> Invalidate(() => notification.MoveInfoCollection.Any(move => IsTopLevelPath(move.OriginalPath)));

	/// <inheritdoc />
	public void Handle(ContentDeletedNotification notification)
		=> Invalidate(() => notification.DeletedEntities.Any(IsTopLevel));

	/// <inheritdoc />
	public void Handle(ContentCacheRefresherNotification notification)
	{
		try
		{
			if (!_navigation.TryGetRootKeys(out IEnumerable<Guid> rootKeys))
			{
				_rootNodeResolver.InvalidateNameCache();
				return;
			}

			var roots = rootKeys.ToHashSet();
			var involvesRoot = notification.MessageObject is not ContentCacheRefresher.JsonPayload[] payloads
				|| payloads.Any(payload => !payload.Blueprint
					&& (payload.ChangeTypes.HasType(TreeChangeTypes.RefreshAll) || payload.Key is not { } key || roots.Contains(key)));
			if (involvesRoot)
			{
				_rootNodeResolver.InvalidateNameCache();
				_trigger.Request();
			}
			else
			{
				_trigger.RequestIfRootsChanged(roots);
			}
		}
		catch (Exception)
		{
			// Best effort: the next configuration change or the health check still shows the state.
			InvalidateQuietly();
		}
	}

	private static bool IsTopLevel(IContent content) => content.ParentId == CoreConstants.System.Root;

	// "-1,1234": a document directly below the virtual root.
	private static bool IsTopLevelPath(string? path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return false;
		}

		var segments = path.Split(',');
		return segments.Length == 2 && segments[0] == CoreConstants.System.RootString;
	}

	private void Invalidate(Func<bool> involvesRoot)
	{
		try
		{
			if (involvesRoot())
			{
				_rootNodeResolver.InvalidateNameCache();
				_trigger.Request();
			}
		}
		catch (Exception)
		{
			// Best effort: never fail the content operation that raised the notification.
			InvalidateQuietly();
		}
	}

	private void InvalidateQuietly()
	{
		try
		{
			_rootNodeResolver.InvalidateNameCache();
		}
		catch (Exception)
		{
			// Nothing else to do; the next root change clears the cache.
		}
	}
}
