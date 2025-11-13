// No ANPR.Core/Program.cs

using ANPR.Shared; // <-- Precisamos da interface
using OpenCvSharp; // <-- Precisamos do Cv2.ImShow e Mat
using System;

namespace ANPR.Core
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(">>> SISTEMA ANPR INICIADO <<<");

            // 1. Definir o nome do vídeo de demo (para a Fase 5)
            string videoFallback = "video_teste.mp4";

            // 2. Criar a variável POO (contrato)
            IVideoSource videoSource;

            // 3. TENTAR ABRIR A CÂMERA REAL
            Console.WriteLine("[INFO] Tentando abrir a câmera (DroidCam)...");
            videoSource = new LiveCameraSource(0); // 0 = primeira câmera

            if (!videoSource.Open())
            {
                // 4. SE FALHAR, TENTAR O VÍDEO DE FALLBACK
                Console.WriteLine("[AVISO] Câmera não encontrada.");
                Console.WriteLine($"[INFO] Tentando carregar o vídeo: '{videoFallback}'...");

                videoSource = new VideoFileSource(videoFallback);

                if (!videoSource.Open())
                {
                    // 5. SE AMBOS FALHAREM, DESISTIR.
                    Console.WriteLine("[ERRO FATAL] Falha ao abrir a câmera E o vídeo de fallback.");
                    Console.WriteLine("Verifique se a câmera está conectada ou se o 'video_teste.mp4' existe.");
                    Console.WriteLine("Pressione qualquer tecla para sair...");
                    Console.ReadKey();
                    return; // Encerra o programa
                }
            }

            Console.WriteLine("[INFO] Fonte de vídeo carregada com sucesso.");

            // (O 'using' garante que o método 'Dispose' será chamado no final)
            using (var processador = new AnprProcessor(videoSource))
            {
                processador.StartProcessing();
            }

            Console.WriteLine(">>> SISTEMA ANPR ENCERRADO <<<");
        }
    }
}