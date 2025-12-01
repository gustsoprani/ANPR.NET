using System;
using OpenCvSharp;

namespace ANPR.Shared.Models
{
    /// <summary>
    /// Resultado da detecção de placa pelo YOLO
    /// </summary>
    public class PlateDetection
    {
        public Rect BoundingBox { get; set; }
        public float Confidence { get; set; }
        public DateTime Timestamp { get; set; }
        public int FrameId { get; set; }

        public override string ToString()
        {
            return $"PlateDetection(Box:[{BoundingBox.X},{BoundingBox.Y},{BoundingBox.Width}x{BoundingBox.Height}], Conf:{Confidence:P1})";
        }
    }

    /// <summary>
    /// Resultado do processamento OCR
    /// </summary>
    public class OcrResult
    {
        public string RawText { get; set; }
        public string ProcessedText { get; set; }
        public float Confidence { get; set; }
        public bool IsValid { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public byte[] DebugImage { get; set; }

        public override string ToString()
        {
            return $"OCR: '{ProcessedText}' (Raw: '{RawText}', Conf: {Confidence:P1})";
        }
    }

    /// <summary>
    /// Resultado final do controle de acesso
    /// </summary>
    public class AccessControlResult
    {
        public bool IsAuthorized { get; set; }
        public string PlateText { get; set; }
        public string VehicleInfo { get; set; }
        public DateTime AccessTime { get; set; }
        public string Reason { get; set; }
        public int MatchConfidence { get; set; } // 0-100 (100 = match perfeito)

        public override string ToString()
        {
            string status = IsAuthorized ? "✅ AUTORIZADO" : "❌ NEGADO";
            return $"{status} - Placa: {PlateText}, Motivo: {Reason}";
        }
    }

    /// <summary>
    /// Veículo cadastrado no banco de dados
    /// </summary>
    public class DatabaseVehicle
    {
        public int Id { get; set; }
        public string PlateNumber { get; set; }
        public string OwnerName { get; set; }
        public string VehicleModel { get; set; }
        public string VehicleColor { get; set; }
        public bool IsActive { get; set; }
        public DateTime RegisteredDate { get; set; }
    }

    /// <summary>
    /// Evento de detecção com informações de contexto
    /// </summary>
    public class DetectionEvent
    {
        public int EventId { get; set; }
        public PlateDetection Detection { get; set; }
        public OcrResult OcrResult { get; set; }
        public AccessControlResult AccessResult { get; set; }
        public DateTime EventTime { get; set; }
        public Mat FrameSnapshot { get; set; }
        public Mat PlateSnapshot { get; set; }
    }

    /// <summary>
    /// Registro histórico de uma tentativa de acesso
    /// </summary>
    public class AccessLog
    {
        public int Id { get; set; }
        public string PlateNumber { get; set; }
        public string VehicleInfo { get; set; } // Ex: "Carlos - Civic" ou "Desconhecido"
        public bool IsAuthorized { get; set; }
        public string Reason { get; set; }      // Ex: "Placa não cadastrada"
        public DateTime Timestamp { get; set; }
    }
}