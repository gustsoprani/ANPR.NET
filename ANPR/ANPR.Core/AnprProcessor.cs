// No ANPR.Core/AnprProcessor.cs

// 1. Importar as bibliotecas que instalamos
using Microsoft.ML.OnnxRuntime;
using Tesseract;
using System;
using System.IO;
using ANPR.Shared; // <-- Adicionado para IVideoSource
using OpenCvSharp;  // <-- Adicionado para Mat (que virá em breve)

namespace ANPR.Core
{
    // 2. A classe agora herda de IDisposable
    public class AnprProcessor : IDisposable
    {
        // 3. Criar variáveis "readonly" para guardar nossos motores
        private readonly InferenceSession _yoloSession;
        private readonly TesseractEngine _tesseractEngine;

        // 4. Adicionar a variável para guardar nossos "olhos" (POO)
        private readonly IVideoSource _videoSource;

        // 5. Definir os caminhos (relativos ao local do .exe)
        private const string YoloModelPath = "models/best.onnx";
        private const string TessDataPath = "tessdata";
        private const string TessLang = "por"; // "por" de por.traineddata

        // 6. O Construtor agora "pede" a IVideoSource
        public AnprProcessor(IVideoSource videoSource) // <-- MUDANÇA AQUI
        {
            Console.WriteLine("[INFO] Carregando motores de IA...");
            _videoSource = videoSource; // <-- Salva a referência dos "olhos"

            // 7. Carregar o Modelo YOLO (ONNX) - (código antigo, sem mudanças)
            try
            {
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                _yoloSession = new InferenceSession(YoloModelPath, options);
                Console.WriteLine("[INFO] Motor YOLO (ONNX) carregado com sucesso.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] Falha ao carregar o modelo ONNX em '{YoloModelPath}'.");
                Console.WriteLine($"Verifique se o arquivo existe e se 'Copiar se for mais novo' está ativado.");
                Console.WriteLine($"Erro: {ex.Message}");
                throw;
            }

            // 8. Carregar o Motor Tesseract (OCR) - (código antigo, sem mudanças)
            try
            {
                if (!Directory.Exists(TessDataPath))
                    throw new DirectoryNotFoundException($"Pasta Tesseract não encontrada em '{TessDataPath}'");

                _tesseractEngine = new TesseractEngine(TessDataPath, TessLang, EngineMode.Default);
                Console.WriteLine("[INFO] Motor Tesseract (OCR) carregado com sucesso.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO FATAL] Falha ao carregar o Tesseract. Verifique a pasta '{TessDataPath}'.");
                Console.WriteLine($"O arquivo '{TessLang}.traineddata' está lá?");
                Console.WriteLine($"Erro: {ex.Message}");
                throw;
            }

            Console.WriteLine("[INFO] Motores carregados. Processador pronto.");
        }

        // 9. NOVO MÉTODO: Dispose (para corrigir os "LEAKs"!)
        // Este método é chamado automaticamente pelo 'using' no Program.cs
        public void Dispose()
        {
            Console.WriteLine("[INFO] Desligando motores de IA...");
            _tesseractEngine?.Dispose();
            _yoloSession?.Dispose();
        }
    }
}