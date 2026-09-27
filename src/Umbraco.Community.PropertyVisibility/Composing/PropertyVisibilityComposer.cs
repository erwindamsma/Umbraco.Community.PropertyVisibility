using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Configuration;
using Umbraco.Cms.Core.DependencyInjection;

namespace Umbraco.Community.PropertyVisibility.Composing;

/// <summary>
///     Registers the package on startup; the host adds nothing.
/// </summary>
public sealed class PropertyVisibilityComposer : IComposer
{
	/// <inheritdoc />
	public void Compose(IUmbracoBuilder builder)
	{
		builder.AddPropertyVisibility();

		Version running = new UmbracoVersion().Version;
		if (running.Major != Constants.SupportedUmbracoMajor)
		{
			builder.BuilderLoggerFactory
				.CreateLogger<PropertyVisibilityComposer>()
				.LogWarning(
					"Umbraco.Community.PropertyVisibility supports Umbraco {SupportedMajor}.x (tested: {TestedVersions}), but this site runs Umbraco {RunningVersion}: tabs and groups may not be hidden. See {CompatibilityUrl}",
					Constants.SupportedUmbracoMajor,
					string.Join(", ", Constants.TestedUmbracoVersions),
					running,
					Constants.CompatibilityDocumentationUrl);
		}
	}
}
