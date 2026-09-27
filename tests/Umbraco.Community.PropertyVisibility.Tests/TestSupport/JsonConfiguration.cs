using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Community.PropertyVisibility.Composing;
using Umbraco.Community.PropertyVisibility.Configuration;

namespace Umbraco.Community.PropertyVisibility.Tests.TestSupport;

/// <summary>
///     A real appsettings-style JSON file in a temporary folder, bound through the package's own options registration
///     (<see cref="UmbracoBuilderExtensions.AddPropertyVisibilityOptions" />), with a real <see cref="IOptionsMonitor{TOptions}" />.
///     The file can be rewritten and reloaded to exercise configuration changes.
/// </summary>
internal sealed class JsonConfiguration : IDisposable
{
	private const string FileName = "appsettings.json";

	private readonly string _directory;
	private readonly IConfigurationRoot _configuration;
	private readonly ServiceProvider _services;

	/// <summary>Creates the file with the given content and builds the configuration and the service provider.</summary>
	/// <param name="json">The whole appsettings file.</param>
	public JsonConfiguration(string json)
	{
		_directory = Path.Combine(Path.GetTempPath(), "pv-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
		File.WriteAllText(Path.Combine(_directory, FileName), json);

		_configuration = new ConfigurationBuilder()
			.SetBasePath(_directory)
			.AddJsonFile(FileName, optional: false, reloadOnChange: false)
			.Build();

		var services = new ServiceCollection();
		UmbracoBuilderExtensions.AddPropertyVisibilityOptions(services, _configuration);
		_services = services.BuildServiceProvider();
	}

	/// <summary>The options monitor, as the service receives it.</summary>
	public IOptionsMonitor<PropertyVisibilityOptions> Monitor => _services.GetRequiredService<IOptionsMonitor<PropertyVisibilityOptions>>();

	/// <summary>Wraps a <c>PropertyVisibility</c> section value in an appsettings document.</summary>
	/// <param name="section">The JSON object of the section.</param>
	/// <returns>The whole file.</returns>
	public static string Appsettings(string section) => $$"""{ "PropertyVisibility": {{section}} }""";

	/// <summary>
	///     Rewrites the file and reloads the configuration, as a file watcher would. A change listener that throws (the
	///     options monitor does when the new options cannot be built) surfaces here as an <see cref="AggregateException" />;
	///     in a running site it is lost on the watcher's thread, so it is swallowed here too.
	/// </summary>
	/// <param name="json">The new file content.</param>
	public void Change(string json)
	{
		File.WriteAllText(Path.Combine(_directory, FileName), json);
		try
		{
			_configuration.Reload();
		}
		catch (AggregateException)
		{
		}
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_services.Dispose();
		try
		{
			Directory.Delete(_directory, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
