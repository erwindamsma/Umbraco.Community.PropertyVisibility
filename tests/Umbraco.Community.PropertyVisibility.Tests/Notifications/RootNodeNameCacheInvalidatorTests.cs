using Moq;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Sync;
using Umbraco.Community.PropertyVisibility.Notifications;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Tests.Notifications;

/// <summary>
///     <see cref="RootNodeNameCacheInvalidator" />: a content change that involves a root node clears the root name cache
///     and asks for a configuration analysis run, so a site that stops matching is logged when it happens; a change below
///     a root keeps the cached names, because it cannot rename a root.
/// </summary>
[TestFixture]
public sealed class RootNodeNameCacheInvalidatorTests
{
	private static readonly Guid CorporateRoot = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid CampaignRoot = Guid.Parse("0b7e4c1a-3d2f-4e5a-9c8b-6f1e2d3c4b5a");
	private static readonly Guid CorporatePage = Guid.Parse("11111111-1111-4111-8111-111111111111");

	private Mock<IRootNodeResolver> _rootNodeResolver = null!;
	private Mock<IDocumentNavigationQueryService> _navigation = null!;
	private ConfigurationAnalysisTrigger _trigger = null!;
	private IDisposable _subscription = null!;
	private int _requests;
	private RootNodeNameCacheInvalidator _invalidator = null!;

	[SetUp]
	public void SetUp()
	{
		_rootNodeResolver = new Mock<IRootNodeResolver>();
		_navigation = new Mock<IDocumentNavigationQueryService>();
		Roots(CorporateRoot, CampaignRoot);
		_trigger = new ConfigurationAnalysisTrigger();
		_requests = 0;
		_subscription = _trigger.Subscribe(() => _requests++);
		_trigger.Analyzed([CorporateRoot, CampaignRoot]);
		_invalidator = new RootNodeNameCacheInvalidator(_rootNodeResolver.Object, _navigation.Object, _trigger);
	}

	[TearDown]
	public void TearDown() => _subscription.Dispose();

	[Test]
	public void A_saved_document_below_a_root_keeps_the_name_cache()
	{
		_invalidator.Handle(new ContentSavedNotification(Content(parentId: 1000), new EventMessages()));

		Assert.That(_requests, Is.Zero, "a document below a root is not a root change");
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void A_saved_root_clears_the_name_cache_and_asks_for_an_analysis()
	{
		// A rename, or a new root.
		_invalidator.Handle(new ContentSavedNotification(Content(parentId: -1), new EventMessages()));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_move_below_the_top_level_keeps_the_name_cache()
	{
		_invalidator.Handle(new ContentMovedNotification(new MoveEventInfo<IContent>(Content(parentId: 1001), "-1,1000,1234", 1001, null), new EventMessages()));

		Assert.That(_requests, Is.Zero);
		NameCacheCleared(Times.Never());
	}

	[TestCase(-1, "-1,1000,1234", TestName = "A document moved to the top level is a root change")]
	[TestCase(1000, "-1,1234", TestName = "A root moved below another document is a root change")]
	public void A_move_to_or_from_the_top_level_clears_the_name_cache_and_asks_for_an_analysis(int newParentId, string originalPath)
	{
		_invalidator.Handle(new ContentMovedNotification(new MoveEventInfo<IContent>(Content(newParentId), originalPath, newParentId, null), new EventMessages()));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_trashed_document_below_a_root_keeps_the_name_cache()
	{
		_invalidator.Handle(new ContentMovedToRecycleBinNotification(new MoveToRecycleBinEventInfo<IContent>(Content(-20), "-1,1000,1234"), new EventMessages()));

		Assert.That(_requests, Is.Zero);
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void A_trashed_root_clears_the_name_cache_and_asks_for_an_analysis()
	{
		_invalidator.Handle(new ContentMovedToRecycleBinNotification(new MoveToRecycleBinEventInfo<IContent>(Content(-20), "-1,1000"), new EventMessages()));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void Emptying_the_recycle_bin_keeps_the_name_cache()
	{
		// Emptying the recycle bin deletes documents whose parent is the bin.
		_invalidator.Handle(new ContentDeletedNotification(Content(parentId: -20), new EventMessages()));

		Assert.That(_requests, Is.Zero);
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void A_root_deleted_without_the_recycle_bin_clears_the_name_cache_and_asks_for_an_analysis()
	{
		_invalidator.Handle(new ContentDeletedNotification(Content(parentId: -1), new EventMessages()));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_distributed_refresh_of_a_document_below_a_root_keeps_the_name_cache()
	{
		// Raised on every server of a load-balanced setup, not only the one that saved the content.
		_invalidator.Handle(Refresh(Payload(CorporatePage, TreeChangeTypes.RefreshNode)));

		Assert.That(_requests, Is.Zero, "a document below a root, with unchanged roots");
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void A_refreshed_root_clears_the_name_cache_and_asks_for_an_analysis()
	{
		// A root renamed on another server.
		_invalidator.Handle(Refresh(Payload(CampaignRoot, TreeChangeTypes.RefreshNode)));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_refresh_after_which_the_roots_differ_asks_for_an_analysis()
	{
		// The refresher updates the navigation structure before it notifies: a root moved below a document elsewhere is
		// no longer a root when the payload arrives, but the root set differs from the one the last analysis saw. Its
		// cached name is never read again while it is not a root, and its return as a root is a root change of its own.
		Roots(CorporateRoot);

		_invalidator.Handle(Refresh(Payload(CampaignRoot, TreeChangeTypes.RefreshBranch)));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void A_refresh_of_everything_or_an_unknown_message_clears_the_name_cache_and_asks_for_an_analysis()
	{
		_invalidator.Handle(Refresh(Payload(CorporatePage, TreeChangeTypes.RefreshAll)));
		_invalidator.Handle(new ContentCacheRefresherNotification(new object(), MessageType.RefreshAll));

		Assert.That(_requests, Is.EqualTo(2));
		NameCacheCleared(Times.Exactly(2));
	}

	[Test]
	public void A_payload_without_a_key_clears_the_name_cache()
	{
		_invalidator.Handle(Refresh(new ContentCacheRefresher.JsonPayload { Id = 1234, Key = null, ChangeTypes = TreeChangeTypes.RefreshNode }));

		Assert.That(_requests, Is.EqualTo(1));
		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_blueprint_refresh_is_not_a_root_change()
	{
		_invalidator.Handle(Refresh(new ContentCacheRefresher.JsonPayload { Id = 2000, Key = Guid.NewGuid(), ChangeTypes = TreeChangeTypes.RefreshAll, Blueprint = true }));

		Assert.That(_requests, Is.Zero);
		NameCacheCleared(Times.Never());
	}

	[Test]
	public void Nothing_is_requested_before_the_first_analysis()
	{
		var trigger = new ConfigurationAnalysisTrigger();
		var requests = 0;
		using IDisposable subscription = trigger.Subscribe(() => requests++);
		var invalidator = new RootNodeNameCacheInvalidator(_rootNodeResolver.Object, _navigation.Object, trigger);
		Roots(CorporateRoot);

		invalidator.Handle(Refresh(Payload(CorporatePage, TreeChangeTypes.RefreshNode)));

		Assert.That(requests, Is.Zero, "the startup run has not happened yet and covers the change");
	}

	[Test]
	public void A_failing_navigation_never_fails_the_content_operation_and_clears_the_name_cache()
	{
		IEnumerable<Guid> ignored;
		_navigation.Setup(navigation => navigation.TryGetRootKeys(out ignored)).Throws(new InvalidOperationException("navigation not ready"));

		Assert.DoesNotThrow(() => _invalidator.Handle(Refresh(Payload(CorporatePage, TreeChangeTypes.RefreshNode))));
		NameCacheCleared(Times.Once(), "when root involvement cannot be determined");
	}

	[Test]
	public void Navigation_without_root_keys_clears_the_name_cache()
	{
		IEnumerable<Guid> none = [];
		_navigation.Setup(navigation => navigation.TryGetRootKeys(out none)).Returns(false);

		_invalidator.Handle(Refresh(Payload(CorporatePage, TreeChangeTypes.RefreshNode)));

		NameCacheCleared(Times.Once());
	}

	[Test]
	public void A_failing_root_check_never_fails_the_content_operation_and_clears_the_name_cache()
	{
		var content = new Mock<IContent>();
		content.SetupGet(item => item.ParentId).Throws(new InvalidOperationException("not loaded"));

		Assert.DoesNotThrow(() => _invalidator.Handle(new ContentSavedNotification(content.Object, new EventMessages())));
		NameCacheCleared(Times.Once());
	}

	private static IContent Content(int parentId) => Mock.Of<IContent>(content => content.ParentId == parentId);

	private static ContentCacheRefresher.JsonPayload Payload(Guid key, TreeChangeTypes changeTypes)
		=> new() { Id = 1234, Key = key, ChangeTypes = changeTypes };

	private static ContentCacheRefresherNotification Refresh(params ContentCacheRefresher.JsonPayload[] payloads)
		=> new(payloads, MessageType.RefreshByPayload);

	private void NameCacheCleared(Times times, string? because = null)
		=> _rootNodeResolver.Verify(resolver => resolver.InvalidateNameCache(), times, because ?? string.Empty);

	private void Roots(params Guid[] roots)
	{
		IEnumerable<Guid> keys = roots;
		_navigation.Setup(navigation => navigation.TryGetRootKeys(out keys)).Returns(true);
	}
}
