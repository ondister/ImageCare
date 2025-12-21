using ImageCare.Core.Domain.Notification;
using ImageCare.Core.Exceptions;
using ImageCare.Core.Services.NotificationService;

namespace ImageCare.Core.Tests.Services.NotificationService;

[TestFixture]
public class LocalNotificationServiceTests
{
	private LocalNotificationService _service;

	[SetUp]
	public void Setup()
	{
		_service = new LocalNotificationService();
	}

	[TearDown]
	public void TearDown()
	{
		_service?.Dispose();
	}

	[Test]
	public void SendNotification_ValidNotification_ShouldEmitToObservable()
	{
		var expectedNotification = new SuccessNotification("Test", "Description");
		Notification actualNotification = null;

		using var subscription = _service.NotificationReceived
		                                 .Subscribe(notification => actualNotification = notification);

		_service.SendNotification(expectedNotification);

		Assert.That(actualNotification, Is.EqualTo(expectedNotification));
	}

	[Test]
	public void SendNotification_MultipleSubscribers_AllShouldReceiveNotification()
	{
		var notification = new ErrorNotification("Error", "Something went wrong");
		var receivedCount = 0;

		using var sub1 = _service.NotificationReceived.Subscribe(_ => receivedCount++);
		using var sub2 = _service.NotificationReceived.Subscribe(_ => receivedCount++);
		using var sub3 = _service.NotificationReceived.Subscribe(_ => receivedCount++);

		_service.SendNotification(notification);

		Assert.That(receivedCount, Is.EqualTo(3));
	}

	[Test]
	public void SendNotification_NullNotification_ShouldThrowServiceException()
	{
		var exception = Assert.Throws<ServiceException>(() => _service.SendNotification(null));
		Assert.That(exception.Message, Is.EqualTo("Notification cannot be null"));
	}

	[Test]
	public void SendNotification_EmptyTitle_ShouldThrowServiceException()
	{
		var invalidNotification = new SuccessNotification("", "Valid description");

		var exception = Assert.Throws<ServiceException>(() => _service.SendNotification(invalidNotification));
		Assert.That(exception.Message, Is.EqualTo("Notification title cannot be null or empty"));
	}

	[Test]
	public void SendNotification_WhitespaceTitle_ShouldThrowServiceException()
	{
		var invalidNotification = new SuccessNotification("   ", "Valid description");

		var exception = Assert.Throws<ServiceException>(() => _service.SendNotification(invalidNotification));
		Assert.That(exception.Message, Is.EqualTo("Notification title cannot be null or empty"));
	}

	[Test]
	public void SendNotification_AfterDispose_ShouldThrowServiceException()
	{
		_service.Dispose();

		var notification = new SuccessNotification("Test", "Description");

		var exception = Assert.Throws<ServiceException>(() => _service.SendNotification(notification));
		Assert.That(exception.Message, Is.EqualTo("Notification service is disposed"));
	}

	[Test]
	public void NotificationReceived_AfterDispose_ShouldCompleteObservable()
	{
		var isCompleted = false;

		using var subscription = _service.NotificationReceived
		                                 .Subscribe(
			                                 onNext: _ => { },
			                                 onCompleted: () => isCompleted = true);

		_service.Dispose();

		Assert.That(isCompleted, Is.True);
	}

	[Test]
	public void NotificationReceived_Unsubscribe_ShouldStopReceivingNotifications()
	{
		var receivedCount = 0;
		var notification = new SuccessNotification("Test", "Description");

		var subscription = _service.NotificationReceived.Subscribe(_ => receivedCount++);

		_service.SendNotification(notification);
		subscription.Dispose();
		_service.SendNotification(notification);

		Assert.That(receivedCount, Is.EqualTo(1));
	}

	[Test]
	public void SendNotification_DifferentNotificationTypes_ShouldBeHandledCorrectly()
	{
		var successNotification = new SuccessNotification("Success", "Operation completed");
		var errorNotification = new ErrorNotification("Error", "Operation failed");
		Notification lastReceived = null;

		using var subscription = _service.NotificationReceived
		                                 .Subscribe(notification => lastReceived = notification);

		_service.SendNotification(successNotification);
		Assert.That(lastReceived, Is.TypeOf<SuccessNotification>());

		_service.SendNotification(errorNotification);
		Assert.That(lastReceived, Is.TypeOf<ErrorNotification>());
	}

	[Test]
	public void SendNotification_ValidNotificationWithNullDescription_ShouldWorkCorrectly()
	{
		var notification = new SuccessNotification("Title", null);
		Notification receivedNotification = null;

		using var subscription = _service.NotificationReceived
		                                 .Subscribe(n => receivedNotification = n);

		Assert.DoesNotThrow(() => _service.SendNotification(notification));
		Assert.That(receivedNotification.Title, Is.EqualTo("Title"));
		Assert.That(receivedNotification.Description, Is.Null);
	}
}