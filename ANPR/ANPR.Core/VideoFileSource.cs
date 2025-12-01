using ANPR.Shared.Interfaces;
using OpenCvSharp;
using System;
using System.IO;

namespace ANPR.Core
{
    public class VideoFileSource : IVideoSource
    {
        private readonly string _filePath;
        private VideoCapture? _capture;
        private int _frameCount = 0;

        public VideoFileSource(string filePath)
        {
            _filePath = filePath;
            // Tenta abrir imediatamente ao criar
            if (File.Exists(_filePath))
            {
                _capture = new VideoCapture(_filePath);
            }
        }

        // --- Implementação da Interface IVideoSource ---

        public bool IsAvailable => _capture?.IsOpened() ?? false;

        public int FrameCount => _frameCount;

        public int TotalFrames => _capture != null ? (int)_capture.Get(VideoCaptureProperties.FrameCount) : 0;

        public double CurrentFps => _capture != null ? _capture.Get(VideoCaptureProperties.Fps) : 0;

        public Mat GetNextFrame()
        {
            if (_capture == null || !_capture.IsOpened())
                return new Mat();

            var frame = new Mat();
            _capture.Read(frame);

            // Loop infinito: Se o vídeo acabar, reinicia
            if (frame.Empty())
            {
                Console.WriteLine("[INFO] Fim do vídeo. Reiniciando...");
                _capture.Set(VideoCaptureProperties.PosFrames, 0);
                _frameCount = 0; // Reseta contador
                _capture.Read(frame);
            }
            else
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