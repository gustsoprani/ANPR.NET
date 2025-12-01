using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;

namespace ANPR.Core.Services
{
    /// <summary>
    /// Processador principal do ANPR
    /// Implementa a arquitetura "Gatekeeper" com filtro temporal
    /// </summary>
    public class AnprProcessor : IDisposable
    {
        private readonly IVideoSource _videoSource;
        private readonly IPlateDetector _yoloDetector;
        private readonly IOcrEngine _ocrEngine;
        private readonly IAccessDatabase _database;

        // Configurações de otimização
        private readonly int _framesPerDetection;      // YOLO a cada N frames
        private readonly int _framesToConfirm;         // Quantos frames confirmar detecção
        private readonly int _maxMemoryMb;             // Limite de memória

        // Estado de rastreamento
        private Dictionary<int, DetectionTracker> _activeDetections;
        private Dictionary<string, DateTime> _recentAccesses = new Dictionary<string, DateTime>();
        private int _frameCounter;
        private long _lastMemoryCheck;

        public AnprProcessor(
            IVideoSource videoSource,
            IPlateDetector yoloDetector,
            IOcrEngine ocrEngine,
            IAccessDatabase database,
            int framesPerDetection = 5,
            int framesToConfirm = 3,
            int maxMemoryMb = 800)
        {
            _videoSource = videoSource ?? throw new ArgumentNullException(nameof(videoSource));
            _yoloDetector = yoloDetector ?? throw new ArgumentNullException(nameof(yoloDetector));
            _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
            _database = database ?? throw new ArgumentNullException(nameof(database));

            _framesPerDetection = framesPerDetection;
            _framesToConfirm = framesToConfirm;
            _maxMemoryMb = maxMemoryMb;

            _activeDetections = new Dictionary<int, DetectionTracker>();
            _frameCounter = 0;
            _lastMemoryCheck = 0;

            Console.WriteLine($"⚙️ AnprProcessor iniciado com configuração:");
            Console.WriteLine($"   YOLO a cada: {framesPerDetection} frames");
            Console.WriteLine($"   Confirmação após: {framesToConfirm} frames");
            Console.WriteLine($"   Limite memória: {maxMemoryMb}MB");
        }

        /// <summary>
        /// Inicia o processamento de vídeo
        /// Este é o loop principal
        /// </summary>
        public void Start()
        {
            Console.WriteLine("🚀 Iniciando processamento ANPR...");
            var window = new Window("ANPR - Controle de Acesso");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                while (_videoSource.IsAvailable)
                {
                    Mat frame = _videoSource.GetNextFrame();
                    if (frame.Empty()) break;

                    // =========================================================
                    // OTIMIZAÇÃO YOLO: Isso já faz ele rodar a cada 5 frames!
                    // Se _framesPerDetection for 5, ele entra aqui no frame 0, 5, 10...
                    // =========================================================
                    List<PlateDetection> detections = new List<PlateDetection>();
                    if (_frameCounter % _framesPerDetection == 0)
                    {
                        detections = _yoloDetector.Detect(frame);
                        // Console.WriteLine($"[Frame {_frameCounter}] YOLO executado"); // Descomente para testar
                    }

                    // Atualiza a lógica de rastreamento (mantém os trackers vivos nos frames vazios)
                    ProcessDetections(detections);

                    // Seleciona apenas placas confirmadas (vistas N vezes seguidas)
                    var confirmedDetections = _activeDetections
                        .Where(x => x.Value.ConfirmationCount >= _framesToConfirm)
                        .ToList();

                    foreach (var confirmed in confirmedDetections)
                    {
                        // Se já processamos este tracker específico, ignora
                        if (confirmed.Value.AlreadyProcessed) continue;

                        var rect = confirmed.Value.LastDetection.BoundingBox;

                        // Expansão de segurança (15%)
                        int expandX = (int)(rect.Width * 0.05);
                        int expandY = (int)(rect.Height * 0.05);
                        var expanded = new Rect(
                            Math.Max(rect.X - expandX, 0),
                            Math.Max(rect.Y - expandY, 0),
                            Math.Min(rect.Width + 2 * expandX, frame.Width - rect.X + expandX),
                            Math.Min(rect.Height + 2 * expandY, frame.Height - rect.Y + expandY)
                        );

                        using (var plateRoi = new Mat(frame, expanded))
                        {
                            // Marca o tracker como processado para não ler o mesmo tracker no próximo frame
                            confirmed.Value.AlreadyProcessed = true;

                            var ocrResult = _ocrEngine.ReadPlate(plateRoi);

                            if (ocrResult.IsValid)
                            {
                                // =====================================================
                                // [NOVO] LÓGICA DE COOLDOWN (ANTI-SPAM)
                                // =====================================================
                                if (_recentAccesses.ContainsKey(ocrResult.ProcessedText))
                                {
                                    var lastTime = _recentAccesses[ocrResult.ProcessedText];
                                    if ((DateTime.Now - lastTime).TotalSeconds < 15) // 15 Segundos de espera
                                    {
                                        Console.WriteLine($"⏳ Cooldown: Placa {ocrResult.ProcessedText} ignorada (Aguarde 15s).");

                                        // Removemos o tracker para liberar a visão, mas não abrimos o portão
                                        _activeDetections.Remove(confirmed.Key);
                                        continue;
                                    }
                                }

                                // Atualiza o horário do último acesso desta placa
                                _recentAccesses[ocrResult.ProcessedText] = DateTime.Now;
                                // =====================================================

                                // Busca no banco e libera acesso
                                var vehicle = _database.FindVehicle(ocrResult.ProcessedText);

                                var accessResult = new AccessControlResult
                                {
                                    PlateText = ocrResult.ProcessedText,
                                    AccessTime = DateTime.Now,
                                    IsAuthorized = vehicle != null,
                                    VehicleInfo = vehicle != null ? $"{vehicle.OwnerName} - {vehicle.VehicleModel}" : "Desconhecido",
                                    Reason = vehicle != null ? "Placa cadastrada" : "Placa não autorizada",
                                    MatchConfidence = vehicle != null ? 100 : 0
                                };

                                Console.WriteLine(accessResult.ToString());
                                _database.LogAccess(accessResult);
                            }
                            else
                            {
                                // Se o OCR falhou, talvez queiramos tentar de novo no próximo frame.
                                // Nesse caso, NÃO removemos o tracker e NÃO marcamos como AlreadyProcessed?
                                // Depende da estratégia. Aqui vou apenas logar.
                                Console.WriteLine($"⚠️ OCR Inválido. Lido: '{ocrResult.RawText}' -> Processado: '{ocrResult.ProcessedText}'");
                            }
                        }

                        // Sempre remove o tracker após uma tentativa de leitura (com sucesso ou cooldown)
                        // Isso força o sistema a detectar "do zero" se o carro se mover.
                        _activeDetections.Remove(confirmed.Key);
                    }

                    RenderFrame(frame, detections);
                    window.ShowImage(frame);

                    // Limpeza de memória periódica (A cada 30 frames)
                    if (_frameCounter % 30 == 0)
                    {
                        CheckMemory(); // A limpeza do dicionário vai aqui dentro!
                    }

                    if (Cv2.WaitKey(1) == 27) break;
                    _frameCounter++;
                    frame.Dispose();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro: {ex.Message}");
            }
            finally
            {
                window?.Dispose();
            }
        }

        /// <summary>
        /// Processa detecções e atualiza rastreadores temporais
        /// </summary>
        private void ProcessDetections(List<PlateDetection> detections)
        {
            // Incrementar counter de todos os rastreadores ativos
            foreach (var tracker in _activeDetections.Values)
            {
                tracker.FramesSinceLastDetection++;
            }

            // Remover rastreadores que não viram mais placas (timeout)
            var toRemove = _activeDetections
                .Where(x => x.Value.FramesSinceLastDetection > 10)
                .Select(x => x.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _activeDetections.Remove(key);
                Console.WriteLine($"⏱️ Rastreador {key} expirado (sem detecção por 10 frames)");
            }

            // Para cada detecção atual, tentar associar com rastreador existente
            foreach (var det in detections)
            {
                bool found = false;

                foreach (var tracker in _activeDetections.Values)
                {
                    if (IsNearby(det.BoundingBox, tracker.LastDetection.BoundingBox))
                    {
                        // Incrementar counter de confirmação
                        tracker.ConfirmationCount++;
                        Console.WriteLine($"🔍 Rastreador ativo: Confirmação {tracker.ConfirmationCount}/{_framesToConfirm}");
                        tracker.LastDetection = det;
                        tracker.FramesSinceLastDetection = 0;
                        found = true;
                        break;
                    }
                }

                // Se não encontrou rastreador, criar novo
                if (!found)
                {
                    int newId = _activeDetections.Keys.Count + 1;
                    _activeDetections[newId] = new DetectionTracker
                    {
                        LastDetection = det,
                        ConfirmationCount = 1,
                        FramesSinceLastDetection = 0
                    };
                    Console.WriteLine($"🆕 Novo rastreador {newId} criado");
                }
            }
        }

        /// <summary>
        /// Verifica se dois retângulos estão próximos (mesmo objeto)
        /// </summary>
        private bool IsNearby(Rect rect1, Rect rect2, int tolerance = 50)
        {
            return Math.Abs(rect1.X - rect2.X) < tolerance &&
                   Math.Abs(rect1.Y - rect2.Y) < tolerance &&
                   Math.Abs(rect1.Width - rect2.Width) < tolerance &&
                   Math.Abs(rect1.Height - rect2.Height) < tolerance;
        }

        /// <summary>
        /// Renderiza visualizações no frame
        /// </summary>
        private void RenderFrame(Mat frame, List<PlateDetection> detections)
        {
            // Desenhar detecções
            foreach (var det in detections)
            {
                Cv2.Rectangle(frame, det.BoundingBox, Scalar.Yellow, 2);
                Cv2.PutText(frame, $"Detectando {det.Confidence:P1}",
                    new Point(det.BoundingBox.X, det.BoundingBox.Y - 5),
                    HersheyFonts.HersheySimplex, 0.5, Scalar.Yellow, 1);
            }

            // Desenhar rastreadores confirmados
            foreach (var tracker in _activeDetections.Where(x => x.Value.ConfirmationCount >= _framesToConfirm))
            {
                var det = tracker.Value.LastDetection;
                Cv2.Rectangle(frame, det.BoundingBox, Scalar.Green, 3);
                Cv2.PutText(frame, $"Confirmado {tracker.Value.ConfirmationCount}/{_framesToConfirm}",
                    new Point(det.BoundingBox.X, det.BoundingBox.Y - 5),
                    HersheyFonts.HersheySimplex, 0.6, Scalar.Green, 2);
            }

            // Info no topo
            Cv2.PutText(frame, $"Frame: {_frameCounter} | Rastreadores: {_activeDetections.Count}",
                new Point(10, 30), HersheyFonts.HersheySimplex, 0.7, Scalar.Cyan, 2);
        }

        /// <summary>
        /// Verifica uso de memória
        /// </summary>
        private void CheckMemory()
        {
            // 1. Limpeza do Dicionário de Cooldown [NOVO]
            // Removemos placas que não são vistas há mais de 1 minuto para liberar memória
            var chavesExpiradas = _recentAccesses
                .Where(pair => (DateTime.Now - pair.Value).TotalSeconds > 60)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var chave in chavesExpiradas)
            {
                _recentAccesses.Remove(chave);
            }

            // Se removeu algo, avisa no log (opcional, bom para debug)
            if (chavesExpiradas.Count > 0)
                Console.WriteLine($"🧹 Limpeza: {chavesExpiradas.Count} placas removidas do cache de cooldown.");

            // 2. Verificação de Memória RAM (Código original)
            long memoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
            if (memoryMb > _maxMemoryMb * 0.9)
            {
                Console.WriteLine($"⚠️ Memória cheia ({memoryMb}MB). Coletando lixo...");
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        public void Dispose()
        {
            _videoSource?.Dispose();
            _yoloDetector?.Dispose();
            _ocrEngine?.Dispose();
            _database?.Dispose();
        }

        /// <summary>
        /// Classe interna para rastrear detecções
        /// </summary>
        private class DetectionTracker
        {
            public PlateDetection LastDetection { get; set; }
            public int ConfirmationCount { get; set; }
            public int FramesSinceLastDetection { get; set; }
            public bool AlreadyProcessed { get; set; } = false;
        }
    }
}