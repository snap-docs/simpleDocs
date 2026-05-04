using System.Threading.Tasks;
using System.Windows;

namespace CodeExplainer.ContextCapture.Vision
{
    public interface IVisionCaptureService
    {
        Task<VisionCaptureResult> CaptureAsync(Point cursorPosition, CaptureMode mode);
    }
}
