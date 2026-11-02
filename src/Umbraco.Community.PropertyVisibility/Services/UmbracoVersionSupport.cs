namespace Umbraco.Community.PropertyVisibility.Services;

/// <summary>
///     Whether an Umbraco version counts as tested, given the verified versions in
///     <see cref="Constants.TestedUmbracoVersions" />.
/// </summary>
/// <remarks>
///     Verification is per minor: a version is tested when a verified version has the same major and minor and a patch at
///     or below its own, so 17.8.1 is covered by 17.8.0 and 17.6.1 is not covered by 17.6.2. The weekly compatibility run
///     guards the patches: it tests the latest one of each major and opens an issue when it fails.
/// </remarks>
internal static class UmbracoVersionSupport
{
	/// <summary>
	///     Returns whether <paramref name="version" /> is a verified version or a later patch of one.
	/// </summary>
	/// <param name="version">The Umbraco version.</param>
	/// <param name="tested">The verified versions.</param>
	/// <returns><c>true</c> when the version counts as tested.</returns>
	public static bool IsTested(Version version, IReadOnlyList<Version> tested)
		=> tested.Any(verified => verified.Major == version.Major && verified.Minor == version.Minor && verified.Build <= version.Build);

	/// <summary>
	///     Describes the verified versions per minor, for example <c>17.6.x from 17.6.2, 17.7.x</c>.
	/// </summary>
	/// <param name="tested">The verified versions.</param>
	/// <returns>One entry per minor, in version order.</returns>
	public static string Describe(IReadOnlyList<Version> tested)
		=> string.Join(", ", tested
			.GroupBy(version => (version.Major, version.Minor))
			.OrderBy(minor => minor.Key)
			.Select(minor =>
			{
				Version first = minor.Min()!;
				return first.Build > 0 ? $"{first.Major}.{first.Minor}.x from {first}" : $"{first.Major}.{first.Minor}.x";
			}));
}
