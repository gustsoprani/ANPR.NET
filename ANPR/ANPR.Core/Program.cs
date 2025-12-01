using System;
using System.Threading.Tasks;
using ANPR.Core.Services;
using ANPR.Core.Data; // Namespace do DbContext
using ANPR.Shared.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // Necessário para criar o ScopeFactory

namespace ANPR.Core
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine(">>> SISTEMA ANPR INICIADO (CONSOLE) <<<");

            string modeloYolo = "models/best.onnx";
            string tessData = "tessdata";
            string videoTeste = "video_teste.mp4";

            try
            {
                Console.WriteLine("[Init] Configurando Banco de Dados para Console...");

                // 1. CRIAR O MOTOR DE INJEÇÃO DE DEPENDÊNCIA (Manual)
                // O Console App não tem isso nativo, então criamos na mão.
                var serviceCollection = new ServiceCollection();

                // Configura o SQLite (arquivo diferente do Dashboard para não dar conflito)
                serviceCollection.AddDbContext<AnprDbContext>(options =>
                    options.UseSqlite("Data Source=anpr_console.db"));

                var serviceProvider = serviceCollection.BuildServiceProvider();

                // Pega a fábrica de escopos que o AccessDatabaseService precisa
                var scopeFactory = serviceProvider.GetRequiredService<IServiceScopeFactory>();

                Console.WriteLine("[Init] Inicializando Serviços de IA...");

                IVideoSource videoSource = new LiveCameraSource(0);
                if (!videoSource.IsAvailable) videoSource = new VideoFileSource(videoTeste);

                IPlateDetector yoloService = new YoloDetectionService(modelPath: modeloYolo, confidenceThreshold: 0.4f);
                IOcrEngine ocrService = new TesseractOcrService(tessDataPath: tessData);

                // 2. PASSAR O 'scopeFactory' NO CONSTRUTOR
                // Agora o serviço recebe a ferramenta para conectar no banco
                IAccessDatabase dbService = new AccessDatabaseService(scopeFactory);

                using (var processor = new AnprProcessor(
                    videoSource,
                    yoloService,
                    ocrService,
                    dbService
                ))
                {
                    // Como o AnprProcessor agora é "mudo" (para Web), 
                    // precisamos assinar o evento para ver algo no console!
                    processor.OnAnprProcessed += (result) =>
                    {
                        // Imprime no console apenas quando houver novidade
                        if (!string.IsNullOrEmpty(result.PlateText) && result.PlateText != "---")
                        {
                            Console.WriteLine($"[EVENTO] Placa: {result.PlateText} | {result.Message}");
                        }
                    };

                    await processor.StartAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}