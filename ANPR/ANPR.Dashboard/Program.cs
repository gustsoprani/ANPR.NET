using ANPR.Dashboard.Components;
using ANPR.Core.Services;
using ANPR.Core;
using ANPR.Shared.Interfaces;
using Microsoft.AspNetCore.SignalR; // <--- ADICIONE ESTE USING
using ANPR.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace ANPR.Dashboard
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. Configuração dos Serviços (Câmera, YOLO, etc...)
            // (Mantenha o seu código de injeção de dependência aqui...)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelPath = Path.Combine(baseDir, "models", "best.onnx");
            string tessData = Path.Combine(baseDir, "tessdata");
            builder.Services.AddDbContext<AnprDbContext>(options =>
                options.UseSqlite("Data Source=anpr.db"));

            //builder.Services.AddSingleton<IVideoSource>(sp => new LiveCameraSource(0)); // Webcam
            builder.Services.AddSingleton<IVideoSource>(sp => new VideoFileSource(@"C:\Users\Guto\source\repos\ANPR.NET\ANPR\ANPR.Dashboard\video.mp4")); // Video
            builder.Services.AddSingleton<IPlateDetector>(sp => new YoloDetectionService(modelPath, 0.4f));
            builder.Services.AddSingleton<IOcrEngine>(sp => new TesseractOcrService(tessData));
            builder.Services.AddSingleton<IAccessDatabase, AccessDatabaseService>();
            builder.Services.AddSingleton<AnprProcessor>();

            // 2. Configuração do Blazor
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // =================================================================
            // [CORREÇÃO CRÍTICA] AUMENTAR O LIMITE DE TAMANHO DO SIGNALR
            // Sem isso, a imagem da câmera mata a conexão e trava os botões.
            // =================================================================
            builder.Services.AddSignalR(hubOptions =>
            {
                hubOptions.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB
                hubOptions.EnableDetailedErrors = true;
            });
            // =================================================================

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

            // 3. Iniciar o Motor
            try
            {
                var processor = app.Services.GetService<AnprProcessor>();
                // Inicia sem travar o site
                _ = processor.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao iniciar ANPR: {ex.Message}");
            }

            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode(); // <--- Garanta que isso está aqui

            app.Run();
        }
    }
}