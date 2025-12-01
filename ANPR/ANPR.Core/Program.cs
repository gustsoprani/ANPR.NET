using System;
using ANPR.Core.Services; // Namespace dos novos serviços
using ANPR.Core.VideoSources; // Namespace das fontes de vídeo
using ANPR.Shared.Interfaces;

namespace ANPR.Core
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(">>> INICIALIZANDO SISTEMA ANPR MODULAR <<<");

            // 1. Configurar Caminhos (Verifique se os arquivos existem nestas pastas!)
            string modeloYolo = "models/best.onnx";
            string tessData = "tessdata";
            string videoTeste = "video_teste.mp4";

            try
            {
                // 2. Instanciar as "Ferramentas" (Serviços)
                // Os componentes agora são independentes. Se um falhar, sabemos exatamente qual.

                Console.WriteLine("[Init] Inicializando Câmera/Vídeo...");
                // Tenta câmera (0), se falhar, vai para null ou trate com try-catch
                IVideoSource videoSource = new VideoFileSource(videoTeste);
                // Nota: Se tiver a classe LiveCameraSource, use: new LiveCameraSource(0);

                Console.WriteLine("[Init] Inicializando Detector YOLO...");
                IPlateDetector yoloService = new YoloDetectionService(modeloYolo, confidenceThreshold: 0.4f);

                Console.WriteLine("[Init] Inicializando OCR Tesseract...");
                IOcrEngine ocrService = new TesseractOcrService(tessData);

                Console.WriteLine("[Init] Inicializando Banco de Dados...");
                IAccessDatabase dbService = new AccessDatabaseService();

                // 3. Injetar tudo no Processador Principal
                // Aqui "ligamos" os componentes
                using (var processor = new AnprProcessor(
                    videoSource,
                    yoloService,
                    ocrService,
                    dbService,
                    framesPerDetection: 5, // Otimização: YOLO a cada 5 frames
                    framesToConfirm: 3     // Precisa de 3 confirmações para abrir
                ))
                {
                    // 4. Rodar o loop
                    processor.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("Pressione ENTER para fechar...");
            Console.ReadLine();
        }
    }
}