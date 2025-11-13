// No ANPR.Core/AnprProcessor.cs

// 1. Importar as bibliotecas que instalamos
using ANPR.Shared; // <-- Adicionado para IVideoSource
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Runtime.InteropServices;
using OpenCvSharp;  // <-- Adicionado para Mat
using System;
using System.IO;
using Tesseract;
using System.Collections.Generic; // <-- Adicionado para List<>
using System.Drawing; // <-- Adicionado para Size e Scalar
using OpenCvSharp.Dnn;

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

        public void StartProcessing()
        {
            Console.WriteLine("[INFO] Iniciando loop de processamento... Pressione 'q' para sair.");
            int cont = 0;
            while (true)
            {
                using (Mat frame = _videoSource.GetNextFrame())
                {
                    if (frame.Empty())
                    {
                        Console.WriteLine("Fonte de vídeo terminou.");
                        break;
                    }
                    List<OpenCvSharp.Rect> listaPlacas = RunYoloDetection(frame);
                    if (listaPlacas.Count == 0)
                    {
                        cont = 0;
                    }
                    else
                    {
                        cont++;
                        if (cont >= 3)
                        {
                            // CORREÇÃO (CS1503):
                            // 'listaPlacas' é uma LISTA. Precisamos processar CADA placa
                            // que está DENTRO da lista. Usamos um loop 'foreach'.
                            foreach (var placaRect in listaPlacas)
                            {
                                // Agora passamos UMA placa de cada vez.
                                string textoPlaca = RunTesseractProcess(placaRect, frame);
                                Console.WriteLine($"Placa Lida: {textoPlaca}"); // Teste
                            }
                        }
                    }
                    // ETAPA DE TESTE: Mostrar o vídeo na tela
                    Cv2.ImShow("ANPR.Core - Teste de Vídeo", frame);
                }
                if (Cv2.WaitKey(1) == 'q')
                {
                    break;
                }
            }
        }
        // O tamanho que o nosso modelo YOLO espera (ex: 640x640)
        private const int _inputSize = 640;
        private DenseTensor<float> photoProcessingYollo(Mat frame)
        {
            Mat blob = CvDnn.BlobFromImage(
                frame,
                1.0 / 255.0,
                new OpenCvSharp.Size(_inputSize, _inputSize),
                new Scalar(0, 0, 0),
                swapRB: true,
                crop: false
            );

            var dimensions = new[] { 1, 3, _inputSize, _inputSize };
            var tensor = new DenseTensor<float>(dimensions);

            // 1. Cria um array temporário de floats do tamanho exato.
            float[] tempArray = new float[tensor.Length];

            // 2. Copia os dados do 'blob' (da memória do OpenCV) 
            //    para o nosso array temporário. Esta é uma operação "segura".
            Marshal.Copy(blob.Data, tempArray, 0, (int)tensor.Length);

            // 3. Copia os dados do nosso array temporário para o buffer do tensor.
            tempArray.CopyTo(tensor.Buffer.Span);

            // 4. Libera o 'blob' da memória manualmente, pois já copiamos os dados
            blob.Dispose();

            return tensor;
        }
        private List<OpenCvSharp.Rect> ParseYoloOutput(DisposableNamedOnnxValue result)
        {
            // 1. Pegar o Tensor "bruto" (a "planilha")
            var tensor = result.Value as DenseTensor<float>;

            // 2. Criar a lista final que vamos retornar
            var detections = new List<OpenCvSharp.Rect>();

            // 3. Definir o filtro de confiança (A SUA IDEIA!)
            // (Qualquer coisa abaixo de 70% de certeza será ignorada)
            const float confidenceThreshold = 0.70f;

            // 4. Pegar as dimensões da "planilha". 
            // A saída do YOLOv8 é [1, 84, 8400]
            // O que nos importa é o 8400 (o número de "caixas" candidatas)
            var numCandidates = tensor.Dimensions[2]; // 8400

            // 5. O LOOP (A SUA LÓGICA)
            // Vamos "ler" a planilha de lado.
            for (int i = 0; i < numCandidates; i++)
            {
                // 6. PEGAR A PONTUAÇÃO DE CONFIANÇA
                // Para cada "caixa", pegamos a maior pontuação de classe
                // (Isso é matemática complexa, apenas saiba que é o "score")
                float score = 0;
                int classIndex = 0;
                for (int j = 4; j < 84; j++) // As pontuações começam na coluna 4
                {
                    float currentScore = tensor[0, j, i];
                    if (currentScore > score)
                    {
                        score = currentScore;
                        classIndex = j - 4; // O índice da classe
                    }
                }

                // 7. O FILTRO (A SUA IDEIA!)
                // É aqui que checamos sua lógica de 70%
                if (score > confidenceThreshold)
                {
                    // 8. Pegar as Coordenadas (A "Caixa")
                    // O YOLO nos dá a caixa no formato [centro_x, centro_y, largura, altura]
                    var x_center = tensor[0, 0, i];
                    var y_center = tensor[0, 1, i];
                    var width = tensor[0, 2, i];
                    var height = tensor[0, 3, i];

                    // 9. Converter para o formato (x, y, w, h) que o OpenCV gosta
                    var x_left = (int)(x_center - width / 2);
                    var y_top = (int)(y_center - height / 2);
                    var w = (int)width;
                    var h = (int)height;

                    // 10. Adicionar a "caixa" boa à nossa lista final
                    detections.Add(new OpenCvSharp.Rect(x_left, y_top, w, h));
                }
            }

            // 11. Retornar a lista limpa!
            return detections;
        }
        private List<OpenCvSharp.Rect> RunYoloDetection(Mat frame)
        {
            DenseTensor<float> prepareTensor = photoProcessingYollo(frame);
            // 1. "Empacotar" nosso tensor com o nome "images"
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", prepareTensor)
            };
            // 2. O Run também precisa saber quais "saídas" queremos (o "relatório")
            // O nome da saída do YOLO é (geralmente) "output0"
            var outputNames = new List<string> { "output0" };

            // 3. Chamamos o Run
            var results = _yoloSession.Run(inputs, outputNames);

            // 4. Pegamos o "relatório bruto" (o primeiro item)
            var rawReport = results[0];

            // 5. Chamamos nosso novo ajudante e retornamos o resultado
            var endReport = ParseYoloOutput(rawReport);

            // 6. Retorna o relatório formatado
            return endReport;
        }
        private string RunTesseractProcess(OpenCvSharp.Rect placaRect, Mat frame)
        {
            return "";
        }
    }
}