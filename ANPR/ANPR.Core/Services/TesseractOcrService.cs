using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenCvSharp;
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
        private const string WHITELIST = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        public TesseractOcrService(string tessDataPath)
        {
            try
            {
                // EngineMode.LstmOnly é geralmente mais robusto para o Tesseract 4/5
                _tesseractEngine = new TesseractEngine(tessDataPath, "por", EngineMode.LstmOnly);

                // Configurações globais para restringir o reconhecimento
                _tesseractEngine.SetVariable("tessedit_char_whitelist", WHITELIST);
                _tesseractEngine.SetVariable("user_defined_dpi", "70"); // Ajuda no redimensionamento interno
                _tesseractEngine.SetVariable("tessedit_pageseg_mode", ((int)PageSegMode.SingleBlock).ToString());

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

            // MANTENHA ESTE USING! Ele é vital para não travar o PC.
            using (Mat processed = PreprocessPlateOptimized(plateImage))
            {
                // === DEBUG (Salva imagem para conferirmos) ===
                string debugFilename = $"debug_{DateTime.Now:HH-mm-ss-fff}.png";
                Cv2.ImWrite(debugFilename, processed);
                Console.WriteLine($"[DEBUG] Imagem salva: {debugFilename}");
                // ============================================

                // Converte para memória
                byte[] imageBytes = processed.ToBytes(".png");

                using (var pix = Pix.LoadFromMemory(imageBytes))
                {
                    using (var page = _tesseractEngine.Process(pix, PageSegMode.SingleBlock))
                    {
                        rawText = page.GetText().Trim().ToUpper()
                            .Replace(" ", "")
                            .Replace("\n", "")
                            .Replace("\r", "")
                            .Replace("\t", "");
                        confidence = page.GetMeanConfidence();
                    }
                }

                processedText = PostProcess(rawText);
                isValid = _plateCharPattern.IsMatch(processedText);
            } // Aqui a memória da imagem 'processed' é liberada automaticamente

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
        /// Pré-processamento V12: "Gigante e Grosso"
        /// Aumenta a imagem para 120px e usa morfologia pesada para fechar buracos.
        /// </summary>
        private Mat PreprocessPlateOptimized(Mat plateImage)
        {
            // 1. Redimensionamento Agressivo (120px)
            // Ao trabalhar com uma imagem grande, os "pontinhos" de ruído ficam insignificantes
            // comparados ao tamanho da letra, facilitando a filtragem.
            double scaleFactor = 120.0 / plateImage.Height;
            Mat resized = new Mat();
            Cv2.Resize(plateImage, resized, new Size(0, 0), scaleFactor, scaleFactor, InterpolationFlags.Cubic);

            // 2. Escala de Cinza
            Mat gray = new Mat();
            Cv2.CvtColor(resized, gray, ColorConversionCodes.BGR2GRAY);

            // 3. Gaussian Blur (5x5)
            // Borra os pontos tracejados para que se conectem.
            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(5, 5), 0);

            // 4. Binarização Otsu
            Mat binary = new Mat();
            Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // [O TRUQUE V12] 5. Erosão Forte (3x3)
            // Como a imagem agora é grande (120px), podemos usar um kernel 3x3 sem medo.
            // Isso vai fazer as letras pretas ficarem bem "gordas" e sólidas, fechando qualquer falha.
            Mat element = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            Cv2.Erode(binary, binary, element);

            // 6. Borda Branca (Necessária para PSM 6 funcionar bem)
            Mat finalProcessed = new Mat();
            Cv2.CopyMakeBorder(
                binary,
                finalProcessed,
                20, 20, 20, 20, // Borda generosa
                BorderTypes.Constant,
                Scalar.All(255)
            );

            // Limpeza
            if (resized != plateImage) resized.Dispose();
            gray.Dispose();
            binary.Dispose();
            // element.Dispose() // Opcional

            return finalProcessed;
        }

        /// <summary>
        /// Pós-processamento para forçar o formato Mercosul LLL#L## e corrigir erros comuns
        /// </summary>
        public string PostProcess(string rawText)
        {
            // 1. Limpeza
            string cleaned = new string(rawText.Where(c => WHITELIST.Contains(c) || c == '|').ToArray());

            // 2. SMART SHIFT V3 (Mais tolerante)
            if (cleaned.Length >= 8)
            {
                char firstChar = cleaned[0];

                char charAt3 = cleaned[3];
                char charAt4 = cleaned[4];

                // [AJUSTE] Adicionei 'G' e 'b' aqui. 
                // Às vezes o 6 vira G, ou o 0 vira G, ou 8 vira B/b.
                // Se a posição 4 tiver um 'G', assumimos que é um número mal lido e ativamos o Shift.
                bool index4IsDigitLike = char.IsDigit(charAt4) || "ODQBSZGb".Contains(charAt4);

                bool index3IsLetterLike = char.IsLetter(charAt3) || "015".Contains(charAt3);

                bool isShifted = false;

                // Aceita K também como borda (apareceu no seu log "KETTOF...")
                if ("LI|1K".Contains(firstChar))
                {
                    if (index3IsLetterLike && index4IsDigitLike)
                    {
                        isShifted = true;
                    }
                }

                if (isShifted)
                {
                    Console.WriteLine($"🔧 Smart Shift: Removido '{firstChar}' inicial.");
                    cleaned = cleaned.Substring(1, 7);
                }
                else
                {
                    cleaned = cleaned.Substring(0, 7);
                }
            }
            else if (cleaned.Length > 7)
            {
                cleaned = cleaned.Substring(0, 7);
            }

            if (cleaned.Length < 7) return string.Empty;

            char[] chars = cleaned.ToCharArray();

            // 3. Correções posicionais
            for (int i = 0; i < chars.Length; i++)
            {
                // === LETRAS ===
                if (i == 0 || i == 1 || i == 2 || i == 4)
                {
                    if (char.IsDigit(chars[i]) || chars[i] == '|')
                    {
                        if (chars[i] == '0') chars[i] = 'O';
                        else if (chars[i] == '1' || chars[i] == '|') chars[i] = 'I';
                        else if (chars[i] == '2') chars[i] = 'Z';
                        else if (chars[i] == '4') chars[i] = 'A';
                        else if (chars[i] == '5') chars[i] = 'S';
                        else if (chars[i] == '6') chars[i] = 'G';
                        else if (chars[i] == '7') chars[i] = 'Z';
                        else if (chars[i] == '8') chars[i] = 'B';
                    }
                    // [NOVO] Correção específica: E -> G (Muito comum)
                    else if (chars[i] == 'E' || chars[i] == 'C')
                    {
                        // Se for Mercosul, G é muito mais comum que E em posições confusas
                        // Mas cuidado para não trocar placas reais com E. 
                        // Deixe o Fuzzy Match lidar com isso ou descomente abaixo se o erro E->G for frequente:
                        // chars[i] = 'G'; 
                    }
                }
                // === NÚMEROS ===
                else
                {
                    if (char.IsLetter(chars[i]) || chars[i] == '|')
                    {
                        if (chars[i] == 'O' || chars[i] == 'Q' || chars[i] == 'D') chars[i] = '0';
                        else if (chars[i] == 'U') chars[i] = '0';
                        else if (chars[i] == 'I' || chars[i] == 'L' || chars[i] == '|') chars[i] = '1';
                        else if (chars[i] == 'Z') chars[i] = '2';
                        else if (chars[i] == 'A') chars[i] = '4';
                        else if (chars[i] == 'S') chars[i] = '5';
                        else if (chars[i] == 'G' || chars[i] == 'b') chars[i] = '6'; // G ou b vira 6
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