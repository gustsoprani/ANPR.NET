using OpenCvSharp;

namespace ANPR.Shared.Models
{
    public class AnprWebResult
    {
        // A imagem da câmera para exibir na tela (em bytes para virar Base64)
        public byte[] FrameImage { get; set; }

        // A imagem processada (P&B) para debug
        public byte[] DebugImage { get; set; }

        // Dados da leitura
        public string PlateText { get; set; }
        public bool IsAuthorized { get; set; }
        public string VehicleInfo { get; set; } // Ex: "Honda Civic - João"
        public string Message { get; set; }     // Ex: "Acesso Liberado" ou "Não Autorizado"
        public DateTime Timestamp { get; set; }
    }
}