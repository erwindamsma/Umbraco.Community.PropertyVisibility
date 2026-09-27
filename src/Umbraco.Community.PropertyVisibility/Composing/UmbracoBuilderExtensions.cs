using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.PropertyVisibility.Api.OpenApi;
using Umbraco.Community.PropertyVisibility.Configuration;
using Umbraco.Community.PropertyVisibility.Configuration.ConfigFile;
using Umbraco.Community.PropertyVisibility.Notifications;
using Umbraco.Community.PropertyVisibility.Services;

namespace Umbraco.Community.PropertyVisibility.Composing;

/// <summary>
///     Registers the package. <see cref="PropertyVisibilityComposer" /> calls this automatically; a host only calls it
///     when it has disabled composer discovery for the package.
/// </summary>
public static class UmbracoBuilderExtensions
{
	/// <summary>
	///     Adds the Property Visibility services, options, validator, notification handlers and OpenAPI document.
	///     Idempotent: a second call is a no-op.
	/// </summary>
	/// <param name="builder">The Umbraco builder.</param>
	/// <returns>The same builder, for chaining.</returns>
	public static IUmbracoBuilder AddPropertyVisibility(this IUmbracoBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		if (builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IPropertyVisibilityService)))
		{
			return builder;
		}

		AddPropertyVisibilityOptions(builder.Services, builder.Config);
		AddPropertyVisibilityConfigFile(builder.Services, builder.Config);

		builder.Services.AddSingleton<IRootNodeResolver, RootNodeResolver>();
		builder.Services.AddSingleton<ISiteMatcher, SiteMatcher>();
		builder.Services.AddSingleton<IHiddenFieldsResolver, HiddenFieldsResolver>();
		builder.Services.AddSingleton<IPropertyVisibilityService, PropertyVisibilityService>();
		builder.Services.AddSingleton<IConfigurationAnalyzer, ConfigurationAnalyzer>();

		// One instance for the whole application: it holds the options change subscription. Umbraco resolves
		// notification handlers per publish, so the handler registration hands out that singleton. The trigger has no
		// dependencies, so the content notification handlers that use it never depend on the options.
		builder.Services.AddSingleton<ConfigurationAnalysisTrigger>();
		builder.Services.AddSingleton<ConfigurationAnalysisLogger>();
		builder.Services.AddSingleton<Umbraco.Cms.Core.Events.INotificationHandler<UmbracoApplicationStartedNotification>>(
			provider => provider.GetRequiredService<ConfigurationAnalysisLogger>());

		builder
			.AddNotificationHandler<ContentSavedNotification, RootNodeNameCacheInvalidator>()
			.AddNotificationHandler<ContentMovedNotification, RootNodeNameCacheInvalidator>()
			.AddNotificationHandler<ContentMovedToRecycleBinNotification, RootNodeNameCacheInvalidator>()
			.AddNotificationHandler<ContentDeletedNotification, RootNodeNameCacheInvalidator>()
			.AddNotificationHandler<ContentCacheRefresherNotification, RootNodeNameCacheInvalidator>();

		builder.Services.ConfigureOptions<PropertyVisibilitySwaggerGenOptionsSetup>();

		return builder;
	}

	/// <summary>
	///     Binds <see cref="PropertyVisibilityOptions" /> from the <c>PropertyVisibility</c> section and registers the
	///     validator. Separate from <see cref="AddPropertyVisibility" /> so tests bind real configuration exactly as the
	///     package does.
	/// </summary>
	/// <remarks>
	///     <see cref="BinderOptions.ErrorOnUnknownConfiguration" /> is on: without it the binder silently drops a
	///     dictionary entry whose value cannot be converted (a <c>RootNodeKey</c> with a missing digit, <c>IsDefault: "yes"</c>),
	///     so a mistyped site would vanish and its documents would quietly match another site. With it, a value of the wrong
	///     type or an unknown key makes reading the options throw, which the service logs and fails open on (<c>PV008</c>).
	/// </remarks>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration root the section is read from.</param>
	internal static void AddPropertyVisibilityOptions(IServiceCollection services, IConfiguration configuration)
	{
		services.AddOptions<PropertyVisibilityOptions>()
			.Bind(configuration.GetSection(PropertyVisibilityOptions.SectionName), binder => binder.ErrorOnUnknownConfiguration = true)
			.ValidateDataAnnotations();

		services.AddSingleton<IValidateOptions<PropertyVisibilityOptions>, PropertyVisibilityOptionsValidator>();
	}

	/// <summary>
	///     Registers the rules-file source: <see cref="ConfigFileOptionsSetup" /> (applied after the appsettings binding, so
	///     call this after <see cref="AddPropertyVisibilityOptions" />), <see cref="ConfigFileChangeTokenSource" /> (reload
	///     on file change, and the retries of a locked file) and <see cref="IConfigurationInfo" />. Needs
	///     <see cref="IHostEnvironment" />, which every ASP.NET Core host registers.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="configuration">The configuration root the section is read from; the watcher reads the file name from it.</param>
	internal static void AddPropertyVisibilityConfigFile(IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<ConfigurationInfo>();
		services.AddSingleton<IConfigurationInfo>(provider => provider.GetRequiredService<ConfigurationInfo>());
		services.AddSingleton(_ => new ConfigFileReadRetry());
		services.AddSingleton<IConfigureOptions<PropertyVisibilityOptions>>(provider => new ConfigFileOptionsSetup(
			provider.GetRequiredService<IHostEnvironment>(),
			provider.GetRequiredService<ConfigurationInfo>(),
			provider.GetRequiredService<ConfigFileReadRetry>(),
			provider.GetRequiredService<ILogger<ConfigFileOptionsSetup>>()));
		services.AddSingleton<IOptionsChangeTokenSource<PropertyVisibilityOptions>>(provider => new ConfigFileChangeTokenSource(
			provider.GetRequiredService<IHostEnvironment>(),
			configuration,
			provider.GetRequiredService<ConfigurationInfo>(),
			provider.GetRequiredService<ConfigFileReadRetry>(),
			provider.GetRequiredService<ILogger<ConfigFileChangeTokenSource>>()));
	}
}
