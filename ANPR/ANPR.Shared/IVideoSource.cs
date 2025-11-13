// No ANPR.Shared/IVideoSource.cs

using OpenCvSharp; // <-- Precisamos disso!
using System;

namespace ANPR.Shared
{
    // Este é o "contrato" de POO.
    // Qualquer classe que "assinar" este contrato (IVideoSource)
    // é obrigada a ter todos esses métodos.
    public interface IVideoSource : IDisposable
    {
        bool Open();           // Tenta abrir a câmera ou o arquivo
        bool IsOpened();       // Verifica se está funcionando
        Mat GetNextFrame();    // Pega o próximo frame da fonte
    }
}