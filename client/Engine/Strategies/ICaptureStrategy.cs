using System.Threading.Tasks;
using CodeExplainer.Engine.Models;

namespace CodeExplainer.Engine.Strategies
{
    public interface ICaptureStrategy
    {
        Task<CaptureResult> CaptureAsync(ActiveWindowInfo window);
    }
}
