using ANPR.Dashboard.Components;
using ANPR.Core.Services;
using ANPR.Core;
using ANPR.Shared.Interfaces;
using ANPR.Shared.Models; // Caso precise de modelos
using System.Diagnostics; // Para debug

namespace ANPR.Dashboard
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // =============================================================
            // 1. CONFIGURAÇÃO DOS SERVIÇOS DO ANPR (INJEÇÃO DE DEPENDÊNCIA)
            // =============================================================

            // Definir caminhos absolutos para garantir que o IIS/Kestrel ache os arquivos
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelPath = Path.Combine(baseDir, "models", "best.onnx");
            string tessData = Path.Combine(baseDir, "tessdata");

            Console.WriteLine($"[SETUP] Base Directory: {baseDir}");
            Console.WriteLine($"[SETUP] Model Path: {modelPath}");

            // Verificar se os arquivos existem para evitar erro silencioso
            if (!File.Exists(modelPath))
            {
                Console.WriteLine("[ERRO FATAL] O arquivo de modelo YOLO não foi encontrado na pasta 'models'.");
                Console.WriteLine("Certifique-se de copiar a pasta 'models' do Core para o Dashboard ou configurar o 'Copy to Output Directory'.");
            }

            // A. Registrar a Câmera (Singleton = Uma câmera para toda a vida do app)
            // Use índice 0 para webcam padrão.
            builder.Services.AddSingleton<IVideoSource>(sp => new LiveCameraSource(0));

            // B. Registrar o Detector YOLO
            builder.Services.AddSingleton<IPlateDetector>(sp => new YoloDetectionService(modelPath, confidenceThreshold: 0.4f));

            // C. Registrar o OCR Tesseract
            builder.Services.AddSingleton<IOcrEngine>(sp => new TesseractOcrService(tessData));

            // D. Registrar o Banco de Dados
            builder.Services.AddSingleton<IAccessDatabase, AccessDatabaseService>();

            // E. Registrar o Processador Principal (O "Motor")
            builder.Services.AddSingleton<AnprProcessor>();

            // =============================================================
            // FIM DA CONFIGURAÇÃO DO ANPR
            // =============================================================

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseAntiforgery();

            // =============================================================
            // 2. INICIAR O MOTOR ANPR EM BACKGROUND
            // =============================================================

            try
            {
                // Pegamos a instância única do processador que criamos acima
                var processor = app.Services.GetService<AnprProcessor>();

                if (processor != null)
                {
                    Console.WriteLine("🚀 Iniciando Motor ANPR em Background...");

                    // IMPORTANTE: Usamos Task.Run para não travar a inicialização do site.
                    // Se você renomeou o método para 'StartAsync', use:
                    // _ = processor.StartAsync();

                    // Se o método ainda se chama 'Start' (síncrono), use:
                    _ = Task.Run(() => processor.StartAsync());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] Falha ao iniciar ANPR: {ex.Message}");
            }

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}