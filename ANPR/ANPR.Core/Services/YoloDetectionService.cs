using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;

namespace ANPR.Core.Services
{
    /// <summary>
    /// Serviço de detecção de placas usando YOLOv8 Nano
    /// Implementa a "porta" para OCR com filtro temporal
    /// </summary>
    public class YoloDetectionService : IPlateDetector
    {
        private readonly InferenceSession _yoloSession;
        private float _confidenceThreshold;
        private readonly int _netWidth = 640;
        private readonly int _netHeight = 640;

        public float ConfidenceThreshold
        {
            get => _confidenceThreshold;
            set => _confidenceThreshold = value;
        }

        public YoloDetectionService(string modelPath, float confidenceThreshold = 0.25f)
        {
            _confidenceThreshold = confidenceThreshold;

            try
            {
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                _yoloSession = new InferenceSession(modelPath, sessionOptions);

                Console.WriteLine($"✅ YOLOv8 Model carregado: {modelPath}");
                Console.WriteLine($"📊 Entradas: {string.Join(", ", _yoloSession.InputMetadata.Keys)}");
                Console.WriteLine($"📊 Saídas: {string.Join(", ", _yoloSession.OutputMetadata.Keys)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao carregar modelo YOLO: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Detecta placas em um frame
        /// Esta é a operação "barata" que roda a cada frame
        /// </summary>
        public List<PlateDetection> Detect(Mat frame)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Pré-processamento: converter para tensor 640x640
                var (blob, scale, padX, padY) = PreprocessLetterbox(frame);

                // Executar YOLO
                string inputName = _yoloSession.InputMetadata.Keys.First();
                var inputTensor = new DenseTensor<float>(blob, new[] { 1, 3, _netHeight, _netWidth });
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
                };

                using (var results = _yoloSession.Run(inputs))
                {
                    var output = results.First();
                    var dims = output.AsTensor<float>().Dimensions.ToArray();
                    var data = output.AsEnumerable<float>().ToArray();

                    // Parse detections
                    var dets = ParseDetections(data, dims);

                    // Reescalar coordenadas para frame original
                    var rescaledDets = new List<PlateDetection>();
                    foreach (var det in dets)
                    {
                        var rect = RescaleRect(det.BoundingBox, frame.Width, frame.Height, scale, padX, padY);
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            rescaledDets.Add(new PlateDetection
                            {
                                BoundingBox = rect,
                                Confidence = det.Confidence,
                                Timestamp = DateTime.Now
                            });
                        }
                    }

                    stopwatch.Stop();
                    return rescaledDets;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro na detecção YOLO: {ex.Message}");
                return new List<PlateDetection>();
            }
        }

        private (float[], float, int, int) PreprocessLetterbox(Mat frame)
        {
            int w = frame.Width, h = frame.Height;
            float r = Math.Min(_netWidth / (float)w, _netHeight / (float)h);
            int newW = (int)(w * r), newH = (int)(h * r);
            int padX = (_netWidth - newW) / 2, padY = (_netHeight - newH) / 2;

            Mat resized = new Mat();
            Cv2.Resize(frame, resized, new Size(newW, newH));

            Mat canvas = new Mat(new Size(_netWidth, _netHeight), MatType.CV_8UC3, new Scalar(114, 114, 114));
            resized.CopyTo(new Mat(canvas, new Rect(padX, padY, newW, newH)));

            Cv2.CvtColor(canvas, canvas, ColorConversionCodes.BGR2RGB);
            canvas.ConvertTo(canvas, MatType.CV_32FC3, 1.0 / 255.0);

            float[] blob = new float[3 * _netHeight * _netWidth];
            int idx = 0;
            for (int c = 0; c < 3; c++)
                for (int y = 0; y < _netHeight; y++)
                    for (int x = 0; x < _netWidth; x++)
                    {
                        var v = canvas.At<Vec3f>(y, x);
                        blob[idx++] = c == 0 ? v.Item0 : c == 1 ? v.Item1 : v.Item2;
                    }

            resized.Dispose();
            canvas.Dispose();

            return (blob, r, padX, padY);
        }

        private List<PlateDetection> ParseDetections(float[] data, int[] dims)
        {
            var dets = new List<PlateDetection>();

            if (dims.Length == 3 && dims[0] == 1 && dims[2] == 6)
            {
                int N = dims[1];
                for (int i = 0; i < N; i++)
                {
                    int baseIdx = i * 6;
                    float x1 = data[baseIdx + 0];
                    float y1 = data[baseIdx + 1];
                    float x2 = data[baseIdx + 2];
                    float y2 = data[baseIdx + 3];
                    float conf = data[baseIdx + 4];

                    if (conf < _confidenceThreshold) continue;

                    var rect = new Rect(
                        (int)Math.Round(x1),
                        (int)Math.Round(y1),
                        (int)Math.Round(x2 - x1),
                        (int)Math.Round(y2 - y1));

                    dets.Add(new PlateDetection
                    {
                        BoundingBox = rect,
                        Confidence = conf
                    });
                }
            }

            return dets;
        }

        private Rect RescaleRect(Rect r, int origW, int origH, float scale, int padX, int padY)
        {
            int x = (int)((r.X - padX) / scale);
            int y = (int)((r.Y - padY) / scale);
            int w = (int)(r.Width / scale);
            int h = (int)(r.Height / scale);

            x = Math.Max(0, Math.Min(x, origW - 1));
            y = Math.Max(0, Math.Min(y, origH - 1));
            w = Math.Max(0, Math.Min(w, origW - x));
            h = Math.Max(0, Math.Min(h, origH - y));

            return new Rect(x, y, w, h);
        }

        public void Dispose()
        {
            _yoloSession?.Dispose();
        }
    }
}