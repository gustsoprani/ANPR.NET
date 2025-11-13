// No ANPR.Core/VideoFileSource.cs

using ANPR.Shared; // <-- Nosso contrato POO
using OpenCvSharp;
using System.IO;

namespace ANPR.Core
{
    // Esta classe TAMBÉM herda (assina) o mesmo contrato IVideoSource
    public class VideoFileSource : IVideoSource
    {
        private readonly string _filePath;
        private VideoCapture? _capture;

        public VideoFileSource(string filePath)
        {
            _filePath = filePath;
        }

        public bool Open()
        {
            // A única diferença: em vez de um número (0), 
            // passamos o caminho do arquivo de vídeo.
            if (!File.Exists(_filePath))
            {
                Console.WriteLine($"[ERRO] Arquivo de vídeo não encontrado: {_filePath}");
                return false;
            }

            _capture = new VideoCapture(_filePath);
            return _capture.IsOpened();
        }

        public Mat GetNextFrame()
        {
            var frame = new Mat();
            _capture?.Read(frame);

            // LÓGICA EXTRA: Se o vídeo acabar, reinicie!
            // Isso faz com que a demo rode em loop.
            if (frame.Empty())
            {
                Console.WriteLine("[INFO] Fim do vídeo. Reiniciando...");
                _capture?.Set(VideoCaptureProperties.PosFrames, 0); // Volta para o frame 0
                _capture?.Read(frame); // Lê o primeiro frame
            }

            return frame;
        }

        public bool IsOpened() => _capture?.IsOpened() ?? false;

        public void Dispose()
        {
            // Limpa os recursos
            _capture?.Release();
            _capture?.Dispose();
        }
    }
}