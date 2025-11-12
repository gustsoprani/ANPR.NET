// No ANPR.Core/AnprProcessor.cs

// 1. Importar as bibliotecas que instalamos
using Microsoft.ML.OnnxRuntime;
using Tesseract;
using System;
using System.IO;

namespace ANPR.Core
{
    public class AnprProcessor
    {
        // 2. Criar variáveis "readonly" para guardar nossos motores
        private readonly InferenceSession _yoloSession;
        private readonly TesseractEngine _tesseractEngine;

        // 3. Definir os caminhos (relativos ao local do .exe)
        private const string YoloModelPath = "models/best.onnx";
        private const string TessDataPath = "tessdata";
        private const string TessLang = "por"; // "por" de por.traineddata

        // 4. Criar o Construtor (o código que roda quando a classe é criada)
        public AnprProcessor()
        {
            Console.WriteLine("[INFO] Carregando motores de IA...");

            // 5. Carregar o Modelo YOLO (ONNX)
            try
            {
                var options = new SessionOptions();
                // (Opcional, mas bom) Otimiza para a CPU
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

            // 6. Carregar o Motor Tesseract (OCR)
            try
            {
                // Verifica se a pasta tessdata existe
                if (!Directory.Exists(TessDataPath))
                    throw new DirectoryNotFoundException($"Pasta Tesseract não encontrada em '{TessDataPath}'");

                // O TesseractEngine espera a PASTA, não o arquivo
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
    }
}