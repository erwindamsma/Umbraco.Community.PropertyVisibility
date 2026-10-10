using Umbraco.Community.PropertyVisibility.Notifications;

namespace Umbraco.Community.PropertyVisibility.Tests.Notifications;

[TestFixture]
public sealed class ConfigurationAnalysisTriggerTests
{
	[Test]
	public void A_request_reaches_every_listener_until_it_unsubscribes()
	{
		var trigger = new ConfigurationAnalysisTrigger();
		var logger = 0;
		var schemaWriter = 0;
		IDisposable loggerSubscription = trigger.Subscribe(() => logger++);
		using IDisposable schemaWriterSubscription = trigger.Subscribe(() => schemaWriter++);

		trigger.Request();
		loggerSubscription.Dispose();
		trigger.Request();

		Assert.Multiple(() =>
		{
			Assert.That(logger, Is.EqualTo(1));
			Assert.That(schemaWriter, Is.EqualTo(2));
		});
	}
}
