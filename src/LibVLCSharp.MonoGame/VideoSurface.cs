using System;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LibVLCSharp;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using static LibVLCSharp.MediaPlayer;
using Device = SharpDX.Direct3D11.Device;
using Texture2D = Microsoft.Xna.Framework.Graphics.Texture2D;
using DxTexture2D = SharpDX.Direct3D11.Texture2D;

namespace LibVLCSharp.MonoGame
{
    public unsafe class VideoSurface : IDisposable
    {
        private readonly Device _mgDevice;
        private readonly DeviceContext _mgContext;
        private readonly GraphicsDevice _graphicsDevice;

        private readonly Device _vlcDevice;
        private readonly DeviceContext _vlcContext;

        private DxTexture2D _sharedTexture;
        private RenderTargetView _vlcRTV;

        private volatile bool _newFrameAvailable;

        private uint _videoWidth;
        private uint _videoHeight;
        private Texture2D _sharedMgTexture;

        private bool _disposed;

        // Store delegates to prevent GC collection
        private readonly OutputSetup _outputSetup;
        private readonly OutputCleanup _outputCleanup;
        private readonly SetWindow _setWindow;
        private readonly UpdateOutput _updateOutput;
        private readonly Swap _swap;
        private readonly MakeCurrent _makeCurrent;
        private readonly OutputSelectPlane _selectPlane;

        private OutputResize _reportSize;
        private IntPtr _reportOpaque;

        public VideoSurface(GraphicsDevice graphicsDevice, MediaPlayer mediaPlayer)
        {
            var handle = graphicsDevice.Handle;
            if (handle is not Device sharpDxDevice)
                throw new InvalidOperationException(
                    "GraphicsDevice.Handle is not a SharpDX.Direct3D11.Device. " +
                    "VideoSurface requires MonoGame WindowsDX.");

            _graphicsDevice = graphicsDevice;
            _mgDevice = sharpDxDevice;
            _mgContext = _mgDevice.ImmediateContext;

            // Enable multithread protection on MonoGame's device
            using var multithread = _mgDevice.QueryInterface<SharpDX.Direct3D11.Multithread>();
            multithread.SetMultithreadProtected(true);

            // Create a separate D3D11 device for VLC with video support
            _vlcDevice = new Device(DriverType.Hardware, DeviceCreationFlags.VideoSupport);
            _vlcContext = _vlcDevice.ImmediateContext;

            // Pin delegates to prevent GC and set up output callbacks
            _outputSetup = OutputSetupCallback;
            _outputCleanup = OutputCleanupCallback;
            _setWindow = SetWindowCallback;
            _updateOutput = UpdateOutputCallback;
            _swap = SwapCallback;
            _makeCurrent = MakeCurrentCallback;
            _selectPlane = SelectPlaneCallback;

            mediaPlayer.SetOutputCallbacks(
                VideoEngine.D3D11,
                _outputSetup,
                _outputCleanup,
                _setWindow,
                _updateOutput,
                _swap,
                _makeCurrent,
                null,
                null,
                _selectPlane);
        }

        public Texture2D GetTexture()
        {
            return _sharedMgTexture;
        }

        public bool UpdateTexture()
        {
            if (!_newFrameAvailable)
                return false;
            _newFrameAvailable = false;
            return true;
        }

        #region VLC Callbacks

        private bool OutputSetupCallback(ref IntPtr opaque, SetupDeviceConfig* config, ref SetupDeviceInfo setup)
        {
            setup.D3D11.DeviceContext = _vlcContext.NativePointer.ToPointer();
            Marshal.AddRef(_vlcContext.NativePointer);
            return true;
        }

        private void OutputCleanupCallback(IntPtr opaque)
        {
            ReleaseTextures();
        }

        private void SetWindowCallback(IntPtr opaque, OutputResize reportSizeChange,
            MouseMove mouseMove, MousePress mousePress, MouseRelease mouseRelease, IntPtr reportOpaque)
        {
            _reportOpaque = reportOpaque;
            _reportSize = reportSizeChange;
        }

        private bool UpdateOutputCallback(IntPtr opaque, RenderConfig* config, ref OutputConfig output)
        {
            ReleaseTextures();

            _videoWidth = config->Width;
            _videoHeight = config->Height;

            // Create shared texture on VLC's device (which we created with VideoSupport)
            _sharedTexture = new DxTexture2D(_vlcDevice, new Texture2DDescription
            {
                Width = (int)config->Width,
                Height = (int)config->Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = SharpDX.Direct3D11.ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                OptionFlags = ResourceOptionFlags.Shared
            });

            // Create render target view on VLC device for the shared texture
            _vlcRTV = new RenderTargetView(_vlcDevice, _sharedTexture);

            // Open shared texture on MonoGame's device via Texture2D.FromSharedHandle
            using var dxgiResource = _sharedTexture.QueryInterface<SharpDX.DXGI.Resource>();
            var sharedHandle = dxgiResource.SharedHandle;
            _sharedMgTexture = Texture2D.FromSharedHandle(
                _graphicsDevice, sharedHandle,
                (int)config->Width, (int)config->Height,
                SurfaceFormat.Color);

            // Set render target
            _vlcContext.OutputMerger.SetRenderTargets(_vlcRTV);

            // Set output config
            output.Union.DxgiFormat = (int)Format.R8G8B8A8_UNorm;
            output.FullRange = true;
            output.ColorSpace = ColorSpace.BT709;
            output.ColorPrimaries = ColorPrimaries.BT709;
            output.TransferFunction = TransferFunction.SRGB;
            output.Orientation = VideoOrientation.TopLeft;

            return true;
        }

        private bool MakeCurrentCallback(IntPtr opaque, bool enter)
        {
            if (_disposed)
                return false;

            if (enter)
            {
                var rtv = _vlcRTV;
                if (rtv == null)
                    return false;
                // Clear the VLC render target view (VLC is about to draw)
                _vlcContext.ClearRenderTargetView(rtv, new SharpDX.Mathematics.Interop.RawColor4(0f, 0f, 0f, 1f));
            }
            else
            {
                // VLC finished rendering, signal new frame
                _newFrameAvailable = true;
            }
            return true;
        }

        private void SwapCallback(IntPtr opaque)
        {
            // No-op: MonoGame controls presentation
        }

        private bool SelectPlaneCallback(IntPtr opaque, UIntPtr plane, void* output)
        {
            return (ulong)plane == 0;
        }

        #endregion

        private void ReleaseTextures()
        {
            _vlcRTV?.Dispose();
            _vlcRTV = null;

            _sharedMgTexture?.Dispose();
            _sharedMgTexture = null;

            _sharedTexture?.Dispose();
            _sharedTexture = null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            ReleaseTextures();

            _vlcContext?.Dispose();
            _vlcDevice?.Dispose();
        }
    }
}
