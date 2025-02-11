using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

namespace VisorClient
{
    public class VisorGrpcClient:Visor.VisorClient
    {

      

        /// <inheritdoc />
        public override AsyncUnaryCall<Empty> StreamImageAsync(ImageData request, CallOptions options)
        {
            return base.StreamImageAsync(request, options);
        }
    }
}
