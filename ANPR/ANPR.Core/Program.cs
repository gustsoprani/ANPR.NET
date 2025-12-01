using System;
using System.Threading.Tasks; // Necessário para Task
using ANPR.Core.Services;
using ANPR.Shared.Interfaces;

namespace ANPR.Core
{
    class Program
    {
        // Mudamos 'void' para 'async Task' para poder usar 'await'
        static async Task Main(string[] args)
        {
            Console.WriteLine(">>> SISTEMA ANPR INICIADO (CONSOLE) <<<");

            string modeloYolo = "models/best.onnx";
            string tessData = "tessdata";
            string videoTeste = "video_teste.mp4";

            try
            {
                Console.WriteLine("[Init] Inicializando Serviços...");

                // Configuração básica para teste em Console
                IVideoSource videoSource = new LiveCameraSource(0);
                if (!videoSource.IsAvailable) videoSource = new VideoFileSource(videoTeste);

                IPlateDetector yoloService = new YoloDetectionService(modeloYolo, confidenceThreshold: 0.4f);
                IOcrEngine ocrService = new TesseractOcrService(tessData);
                IAccessDatabase dbService = new AccessDatabaseService();

                using (var processor = new AnprProcessor(
                    videoSource,
                    yoloService,
                    ocrService,
                    dbService
                ))
                {
                    // CORREÇÃO AQUI: Usamos StartAsync com await
                    await processor.StartAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] {ex.Message}");
            }
        }
    }
}