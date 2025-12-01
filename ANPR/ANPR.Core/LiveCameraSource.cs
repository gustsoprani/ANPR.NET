using ANPR.Shared.Interfaces;
using OpenCvSharp;
using System;

namespace ANPR.Core
{
    public class LiveCameraSource : IVideoSource
    {
        private readonly int _cameraIndex;
        private VideoCapture? _capture;
        private int _frameCount = 0;

        public LiveCameraSource(int cameraIndex = 0)
        {
            _cameraIndex = cameraIndex;
            _capture = new VideoCapture(_cameraIndex);
        }

        // --- Implementação da Interface IVideoSource ---

        public bool IsAvailable => _capture?.IsOpened() ?? false;

        public int FrameCount => _frameCount;

        // Câmera ao vivo não tem "Total de Frames" fixo, retornamos -1
        public int TotalFrames => -1;

        public double CurrentFps => _capture != null ? _capture.Get(VideoCaptureProperties.Fps) : 0;

        public Mat GetNextFrame()
        {
            if (_capture == null || !_capture.IsOpened())
                return new Mat();

            var frame = new Mat();
            _capture.Read(frame);

            if (!frame.Empty())
            {
                _frameCount++;
            }

            return frame;
        }

        public void Dispose()
        {
            _capture?.Release();
            _capture?.Dispose();
        }
    }
}