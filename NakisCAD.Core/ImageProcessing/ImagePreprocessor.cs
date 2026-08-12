using OpenCvSharp;

namespace NakisCAD.Core.ImageProcessing;

/// <summary>
/// Goruntu on isleme: Grayscale, blur, histogram esitleme, gurultu temizleme
/// </summary>
public static class ImagePreprocessor
{
    /// <summary>
    /// Goruntuyu on isle: grayscale + blur + kontrast + gurultu temizleme
    /// </summary>
    public static Mat Process(Mat input, PreprocessOptions? options = null)
    {
        options ??= new PreprocessOptions();
        var result = new Mat();

        // 1. Grayscale donusumu (eger renkli ise)
        Mat gray = input.Channels() > 1 ? input.CvtColor(ColorConversionCodes.BGR2GRAY) : input.Clone();

        // 2. Bilateral Filter - gurultu temizlerken kenarlari korur
        if (options.BilateralFilter)
        {
            var filtered = new Mat();
            Cv2.BilateralFilter(gray, filtered, options.BilateralD, options.BilateralSigmaColor, options.BilateralSigmaSpace);
            gray.Dispose();
            gray = filtered;
        }

        // 3. Gaussian Blur - hafif yumusatma
        if (options.GaussianBlur)
        {
            var blurred = new Mat();
            Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(options.KernelSize, options.KernelSize), 0);
            gray.Dispose();
            gray = blurred;
        }

        // 4. Histogram Esitleme (CLAHE) - kontrast artirma
        if (options.Clahe)
        {
            var clahe = Cv2.CreateCLAHE(options.ClaheClipLimit, new OpenCvSharp.Size(8, 8));
            var enhanced = new Mat();
            clahe.Apply(gray, enhanced);
            gray.Dispose();
            gray = enhanced;
        }

        // 5. Median Blur - tuze/gurultu temizleme
        if (options.MedianBlur && options.MedianKernel > 0)
        {
            var cleaned = new Mat();
            Cv2.MedianBlur(gray, cleaned, options.MedianKernel);
            gray.Dispose();
            gray = cleaned;
        }

        return gray;
    }

    /// <summary>
    /// Renkli goruntuyu renkli olarak da isle (CLAHE L kanali icin)
    /// </summary>
    public static Mat ProcessColor(Mat input, PreprocessOptions? options = null)
    {
        options ??= new PreprocessOptions();

        if (input.Channels() < 3)
            return Process(input, options);

        // LAB donusumu - L kanalinaCLAHE uygula
        var lab = input.CvtColor(ColorConversionCodes.BGR2Lab);
        var channels = Cv2.Split(lab);

        if (options.Clahe)
        {
            var clahe = Cv2.CreateCLAHE(options.ClaheClipLimit, new OpenCvSharp.Size(8, 8));
            var enhanced = new Mat();
            clahe.Apply(channels[0], enhanced);
            channels[0].Dispose();
            channels[0] = enhanced;
        }

        var merged = new Mat();
        Cv2.Merge(channels, merged);
        var result = merged.CvtColor(ColorConversionCodes.Lab2BGR);

        foreach (var ch in channels) ch.Dispose();
        lab.Dispose();
        merged.Dispose();

        return result;
    }
}

public class PreprocessOptions
{
    public bool BilateralFilter { get; set; } = true;
    public int BilateralD { get; set; } = 9;
    public double BilateralSigmaColor { get; set; } = 75;
    public double BilateralSigmaSpace { get; set; } = 75;

    public bool GaussianBlur { get; set; } = false;
    public int KernelSize { get; set; } = 3;

    public bool Clahe { get; set; } = true;
    public double ClaheClipLimit { get; set; } = 2.0;

    public bool MedianBlur { get; set; } = false;
    public int MedianKernel { get; set; } = 3;
}
