using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Tesseract;
using System.IO;
using ANPR.Shared.Models;
using ANPR.Shared.Interfaces;
using System.Linq;

namespace ANPR.Core.Services
{
    /// <summary>
    /// Serviço OCR usando Tesseract otimizado para Placas Mercosul
    /// Utiliza Binarização Otsu e reconhecimento de linha única (SingleLine)
    /// </summary>
    public class TesseractOcrService : IOcrEngine
    {
        private readonly TesseractEngine _tesseractEngine;
        private readonly Regex _plateCharPattern;

        // Whitelist: Apenas letras maiúsculas e números
        private const string WHITELIST = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public TesseractOcrService(string tessDataPath)
        {
            try
            {
                // EngineMode.LstmOnly é geralmente mais robusto para o Tesseract 4/5
                _tesseractEngine = new TesseractEngine(tessDataPath, "eng", EngineMode.LstmOnly);

                // Configurações globais para restringir o reconhecimento
                _tesseractEngine.SetVariable("tessedit_char_whitelist", WHITELIST);
                _tesseractEngine.SetVariable("user_defined_dpi", "300"); // Ajuda no redimensionamento interno
                _tesseractEngine.SetVariable("tessedit_pageseg_mode", ((int)PageSegMode.SingleLine).ToString());

                // Expressão regular para validar o formato LLL#L## (7 caracteres, Mercosul)
                _plateCharPattern = new Regex(@"^[A-Z]{3}[0-9]{1}[A-Z]{1}[0-9]{2}$"); // Padrão Mercosul

                Console.WriteLine($"✅ Tesseract OCR inicializado com modelo 'eng' (LSTM)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro ao inicializar Tesseract. Verifique o caminho: {tessDataPath}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
                // Lança a exceção para impedir o início da aplicação sem OCR
                throw new InvalidOperationException("Falha ao inicializar Tesseract.", ex);
            }
        }

        public OcrResult ReadPlate(Mat plateImage)
        {
            Stopwatch sw = Stopwatch.StartNew();
            float confidence = 0.0f;
            string rawText = string.Empty;
            string processedText = string.Empty;
            bool isValid = false;

            using (Mat processed = PreprocessPlateOptimized(plateImage))
            {
                // Salvar a imagem processada para debug
                string debugPath = Path.Combine(Directory.GetCurrentDirectory(), $"debug_binary_ocr_{DateTime.Now:HHmmss_fff}.png");
                Cv2.ImWrite(debugPath, processed);

                using (var pix = PixConverter.ToPix(processed.ToBitmap()))
                {
                    using (var page = _tesseractEngine.Process(pix, PageSegMode.SingleLine))
                    {
                        rawText = page.GetText().Trim().ToUpper().Replace(" ", "");
                        confidence = page.GetMeanConfidence();
                    }
                }

                processedText = PostProcess(rawText);
                isValid = _plateCharPattern.IsMatch(processedText);
            }

            sw.Stop();

            return new OcrResult
            {
                RawText = rawText,
                ProcessedText = processedText,
                Confidence = confidence,
                IsValid = isValid,
                ProcessingTime = sw.Elapsed
            };
        }

        /// <summary>
        /// Pré-processamento OTIMIZADO para placas.
        /// Aplica Redimensionamento, Binarização Otsu e **Limpeza de Borda**.
        /// </summary>
        private Mat PreprocessPlateOptimized(Mat plateImage)
        {
            // 1. Redimensionamento para o Tesseract
            double scaleFactor = 1.0;
            if (plateImage.Height < 120) // Altura mínima recomendada para Tesseract
            {
                scaleFactor = 120.0 / plateImage.Height;
            }

            Mat resized = plateImage;
            if (scaleFactor > 1.0)
            {
                resized = new Mat();
                // Usa Lanczos4 para melhor qualidade no upscale
                Cv2.Resize(plateImage, resized,
                           new Size((int)(plateImage.Width * scaleFactor), (int)(plateImage.Height * scaleFactor)),
                           0, 0, InterpolationFlags.Lanczos4);
            }

            // 2. Grayscale e Binarização Otsu
            Mat gray = new Mat();
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

            Mat binary = new Mat();
            // Binarização Otsu: Tenta encontrar o melhor limiar global
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // 3. PASSO CRÍTICO: LIMPEZA DE BORDA
            // Adiciona uma margem branca para "apagar" o ruído preto das tarjas da placa (azul/preta)
            int borderSize = 5;
            Mat finalProcessed = new Mat();

            // Adiciona a margem branca (Scalar.All(255))
            Cv2.CopyMakeBorder(
                binary,
                finalProcessed,
                borderSize,
                borderSize,
                borderSize,
                borderSize,
                BorderTypes.Constant,
                Scalar.All(255)
            );

            // Liberação de memória
            if (resized != plateImage) resized.Dispose();
            gray.Dispose();
            binary.Dispose();

            return finalProcessed;
        }

        /// <summary>
        /// Pós-processamento para forçar o formato Mercosul LLL#L## e corrigir erros comuns
        /// </summary>
        public string PostProcess(string rawText)
        {
            // 1. Limpeza básica: Remove espaços e caracteres fora da Whitelist
            string cleaned = new string(rawText.Where(c => WHITELIST.Contains(c)).ToArray());

            // 2. Padronizar para 7 caracteres
            if (cleaned.Length > 7)
                cleaned = cleaned.Substring(0, 7);

            if (cleaned.Length < 7)
                return string.Empty;

            char[] chars = cleaned.ToCharArray();

            // 3. Correções posicionais (Mercosul: LLL#L##)
            for (int i = 0; i < chars.Length; i++)
            {
                // Posições de LETRAS: 0, 1, 2 e 4
                if (i == 0 || i == 1 || i == 2 || i == 4)
                {
                    if (char.IsDigit(chars[i]))
                    {
                        // Dígitos confundidos com Letras: 0->O, 1->I, 4->A, 8->B, 5->S
                        if (chars[i] == '0' || chars[i] == 'D') chars[i] = 'O';
                        else if (chars[i] == '1') chars[i] = 'I';
                        else if (chars[i] == '4') chars[i] = 'A';
                        else if (chars[i] == '8') chars[i] = 'B';
                        else if (chars[i] == '5') chars[i] = 'S';
                    }
                }
                // Posições de NÚMEROS: 3, 5 e 6
                else
                {
                    if (char.IsLetter(chars[i]))
                    {
                        // Letras confundidas com Dígitos: O->0, I->1, S->5, G->6, B->8
                        if (chars[i] == 'O' || chars[i] == 'Q') chars[i] = '0';
                        else if (chars[i] == 'I' || chars[i] == 'L') chars[i] = '1';
                        else if (chars[i] == 'S') chars[i] = '5';
                        else if (chars[i] == 'G') chars[i] = '6';
                        else if (chars[i] == 'B') chars[i] = '8';
                    }
                }
            }

            return new string(chars);
        }

        public void Dispose()
        {
            _tesseractEngine?.Dispose();
        }
    }
}