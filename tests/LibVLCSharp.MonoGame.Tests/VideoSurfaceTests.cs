using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LibVLCSharp;
using LibVLCSharp.MonoGame;
using Xunit;

namespace LibVLCSharp.MonoGame.Tests
{
    public class VideoSurfaceTests
    {
        private const string TestVideoUrl =
            "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4";

        [Fact]
        public void EndToEnd_VideoPlaysAndProducesFrames()
        {
            bool receivedFrame = false;
            int frameWidth = 0;
            int frameHeight = 0;
            bool hasNonZeroPixels = false;

            using var game = new TestGame((graphicsDevice) =>
            {
                var libVLC = new LibVLC();
                var mediaPlayer = new MediaPlayer(libVLC);
                var videoSurface = new VideoSurface(graphicsDevice, mediaPlayer);
                mediaPlayer.Media = new Media(new Uri(TestVideoUrl));
                mediaPlayer.Play();

                return (libVLC, mediaPlayer, videoSurface);
            },
            (graphicsDevice, videoSurface, gameTime) =>
            {
                var texture = videoSurface.GetTexture(graphicsDevice);
                if (texture != null && videoSurface.UpdateTexture(texture))
                {
                    receivedFrame = true;
                    frameWidth = texture.Width;
                    frameHeight = texture.Height;

                    var data = new byte[texture.Width * texture.Height * 4];
                    texture.GetData(data);
                    for (int i = 0; i < data.Length; i++)
                    {
                        if (data[i] != 0)
                        {
                            hasNonZeroPixels = true;
                            break;
                        }
                    }

                    return true; // Signal to exit
                }
                return false;
            });

            game.MaxWaitSeconds = 10;
            game.Run();

            Assert.True(receivedFrame, "Expected at least one video frame");
            Assert.True(frameWidth > 0, "Frame width should be > 0");
            Assert.True(frameHeight > 0, "Frame height should be > 0");
            Assert.True(hasNonZeroPixels, "Frame data should contain non-zero pixels");
        }

        [Fact]
        public void Dispose_Idempotent()
        {
            using var game = new TestGame((graphicsDevice) =>
            {
                var libVLC = new LibVLC();
                var mediaPlayer = new MediaPlayer(libVLC);
                var videoSurface = new VideoSurface(graphicsDevice, mediaPlayer);
                return (libVLC, mediaPlayer, videoSurface);
            },
            (graphicsDevice, videoSurface, gameTime) =>
            {
                // Dispose twice, should not throw
                videoSurface.Dispose();
                videoSurface.Dispose();
                return true; // Exit immediately
            });

            game.Run();
        }

        private class TestGame : Game
        {
            private readonly Func<GraphicsDevice, (LibVLC, MediaPlayer, VideoSurface)> _setup;
            private readonly Func<GraphicsDevice, VideoSurface, GameTime, bool> _onDraw;
            private GraphicsDeviceManager _graphics;
            private LibVLC _libVLC;
            private MediaPlayer _mediaPlayer;
            private VideoSurface _videoSurface;
            private DateTime _startTime;

            public int MaxWaitSeconds { get; set; } = 5;

            public TestGame(
                Func<GraphicsDevice, (LibVLC, MediaPlayer, VideoSurface)> setup,
                Func<GraphicsDevice, VideoSurface, GameTime, bool> onDraw)
            {
                _setup = setup;
                _onDraw = onDraw;
                _graphics = new GraphicsDeviceManager(this);
            }

            protected override void LoadContent()
            {
                (_libVLC, _mediaPlayer, _videoSurface) = _setup(GraphicsDevice);
                _startTime = DateTime.UtcNow;
            }

            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(Color.Black);

                bool shouldExit = _onDraw(GraphicsDevice, _videoSurface, gameTime);
                if (shouldExit || (DateTime.UtcNow - _startTime).TotalSeconds > MaxWaitSeconds)
                {
                    Exit();
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
        }
    }
}
