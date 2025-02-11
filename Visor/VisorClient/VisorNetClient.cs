using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using ImageCare.Core.Domain;
using ImageCare.Core.Services;

namespace VisorClient
{
    public sealed class VisorNetClient:IVisorClient
    {
        private readonly DiscoveryClient _discoverClient;
        private const int broadcastPort = 55125;

        /// <inheritdoc />
        public IObservable<MediaPreview> ImagePreviewReceived { get; }

        public VisorNetClient()
        {
            _discoverClient = new DiscoveryClient(broadcastPort);

        }

        public async Task<bool> StartClientAsync()
        {
            var serverAddress = await _discoverClient.DiscoverServerAsync(TimeSpan.FromSeconds(15));

            return true;
        }
    }
}
