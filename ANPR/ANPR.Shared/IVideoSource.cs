using ANPR.Shared.Models;
using OpenCvSharp;
using System;
using System.Collections.Generic;

namespace ANPR.Shared.Interfaces
{
    /// <summary>
    /// Interface para fontes de vídeo (câmera, arquivo, etc)
    /// </summary>
    public interface IVideoSource : IDisposable
    {
        Mat GetNextFrame();
        bool IsAvailable { get; }
        int FrameCount { get; }
        int TotalFrames { get; }
        double CurrentFps { get; }
    }

    /// <summary>
    /// Interface para detector de placas (YOLO)
    /// </summary>
    public interface IPlateDetector : IDisposable
    {
        List<PlateDetection> Detect(Mat frame);
        float ConfidenceThreshold { get; set; }
    }


    /// <summary>
    /// Interface para mecanismo de OCR (Tesseract)
    /// </summary>
    public interface IOcrEngine : IDisposable
    {
        OcrResult ReadPlate(Mat plateImage);
        string PostProcess(string rawText);
    }

    /// <summary>
    /// Interface para banco de dados de controle de acesso
    /// </summary>
    public interface IAccessDatabase : IDisposable
    {
        DatabaseVehicle FindVehicle(string plateNumber);
        int GetLevenshteinDistance(string s1, string s2);
        bool LogAccess(AccessControlResult result);
        List<DatabaseVehicle> GetAllVehicles();
        void AddVehicle(DatabaseVehicle vehicle);
        bool RemoveVehicle(string plateNumber);
        List<AccessLog> GetHistory(int limit = 100);
    }

    /// <summary>
    /// Interface para logger centralizado
    /// </summary>
    public interface IAccessLogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception ex = null);
        void LogDebug(string message);
    }
}
