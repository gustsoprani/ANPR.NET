// No ANPR.Core/LiveCameraSource.cs

using ANPR.Shared; // <-- Precisamos do nosso contrato!
using OpenCvSharp;

namespace ANPR.Core
{
    // Esta classe "herda" (assina) o contrato IVideoSource
    public class LiveCameraSource : IVideoSource
    {
        private readonly int _cameraIndex;
        private VideoCapture? _capture;

        // "cameraIndex = 0" significa "pegar a primeira câmera"
        public LiveCameraSource(int cameraIndex = 0)
        {
            _cameraIndex = cameraIndex;
        }

        public bool Open()
        {
            _capture = new VideoCapture(_cameraIndex);
            return _capture.IsOpened();
        }

        public Mat GetNextFrame()
        {
            var frame = new Mat();
            _capture?.Read(frame); // Lê um frame da câmera
            return frame;
        }

        public bool IsOpened() => _capture?.IsOpened() ?? false;

        public void Dispose()
        {
            // Limpa os recursos da câmera
            _capture?.Release();
            _capture?.Dispose();
        }
    }
}