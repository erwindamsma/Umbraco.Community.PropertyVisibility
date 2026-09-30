namespace Umbraco.Community.PropertyVisibility.Configuration;

/// <summary>
///     Stable issue codes reported by the validator, the API's <c>warnings</c> list and the health check.
/// </summary>
/// <remarks>
///     <c>PV0xx</c> are structural configuration errors, <c>PV1xx</c> concern sites, rule sets and roots, <c>PV2xx</c> concern
///     content types, <c>PV3xx</c> are informational. Codes never change meaning once released.
/// </remarks>
public static class IssueCodes
{
	/// <summary>
	///     The rules file cannot be read or parsed: invalid JSON (reported with line and position), a value of the wrong
	///     type, a key given twice (keys are case-insensitive), a file larger than 1 MiB, or a file that cannot be opened
	///     (a locked file is read again automatically). The last valid version of the file stays in effect; without one,
	///     the file contributes no rules.
	/// </summary>
	public const string InvalidJson = "PV001";

	/// <summary>
	///     The rules file contains a key the model does not know, reported with its JSON path and, when a known key is
	///     close, a did-you-mean suggestion. Handled like <see cref="InvalidJson" />: the last valid version stays in effect.
	/// </summary>
	public const string UnknownKey = "PV002";

	/// <summary>A site has none of <c>RootNodeKey</c>, <c>RootNodeName</c> or <c>IsDefault</c>.</summary>
	public const string SiteWithoutIdentity = "PV003";

	/// <summary>Two sites share the same <c>RootNodeKey</c> or the same <c>RootNodeName</c>.</summary>
	public const string DuplicateSiteIdentity = "PV004";

	/// <summary>More than one site is marked <c>IsDefault</c>.</summary>
	public const string MultipleDefaultSites = "PV005";

	/// <summary>
	///     A container alias is not <c>tab</c>, <c>group</c> or <c>tab/group</c> (split at the first <c>/</c>; neither part
	///     empty).
	/// </summary>
	public const string InvalidContainerAlias = "PV006";

	/// <summary>A site label contains a colon, which the configuration system reserves as a path separator.</summary>
	public const string InvalidSiteLabel = "PV007";

	/// <summary>
	///     The configuration cannot be bound (a value of the wrong type, an unknown key) or fails validation; nothing is
	///     hidden until it is fixed.
	/// </summary>
	public const string ConfigurationInvalid = "PV008";

	/// <summary>A site's <c>Include</c> names a rule set that does not exist.</summary>
	public const string UnknownRuleSet = "PV009";

	/// <summary>A rule set name contains a colon, which the configuration system reserves as a path separator.</summary>
	public const string InvalidRuleSetName = "PV010";

	/// <summary><c>RootNodeKey</c> is not a root node.</summary>
	public const string RootNodeKeyNotARoot = "PV101";

	/// <summary>
	///     <c>RootNodeName</c> matches no root node, or only a root node that another site claims by <c>RootNodeKey</c>,
	///     so the site never applies to it.
	/// </summary>
	public const string RootNodeNameNotFound = "PV102";

	/// <summary>A root node has no matching site and no site is the default.</summary>
	public const string RootWithoutSite = "PV103";

	/// <summary>A content type alias is unknown.</summary>
	public const string UnknownContentTypeAlias = "PV104";

	/// <summary>A site matched by key but its configured <c>RootNodeName</c> differs from the root's current name.</summary>
	public const string RootNodeNameDrift = "PV105";

	/// <summary>A rule set is not included by any site, so its rules never apply.</summary>
	public const string UnusedRuleSet = "PV106";

	/// <summary>A property alias does not exist on the content type.</summary>
	public const string UnknownPropertyAlias = "PV201";

	/// <summary>A container alias does not exist on the content type.</summary>
	public const string UnknownContainerAlias = "PV202";

	/// <summary>A hidden property is mandatory; publishing still requires it.</summary>
	public const string HiddenMandatoryProperty = "PV203";

	/// <summary>The content type key of a hidden-fields request does not exist (a request-time condition, not a configured alias).</summary>
	public const string UnknownContentTypeKey = "PV204";

	/// <summary>
	///     Informational: a content type entry is keyed by a composition, so its rules also apply to every content type
	///     composed of it (directly or transitively, parent document types included), where they hide only what the
	///     composition contributes. Lists the types it reaches.
	/// </summary>
	public const string CompositionRuleReach = "PV205";

	/// <summary>
	///     Informational: a content type entry lists no properties and no containers (or is <c>null</c>), so it hides nothing.
	/// </summary>
	public const string EmptyContentTypeRule = "PV206";

	/// <summary>
	///     Appsettings defines rules (<c>ContentTypes</c>, <c>RuleSets</c> or <c>Sites</c>) while the rules file is in use;
	///     the file wins and the appsettings rules are ignored.
	/// </summary>
	public const string BothSourcesDefineRules = "PV301";

	/// <summary>The package is disabled.</summary>
	public const string Disabled = "PV302";

	/// <summary>The running Umbraco version is not in the tested list.</summary>
	public const string UntestedUmbracoVersion = "PV303";

	/// <summary>
	///     The rules file cannot be watched (its folder does not exist or cannot be read, or the system's file watcher limit
	///     is reached): edits to it take effect after a restart or an appsettings change. The next appsettings reload tries
	///     again.
	/// </summary>
	public const string ConfigFileNotWatched = "PV304";
}
