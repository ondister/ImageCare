using ImageCare.Core.Domain.Folders;
using ImageCare.Core.Domain.MediaFormats;
using ImageCare.Core.Domain.Preview;
using ImageCare.Core.Services.MediaPreviewOperationsService;

namespace ImageCare.Core.Tests.Services.MediaPreviewOperationsService;

[TestFixture]
public class MediaPreviewNavigationServiceTests
{
    [Test]
    public void SetSelected_UpdatesGlobalLastSelectedBeforePublishing()
    {
        using var service = new MediaPreviewNavigationService();
        var selectedPreview = CreateSelectedMediaPreview(FileManagerPanel.Left, "left.jpg");
        MediaPreview? lastSelectedObservedBySubscriber = null;
        SelectedMediaPreview? publishedPreview = null;
        service.ImagePreviewSelected.Subscribe(preview =>
        {
            publishedPreview = preview;
            lastSelectedObservedBySubscriber = service.GetLastSelected();
        });

        service.SetSelected(selectedPreview);

        Assert.Multiple(() =>
        {
            Assert.That(publishedPreview, Is.SameAs(selectedPreview));
            Assert.That(lastSelectedObservedBySubscriber, Is.SameAs(selectedPreview));
            Assert.That(service.GetLastSelected(), Is.SameAs(selectedPreview));
        });
    }

    [Test]
    public void Observables_DoNotReplayEarlierNotifications()
    {
        using var service = new MediaPreviewNavigationService();
        var earlierPreview = CreateSelectedMediaPreview(FileManagerPanel.Left, "earlier.jpg");
        service.SetSelected(earlierPreview);
        service.RequestNavigation(earlierPreview);
        var selectedNotifications = new List<SelectedMediaPreview>();
        var navigationNotifications = new List<SelectedMediaPreview>();

        service.ImagePreviewSelected.Subscribe(selectedNotifications.Add);
        service.MediaPreviewNavigationRequested.Subscribe(navigationNotifications.Add);

        Assert.Multiple(() =>
        {
            Assert.That(selectedNotifications, Is.Empty);
            Assert.That(navigationNotifications, Is.Empty);
        });
    }

    [Test]
    public void SetSelected_AcrossPanels_KeepsMostRecentPreviewGlobally()
    {
        using var service = new MediaPreviewNavigationService();
        var leftPreview = CreateSelectedMediaPreview(FileManagerPanel.Left, "left.jpg");
        var rightPreview = CreateSelectedMediaPreview(FileManagerPanel.Right, "right.jpg");

        service.SetSelected(leftPreview);
        service.SetSelected(rightPreview);

        Assert.That(service.GetLastSelected(), Is.SameAs(rightPreview));
    }

    [Test]
    public void RequestNavigation_PublishesWithoutChangingLastSelected()
    {
        using var service = new MediaPreviewNavigationService();
        var selectedPreview = CreateSelectedMediaPreview(FileManagerPanel.Left, "selected.jpg");
        var requestedPreview = CreateSelectedMediaPreview(FileManagerPanel.Right, "requested.jpg");
        SelectedMediaPreview? publishedPreview = null;
        service.SetSelected(selectedPreview);
        service.MediaPreviewNavigationRequested.Subscribe(preview => publishedPreview = preview);

        service.RequestNavigation(requestedPreview);

        Assert.Multiple(() =>
        {
            Assert.That(publishedPreview, Is.SameAs(requestedPreview));
            Assert.That(service.GetLastSelected(), Is.SameAs(selectedPreview));
        });
    }

    [Test]
    public void Dispose_PreservesLastSelectedAndDisposesBothSubjects()
    {
        var service = new MediaPreviewNavigationService();
        var selectedPreview = CreateSelectedMediaPreview(FileManagerPanel.Left, "selected.jpg");
        service.SetSelected(selectedPreview);

        service.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(service.GetLastSelected(), Is.SameAs(selectedPreview));
            Assert.That(
                () => service.ImagePreviewSelected.Subscribe(_ => { }),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.MediaPreviewNavigationRequested.Subscribe(_ => { }),
                Throws.TypeOf<ObjectDisposedException>());
            Assert.That(
                () => service.RequestNavigation(selectedPreview),
                Throws.TypeOf<ObjectDisposedException>());
        });
    }

    private static SelectedMediaPreview CreateSelectedMediaPreview(
        FileManagerPanel panel,
        string path)
    {
        var preview = new MediaPreview("Test", path, MediaFormat.MediaFormatJpg, 800);
        return new SelectedMediaPreview(preview, panel);
    }
}
