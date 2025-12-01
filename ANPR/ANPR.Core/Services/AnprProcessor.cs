using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;

namespace ANPR.Core.Services
{
    public class AnprProcessor : IDisposable
    {
        // O SITE ESCUTA ESTE EVENTO
        public event Action<AnprWebResult> OnAnprProcessed;

        private readonly IVideoSource _videoSource;
        private readonly IPlateDetector _yoloDetector;
        private readonly IOcrEngine _ocrEngine;
        private readonly IAccessDatabase _database;

        private readonly int _framesPerDetection;
        private readonly int _framesToConfirm;
        private readonly int _maxMemoryMb;

        private Dictionary<int, DetectionTracker> _activeDetections;
        private Dictionary<string, DateTime> _recentAccesses = new Dictionary<string, DateTime>();
        private int _frameCounter;

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
        }

        // --- MÉTODO PRINCIPAL (LIVRE DE JANELAS) ---
        public async Task StartAsync()
        {
            Console.WriteLine("🚀 Processador ANPR Web Iniciado (Background).");

            try
            {
                while (_videoSource.IsAvailable)
                {
                    // 1. Captura Frame
                    using Mat frame = _videoSource.GetNextFrame();
                    if (frame.Empty())
                    {
                        await Task.Delay(100);
                        continue;
                    }

                    // 2. Prepara objeto para o Site
                    var webResult = new AnprWebResult
                    {
                        Timestamp = DateTime.Now,
                        PlateText = "---",
                        Message = "Monitorando...",
                        IsAuthorized = false,
                        VehicleInfo = ""
                    };

                    // 3. Detecção YOLO (A cada N frames)
                    List<PlateDetection> detections = new List<PlateDetection>();
                    if (_frameCounter % _framesPerDetection == 0)
                    {
                        detections = _yoloDetector.Detect(frame);
                    }

                    // 4. Rastreamento
                    ProcessDetections(detections);

                    // 5. Verifica Rastreadores Confirmados
                    var confirmedDetections = _activeDetections
                        .Where(x => x.Value.ConfirmationCount >= _framesToConfirm)
                        .ToList();

                    foreach (var confirmed in confirmedDetections)
                    {
                        if (confirmed.Value.AlreadyProcessed) continue;

                        // Recorte da Placa
                        var rect = confirmed.Value.LastDetection.BoundingBox;
                        int expandX = (int)(rect.Width * 0.15);
                        int expandY = (int)(rect.Height * 0.15);

                        var expanded = new Rect(
                            Math.Max(rect.X - expandX, 0),
                            Math.Max(rect.Y - expandY, 0),
                            Math.Min(rect.Width + 2 * expandX, frame.Width - rect.X + expandX),
                            Math.Min(rect.Height + 2 * expandY, frame.Height - rect.Y + expandY)
                        );

                        if (expanded.Width > 0 && expanded.Height > 0)
                        {
                            using (var plateRoi = new Mat(frame, expanded))
                            {
                                confirmed.Value.AlreadyProcessed = true;
                                var ocrResult = _ocrEngine.ReadPlate(plateRoi);

                                if (ocrResult.IsValid)
                                {
                                    // Verifica Banco e Cooldown
                                    var vehicle = _database.FindVehicle(ocrResult.ProcessedText);
                                    string cooldownKey = vehicle != null ? vehicle.PlateNumber : ocrResult.ProcessedText;

                                    if (_recentAccesses.ContainsKey(cooldownKey))
                                    {
                                        var lastTime = _recentAccesses[cooldownKey];
                                        if ((DateTime.Now - lastTime).TotalSeconds < 15)
                                        {
                                            _activeDetections.Remove(confirmed.Key);
                                            continue; // Pula se estiver em cooldown
                                        }
                                    }

                                    // Atualiza Cooldown e Log
                                    _recentAccesses[cooldownKey] = DateTime.Now;

                                    var accessResult = new AccessControlResult
                                    {
                                        PlateText = ocrResult.ProcessedText,
                                        AccessTime = DateTime.Now,
                                        IsAuthorized = vehicle != null,
                                        VehicleInfo = vehicle != null ? $"{vehicle.OwnerName} - {vehicle.VehicleModel}" : "Desconhecido",
                                        Reason = vehicle != null ? "Placa cadastrada" : "Placa não autorizada",
                                        MatchConfidence = vehicle != null ? 100 : 0
                                    };

                                    _database.LogAccess(accessResult);

                                    // === ATUALIZA DADOS PARA O SITE ===
                                    webResult.PlateText = accessResult.PlateText;
                                    webResult.VehicleInfo = accessResult.VehicleInfo;
                                    webResult.Message = accessResult.Reason;
                                    webResult.IsAuthorized = accessResult.IsAuthorized;

                                    // Envia a imagem recortada da placa também (opcional)
                                    webResult.DebugImage = ocrResult.DebugImage;
                                }
                            }
                        }
                        _activeDetections.Remove(confirmed.Key);
                    }

                    // 6. Desenha retângulos na imagem principal
                    RenderFrame(frame, detections);

                    // 7. ENVIA PARA O SITE (Converter para JPG)
                    webResult.FrameImage = frame.ToBytes(".jpg");

                    // Dispara o evento!
                    OnAnprProcessed?.Invoke(webResult);

                    // 8. Gestão de Memória e Loop
                    if (_frameCounter % 30 == 0) CheckMemory();
                    _frameCounter++;

                    // Pausa assíncrona para não travar a CPU (aprox 30 FPS)
                    await Task.Delay(33);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro Fatal no Processador: {ex.Message}");
            }
        }

        // --- MÉTODOS AUXILIARES (Mantidos para funcionar a lógica) ---

        private void ProcessDetections(List<PlateDetection> detections)
        {
            foreach (var tracker in _activeDetections.Values) tracker.FramesSinceLastDetection++;

            var toRemove = _activeDetections.Where(x => x.Value.FramesSinceLastDetection > 10).Select(x => x.Key).ToList();
            foreach (var key in toRemove) _activeDetections.Remove(key);

            foreach (var det in detections)
            {
                bool found = false;
                foreach (var tracker in _activeDetections.Values)
                {
                    if (IsNearby(det.BoundingBox, tracker.LastDetection.BoundingBox))
                    {
                        tracker.ConfirmationCount++;
                        tracker.LastDetection = det;
                        tracker.FramesSinceLastDetection = 0;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    int newId = _activeDetections.Keys.Count + 1;
                    _activeDetections[newId] = new DetectionTracker { LastDetection = det, ConfirmationCount = 1 };
                }
            }
        }

        private bool IsNearby(Rect rect1, Rect rect2, int tolerance = 50)
        {
            return Math.Abs(rect1.X - rect2.X) < tolerance && Math.Abs(rect1.Y - rect2.Y) < tolerance;
        }

        private void RenderFrame(Mat frame, List<PlateDetection> detections)
        {
            foreach (var det in detections)
            {
                Cv2.Rectangle(frame, det.BoundingBox, Scalar.Yellow, 2);
            }
        }

        private void CheckMemory()
        {
            var chavesExpiradas = _recentAccesses.Where(p => (DateTime.Now - p.Value).TotalSeconds > 60).Select(p => p.Key).ToList();
            foreach (var k in chavesExpiradas) _recentAccesses.Remove(k);

            long memoryMb = GC.GetTotalMemory(false) / (1024 * 1024);
            if (memoryMb > _maxMemoryMb * 0.9)
            {
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

        private class DetectionTracker
        {
            public PlateDetection LastDetection { get; set; }
            public int ConfirmationCount { get; set; }
            public int FramesSinceLastDetection { get; set; }
            public bool AlreadyProcessed { get; set; } = false;
        }
    }
}