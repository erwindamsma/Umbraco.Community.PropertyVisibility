using Moq;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

[TestFixture]
public sealed class RootNodeResolverTests
{
	private static readonly Guid Root = Guid.Parse("5c2b4d7e-9f1a-4c3e-8b6d-2a1f0e9d8c7b");
	private static readonly Guid Section = Guid.Parse("1f2e3d4c-5b6a-4978-8a9b-0c1d2e3f4a5b");
	private static readonly Guid Page = Guid.Parse("7a6b5c4d-3e2f-4a1b-9c8d-7e6f5a4b3c2d");
	private static readonly Guid NewPage = Guid.Parse("2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e");
	private static readonly Guid TrashedPage = Guid.Parse("3c4d5e6f-7a8b-4c9d-8e0f-2a3b4c5d6e7f");

	private Mock<IDocumentNavigationQueryService> _navigation = null!;
	private Mock<IEntityService> _entityService = null!;
	private RootNodeResolver _resolver = null!;

	[SetUp]
	public void SetUp()
	{
		_navigation = new Mock<IDocumentNavigationQueryService>();
		_entityService = new Mock<IEntityService>(MockBehavior.Strict);
		_entityService
			.Setup(service => service.Get(It.IsAny<Guid>(), It.IsAny<UmbracoObjectTypes>()))
			.Returns((IEntitySlim?)null);
		_resolver = new RootNodeResolver(_navigation.Object, _entityService.Object);
	}

	[Test]
	public void Existing_document_resolves_to_the_last_ancestor()
	{
		Ancestors(Page, Page, Section, Root);

		RootNodeResolution resolution = _resolver.Resolve(Page, parentKey: null);

		Assert.That(resolution, Is.EqualTo(new RootNodeResolution(Root, RootResolutionSource.Document)));
	}

	[Test]
	public void Root_document_resolves_to_itself()
	{
		Ancestors(Root, Root);

		RootNodeResolution resolution = _resolver.Resolve(Root, parentKey: null);

		Assert.That(resolution, Is.EqualTo(new RootNodeResolution(Root, RootResolutionSource.Document)));
	}

	[Test]
	public void IsRoot_is_true_only_for_a_document_without_a_parent_in_the_main_tree()
	{
		Guid? noParent = null;
		Guid? sectionParent = Root;
		Guid? unknown = null;
		_navigation.Setup(navigation => navigation.TryGetParentKey(Root, out noParent)).Returns(true);
		_navigation.Setup(navigation => navigation.TryGetParentKey(Section, out sectionParent)).Returns(true);
		_navigation.Setup(navigation => navigation.TryGetParentKey(TrashedPage, out unknown)).Returns(false);

		Assert.Multiple(() =>
		{
			Assert.That(_resolver.IsRoot(Root), Is.True);
			Assert.That(_resolver.IsRoot(Section), Is.False, "below a root");
			Assert.That(_resolver.IsRoot(TrashedPage), Is.False, "not in the main tree (trashed or unknown)");
			Assert.That(_resolver.IsRoot(Guid.Empty), Is.False);
		});
	}

	[Test]
	public void Existing_document_wins_over_the_parent_key()
	{
		Ancestors(Page, Page, Section, Root);

		RootNodeResolution resolution = _resolver.Resolve(Page, parentKey: Guid.NewGuid());

		Assert.That(resolution.Source, Is.EqualTo(RootResolutionSource.Document));
		Assert.That(resolution.RootKey, Is.EqualTo(Root));
	}

	[Test]
	public void New_document_falls_back_to_the_parent()
	{
		Ancestors(Section, Section, Root);

		RootNodeResolution resolution = _resolver.Resolve(NewPage, parentKey: Section);

		Assert.That(resolution, Is.EqualTo(new RootNodeResolution(Root, RootResolutionSource.Parent)));
	}

	[Test]
	public void New_document_directly_under_a_root_resolves_to_that_root()
	{
		Ancestors(Root, Root);

		RootNodeResolution resolution = _resolver.Resolve(NewPage, parentKey: Root);

		Assert.That(resolution, Is.EqualTo(new RootNodeResolution(Root, RootResolutionSource.Parent)));
	}

	[Test]
	public void Empty_parent_key_is_ignored()
	{
		RootNodeResolution resolution = _resolver.Resolve(NewPage, parentKey: Guid.Empty);

		Assert.That(resolution, Is.SameAs(RootNodeResolution.None));
		IEnumerable<Guid> ignored = [];
		_navigation.Verify(navigation => navigation.TryGetAncestorsOrSelfKeys(Guid.Empty, out ignored), Times.Never);
	}

	[Test]
	public void Trashed_document_resolves_to_the_recycle_bin()
	{
		Guid? binParent = null;
		_navigation.Setup(navigation => navigation.TryGetParentKeyInBin(TrashedPage, out binParent)).Returns(true);

		RootNodeResolution resolution = _resolver.Resolve(TrashedPage, parentKey: null);

		Assert.Multiple(() =>
		{
			Assert.That(resolution, Is.SameAs(RootNodeResolution.RecycleBin));
			Assert.That(resolution.RootKey, Is.Null);
			Assert.That(resolution.Source, Is.EqualTo(RootResolutionSource.RecycleBin));
		});
	}

	[Test]
	public void Trashed_document_with_an_unknown_parent_key_resolves_to_the_recycle_bin()
	{
		Guid? binParent = Guid.NewGuid();
		_navigation.Setup(navigation => navigation.TryGetParentKeyInBin(TrashedPage, out binParent)).Returns(true);

		RootNodeResolution resolution = _resolver.Resolve(TrashedPage, parentKey: Guid.NewGuid());

		Assert.That(resolution.Source, Is.EqualTo(RootResolutionSource.RecycleBin));
	}

	[Test]
	public void Trashed_document_with_a_live_parent_key_resolves_to_the_recycle_bin_not_the_parent()
	{
		// A direct API call can send a trashed document's key with a parent that is still in the tree: the document's own
		// place wins, as it does for a document in the tree.
		Guid? binParent = null;
		_navigation.Setup(navigation => navigation.TryGetParentKeyInBin(TrashedPage, out binParent)).Returns(true);
		Ancestors(Section, Section, Root);

		RootNodeResolution resolution = _resolver.Resolve(TrashedPage, parentKey: Section);

		Assert.That(resolution, Is.SameAs(RootNodeResolution.RecycleBin));
		IEnumerable<Guid> ignored = [];
		_navigation.Verify(navigation => navigation.TryGetAncestorsOrSelfKeys(Section, out ignored), Times.Never, "the parent fallback is not consulted");
	}

	[Test]
	public void Unknown_document_without_parent_resolves_to_none()
	{
		RootNodeResolution resolution = _resolver.Resolve(Guid.NewGuid(), parentKey: null);

		Assert.That(resolution, Is.SameAs(RootNodeResolution.None));
	}

	[Test]
	public void Unknown_document_with_unknown_parent_resolves_to_none()
	{
		RootNodeResolution resolution = _resolver.Resolve(Guid.NewGuid(), parentKey: Guid.NewGuid());

		Assert.That(resolution, Is.SameAs(RootNodeResolution.None));
	}

	[Test]
	public void Empty_ancestor_list_is_treated_as_unknown()
	{
		IEnumerable<Guid> none = [];
		_navigation.Setup(navigation => navigation.TryGetAncestorsOrSelfKeys(Page, out none)).Returns(true);

		RootNodeResolution resolution = _resolver.Resolve(Page, parentKey: null);

		Assert.That(resolution, Is.SameAs(RootNodeResolution.None));
	}

	[Test]
	public void Root_name_comes_from_the_document_entity_and_is_cached_per_key()
	{
		Entity(Root, "Corporate site");
		Entity(Section, "Campaign site");

		Assert.Multiple(() =>
		{
			Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate site"));
			Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate site"));
			Assert.That(_resolver.GetRootName(Section), Is.EqualTo("Campaign site"));
		});

		_entityService.Verify(service => service.Get(Root, UmbracoObjectTypes.Document), Times.Once);
		_entityService.Verify(service => service.Get(Section, UmbracoObjectTypes.Document), Times.Once);
		_entityService.Verify(service => service.Get(It.IsAny<Guid>(), It.Is<UmbracoObjectTypes>(type => type != UmbracoObjectTypes.Document)), Times.Never);
	}

	[Test]
	public void Root_name_is_read_as_a_document_entity_only()
	{
		// A key of another object type (a media item, say) is not a document root: the lookup asks for documents only.
		Guid mediaKey = Guid.NewGuid();
		IEntitySlim media = Mock.Of<IEntitySlim>(entity => entity.Name == "Media folder");
		_entityService.Setup(service => service.Get(mediaKey, UmbracoObjectTypes.Media)).Returns(media);
		_entityService.Setup(service => service.Get(mediaKey)).Returns(media);

		Assert.That(_resolver.GetRootName(mediaKey), Is.Null);
		_entityService.Verify(service => service.Get(mediaKey, UmbracoObjectTypes.Document), Times.Once);
	}

	[Test]
	public void Missing_root_name_is_null()
	{
		Assert.That(_resolver.GetRootName(Guid.NewGuid()), Is.Null);
	}

	[Test]
	public void Invalidating_the_name_cache_reads_the_name_again()
	{
		Entity(Root, "Corporate site");
		Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate site"));

		Entity(Root, "Corporate website");
		Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate site"), "still cached");

		_resolver.InvalidateNameCache();

		Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate website"));
		_entityService.Verify(service => service.Get(Root, UmbracoObjectTypes.Document), Times.Exactly(2));
	}

	[Test]
	public void A_name_read_that_overlaps_an_invalidation_is_not_cached()
	{
		// The read returns the old name, and the rename's invalidation lands while that read is still in progress.
		IEntitySlim oldName = Mock.Of<IEntitySlim>(entity => entity.Name == "Corporate site");
		_entityService
			.Setup(service => service.Get(Root, UmbracoObjectTypes.Document))
			.Callback(() => _resolver.InvalidateNameCache())
			.Returns(oldName);

		Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate site"), "the overlapping read still answers");

		Entity(Root, "Corporate website");
		Assert.That(_resolver.GetRootName(Root), Is.EqualTo("Corporate website"), "the stale name was not kept");
	}

	private void Ancestors(Guid key, params Guid[] selfToRoot)
	{
		IEnumerable<Guid> keys = selfToRoot;
		_navigation.Setup(navigation => navigation.TryGetAncestorsOrSelfKeys(key, out keys)).Returns(true);
	}

	private void Entity(Guid key, string name)
	{
		var entity = new Mock<IEntitySlim>();
		entity.SetupGet(item => item.Name).Returns(name);
		_entityService.Setup(service => service.Get(key, UmbracoObjectTypes.Document)).Returns(entity.Object);
	}
}
