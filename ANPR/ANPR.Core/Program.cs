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

            // 6. USAR O 'USING' PARA CORRIGIR O "LEAK"
            // (O 'using' garante que o método 'Dispose' será chamado no final)
            using (var processador = new AnprProcessor(videoSource))
            {
                // AQUI É ONDE A MÁGICA VAI ACONTECER
                // (Por enquanto, vamos só exibir o vídeo)

                Console.WriteLine("[INFO] Iniciando loop de processamento... Pressione 'q' para sair.");

                while (true)
                {
                    // 7. Puxar um frame dos "olhos"
                    using (Mat frame = videoSource.GetNextFrame())
                    {
                        if (frame.Empty())
                        {
                            Console.WriteLine("Fonte de vídeo terminou.");
                            break;
                        }

                        // ETAPA DE TESTE: Mostrar o vídeo na tela
                        Cv2.ImShow("ANPR.Core - Teste de Vídeo", frame);
                    }

                    // 8. Checar se o usuário apertou 'q'
                    if (Cv2.WaitKey(1) == 'q')
                    {
                        break;
                    }
                }
            } // <-- O 'Dispose()' do AnprProcessor é chamado automaticamente aqui

            Console.WriteLine(">>> SISTEMA ANPR ENCERRADO <<<");
        }
    }
}