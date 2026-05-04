using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using CodeExplainer.Engine.Detectors;
using CodeExplainer.Engine.Managers;
using ImageMagick;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Foundation;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinRT;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace CodeExplainer.ContextCapture.Vision
{
    internal sealed class VisionCaptureService : IVisionCaptureService
    {
        private const int CursorWidth = 400;
        private const int CursorHeight = 300;
        private const int PanelFallbackWidth = 1200;
        private const int PanelFallbackHeight = 800;
        private const int FullWindowMaxWidth = 1024;
        private const int FullWindowMaxHeight = 768;

        private readonly ActiveWindowDetector _windowDetector = new();
        private readonly ClientConfig _config;

        public VisionCaptureService(ClientConfig config)
        {
            _config = config;
        }

        public async Task<VisionCaptureResult> CaptureAsync(Point cursorPosition, CaptureMode mode)
        {
            var activeWindow = _windowDetector.GetForegroundEnvironment();
            if (activeWindow.Hwnd == IntPtr.Zero || !Win32Native.GetWindowRect(activeWindow.Hwnd, out Win32Native.RECT windowRect))
            {
                return new VisionCaptureResult
                {
                    ProcessName = activeWindow.ProcessName,
                    WindowTitle = activeWindow.Title
                };
            }

            var windowBounds = RectFromWindow(windowRect);
            var cursorBounds = BuildCursorBounds(cursorPosition, windowBounds);
            Task<string> selectedTextTask = Task.Run(() => TextSanitizer.SanitizePayloadText(UIATextReader.GetSelectedText(), 5000));
            Task<string> backgroundTask = Task.Run(() => TextSanitizer.SanitizePayloadText(UIATextReader.GetFullContext(), 12000));
            Task<Rect> panelBoundsTask = Task.Run(() => DetectPanelBounds(cursorPosition, windowBounds));

            using SoftwareBitmap fullWindowBitmap = await CaptureWindowBitmapAsync(activeWindow.Hwnd);
            Task<string> ocrTask = ExtractOcrTextAsync(fullWindowBitmap);
            byte[] fullWindowSourcePng = await EncodeSoftwareBitmapAsync(fullWindowBitmap);
            Rect panelBounds = await panelBoundsTask;

            using var sourceImage = new MagickImage(fullWindowSourcePng);
            var cursorLayer = EncodeLayer(sourceImage, ToWindowRelativeViewport(cursorBounds, windowBounds), CursorWidth, CursorHeight, 50 * 1024);
            var panelLayer = EncodeLayer(sourceImage, ToWindowRelativeViewport(panelBounds, windowBounds), PanelFallbackWidth, PanelFallbackHeight, 200 * 1024);
            var fullWindowLayer = EncodeLayer(sourceImage, new MagickGeometry(0, 0, sourceImage.Width, sourceImage.Height), FullWindowMaxWidth, FullWindowMaxHeight, 400 * 1024);

            return new VisionCaptureResult
            {
                CursorRegionPng = cursorLayer,
                ActivePanelPng = panelLayer,
                FullWindowPng = fullWindowLayer,
                ExtractedTextOcr = await ocrTask,
                SelectedTextUia = await selectedTextTask,
                BackgroundContextUia = await backgroundTask,
                ProcessName = activeWindow.ProcessName,
                WindowTitle = activeWindow.Title,
                CursorScreenBounds = cursorBounds
            };
        }

        private static Rect DetectPanelBounds(Point cursorPosition, Rect windowBounds)
        {
            try
            {
                AutomationElement? current = AutomationElement.FromPoint(cursorPosition);
                int depth = 0;
                while (current != null && depth < 12)
                {
                    ControlType controlType = current.Current.ControlType;
                    if (controlType == ControlType.Pane ||
                        controlType == ControlType.Group ||
                        controlType == ControlType.Document ||
                        controlType == ControlType.Custom ||
                        controlType == ControlType.Window)
                    {
                        Rect bounds = current.Current.BoundingRectangle;
                        if (IsReasonablePanel(bounds, windowBounds))
                        {
                            return bounds;
                        }
                    }

                    current = TreeWalker.RawViewWalker.GetParent(current);
                    depth++;
                }
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Vision", $"Active-panel detection fell back: {ex.Message}");
            }

            return BuildCenteredBounds(cursorPosition, windowBounds, PanelFallbackWidth, PanelFallbackHeight);
        }

        private static bool IsReasonablePanel(Rect bounds, Rect windowBounds)
        {
            if (bounds.Width < 400 || bounds.Height < 300)
            {
                return false;
            }

            double maxArea = windowBounds.Width * windowBounds.Height * 0.8;
            return bounds.Width * bounds.Height <= maxArea;
        }

        private static Rect BuildCursorBounds(Point cursorPosition, Rect windowBounds)
        {
            return BuildCenteredBounds(cursorPosition, windowBounds, CursorWidth, CursorHeight);
        }

        private static Rect BuildCenteredBounds(Point center, Rect clipBounds, int width, int height)
        {
            double left = Math.Max(clipBounds.Left, center.X - (width / 2.0));
            double top = Math.Max(clipBounds.Top, center.Y - (height / 2.0));
            double right = Math.Min(clipBounds.Right, left + width);
            double bottom = Math.Min(clipBounds.Bottom, top + height);
            left = Math.Max(clipBounds.Left, right - width);
            top = Math.Max(clipBounds.Top, bottom - height);
            return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }

        private static Rect RectFromWindow(Win32Native.RECT rect)
        {
            return new Rect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        }

        private static MagickGeometry ToWindowRelativeViewport(Rect bounds, Rect windowBounds)
        {
            int x = Math.Max(0, (int)Math.Round(bounds.Left - windowBounds.Left));
            int y = Math.Max(0, (int)Math.Round(bounds.Top - windowBounds.Top));
            int width = Math.Max(1, (int)Math.Round(Math.Min(bounds.Width, windowBounds.Width - x)));
            int height = Math.Max(1, (int)Math.Round(Math.Min(bounds.Height, windowBounds.Height - y)));
            return new MagickGeometry(x, y, (uint)width, (uint)height);
        }

        private static byte[] EncodeLayer(MagickImage source, MagickGeometry crop, int maxWidth, int maxHeight, int targetBytes)
        {
            using var layer = source.CloneArea(crop);
            if (layer.Width > maxWidth || layer.Height > maxHeight)
            {
                layer.Resize(new MagickGeometry((uint)maxWidth, (uint)maxHeight)
                {
                    IgnoreAspectRatio = false
                });
            }

            layer.Strip();
            layer.Format = MagickFormat.Png8;
            layer.Quality = 80;

            int colors = 256;
            byte[] bytes = layer.ToByteArray();
            while (bytes.Length > targetBytes && colors >= 32)
            {
                layer.Quantize(new QuantizeSettings
                {
                    Colors = (uint)colors,
                    DitherMethod = DitherMethod.No
                });
                bytes = layer.ToByteArray();
                colors /= 2;
            }

            return bytes;
        }

        private static async Task<string> ExtractOcrTextAsync(SoftwareBitmap bitmap)
        {
            try
            {
                OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null)
                {
                    return string.Empty;
                }

                using SoftwareBitmap ocrBitmap = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8);
                OcrResult ocrResult = await engine.RecognizeAsync(ocrBitmap);
                return TextSanitizer.SanitizePayloadText(ocrResult.Text, 12000);
            }
            catch (Exception ex)
            {
                RuntimeLog.Warn("Vision", $"Full-window OCR failed: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task<SoftwareBitmap> CaptureWindowBitmapAsync(IntPtr hwnd)
        {
            GraphicsCaptureItem item = CreateCaptureItem(hwnd);
            IDirect3DDevice device = CreateDirect3DDevice();
            var frameTcs = new TaskCompletionSource<SoftwareBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;

            TypedEventHandler<Direct3D11CaptureFramePool, object>? handler = null;
            handler = (_, _) =>
            {
                try
                {
                    using Direct3D11CaptureFrame frame = framePool.TryGetNextFrame();
                    if (frame == null)
                    {
                        return;
                    }

                    _ = SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface).AsTask().ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            frameTcs.TrySetException(task.Exception!.GetBaseException());
                            return;
                        }

                        frameTcs.TrySetResult(task.Result);
                    });
                }
                catch (Exception ex)
                {
                    frameTcs.TrySetException(ex);
                }
            };

            framePool.FrameArrived += handler;
            session.StartCapture();

            try
            {
                return await frameTcs.Task.WaitAsync(TimeSpan.FromMilliseconds(Math.Max(250, _config.VisionCaptureTimeoutMs)));
            }
            finally
            {
                framePool.FrameArrived -= handler;
                device.Dispose();
            }
        }

        private static async Task<byte[]> EncodeSoftwareBitmapAsync(SoftwareBitmap bitmap)
        {
            using SoftwareBitmap converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            using var stream = new InMemoryRandomAccessStream();
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(converted);
            encoder.IsThumbnailGenerated = false;
            await encoder.FlushAsync();
            stream.Seek(0);

            byte[] bytes = new byte[(int)stream.Size];
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
            return bytes;
        }

        private static GraphicsCaptureItem CreateCaptureItem(IntPtr hwnd)
        {
            using IObjectReference factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var interop = factory.AsInterface<IGraphicsCaptureItemInterop>();
            Guid iid = typeof(GraphicsCaptureItem).GUID;
            IntPtr itemPointer = interop.CreateForWindow(hwnd, ref iid);
            try
            {
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }

        private static IDirect3DDevice CreateDirect3DDevice()
        {
            var result = D3D11.D3D11CreateDevice(
                null,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                null,
                out ID3D11Device? device);
            result.CheckError();
            if (device == null)
            {
                throw new InvalidOperationException("Unable to create a Direct3D11 device for graphics capture.");
            }

            using IDXGIDevice dxgiDevice = device.QueryInterface<IDXGIDevice>();
            int hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out IntPtr inspectableDevice);
            Marshal.ThrowExceptionForHR(hr);

            try
            {
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
            }
            finally
            {
                Marshal.Release(inspectableDevice);
                device.Dispose();
            }
        }

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice")]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            IntPtr CreateForWindow(IntPtr window, [In] ref Guid iid);
        }
    }
}
