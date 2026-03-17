# LibVLCSharp.MonoGame

MonoGame integration for [LibVLCSharp](https://github.com/videolan/libvlcsharp), enabling hardware-accelerated video playback in MonoGame applications.

Uses Direct3D 11 shared textures to transfer video frames from VLC to MonoGame without any CPU roundtrip.

## Requirements

- .NET 8.0+
- Windows (DirectX backend)
- MonoGame 3.8.4+ (WindowsDX)
- LibVLCSharp 4.x and LibVLC 4.x runtime

## Installation

```
dotnet add package LibVLCSharp.MonoGame
dotnet add package VideoLAN.LibVLC.Windows
```

The LibVLCSharp 4.x preview packages are hosted on the [VideoLAN feedz.io feed](https://f.feedz.io/videolan/preview/nuget/index.json). Add it to your NuGet sources:

```
dotnet nuget add source https://f.feedz.io/videolan/preview/nuget/index.json --name videolan-preview
```

## Usage

```csharp
using LibVLCSharp;
using LibVLCSharp.MonoGame;

// In your Game class:
private VideoSurface _videoSurface;
private LibVLC _libVLC;
private MediaPlayer _mediaPlayer;

protected override void LoadContent()
{
    _libVLC = new LibVLC();
    _mediaPlayer = new MediaPlayer(_libVLC);
    _videoSurface = new VideoSurface(GraphicsDevice, _mediaPlayer);

    _mediaPlayer.Media = new Media(new Uri("http://example.com/video.mp4"));
    _mediaPlayer.Play();
}

protected override void Draw(GameTime gameTime)
{
    GraphicsDevice.Clear(Color.Black);

    var texture = _videoSurface.GetTexture(GraphicsDevice);
    if (texture != null && _videoSurface.UpdateTexture(texture))
    {
        _spriteBatch.Begin();
        _spriteBatch.Draw(texture, GraphicsDevice.Viewport.Bounds, Color.White);
        _spriteBatch.End();
    }

    base.Draw(gameTime);
}

protected override void UnloadContent()
{
    _mediaPlayer?.Stop();
    _videoSurface?.Dispose();
    _mediaPlayer?.Dispose();
    _libVLC?.Dispose();
}
```

## How it works

`VideoSurface` creates a secondary D3D11 device for VLC and a shared texture accessible by both devices. VLC renders directly to this texture via its D3D11 output callbacks. MonoGame opens the same texture through a shared NTHANDLE and performs a single GPU-to-GPU copy (`CopyResource`) each frame to make the video available as a standard `Texture2D`. There is no CPU readback involved, so performance stays well within budget for real-time rendering.

### Zero-copy path (pending)

The [`perf/no-gpu-copy`](https://github.com/mfkl/LibVLCSharp.MonoGame/tree/perf/no-gpu-copy) branch eliminates even the per-frame GPU copy by wrapping the shared texture directly as a MonoGame `Texture2D`. This relies on a proposed `Texture2D.FromSharedHandle` API that is not yet available in a released MonoGame version. Once the upstream MonoGame PR is merged and released, this branch will be integrated into master.

## Building from source

```bash
dotnet build LibVLCSharp.MonoGame.sln
dotnet test tests/LibVLCSharp.MonoGame.Tests/
dotnet run --project samples/LibVLCSharp.MonoGame.Sample/
```

## License

Licensed under the [LGPL-2.1-or-later](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html).
