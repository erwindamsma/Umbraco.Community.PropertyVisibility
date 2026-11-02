using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Tests.Services;

[TestFixture]
public sealed class UmbracoVersionSupportTests
{
	private static readonly IReadOnlyList<Version> Tested = [new Version(17, 6, 2), new Version(17, 7, 0), new Version(17, 7, 1), new Version(17, 8, 0)];

	[TestCase("17.6.2", true)]
	[TestCase("17.6.9", true)]
	[TestCase("17.7.0", true)]
	[TestCase("17.7.4", true)]
	[TestCase("17.8.1", true)]
	[TestCase("17.6.1", false)]
	[TestCase("17.9.0", false)]
	[TestCase("17.5.3", false)]
	[TestCase("18.8.0", false)]
	[TestCase("16.6.2", false)]
	public void A_verified_version_and_its_later_patches_are_tested(string version, bool expected)
		=> Assert.That(UmbracoVersionSupport.IsTested(Version.Parse(version), Tested), Is.EqualTo(expected));

	[Test]
	public void Nothing_is_tested_without_verified_versions()
		=> Assert.That(UmbracoVersionSupport.IsTested(new Version(17, 8, 0), []), Is.False);

	[Test]
	public void The_description_names_each_minor_once_with_its_first_verified_patch()
		=> Assert.That(
			UmbracoVersionSupport.Describe([new Version(17, 8, 0), new Version(17, 7, 1), new Version(17, 6, 2), new Version(17, 7, 0)]),
			Is.EqualTo("17.6.x from 17.6.2, 17.7.x, 17.8.x"));
}
