using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using LibVLCSharp;
using LibVLCSharp.MonoGame;

namespace LibVLCSharp.MonoGame.Sample
{
    public class VideoGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private VideoSurface _videoSurface;
        private Texture2D _videoTexture;
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private int _frameCount;

        public VideoGame()
        {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = 1280;
            _graphics.PreferredBackBufferHeight = 720;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _libVLC = new LibVLC(enableDebugLogs: true);
            _mediaPlayer = new MediaPlayer(_libVLC);
            _videoSurface = new VideoSurface(GraphicsDevice, _mediaPlayer);

            _mediaPlayer.Media = new Media(new Uri(
                "http://commondatastorage.googleapis.com/gtv-videos-bucket/sample/BigBuckBunny.mp4"));
            _mediaPlayer.Play();
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.CornflowerBlue);

            // Re-fetch texture each frame in case video dimensions changed
            var texture = _videoSurface.GetTexture();
            if (texture != _videoTexture)
                _videoTexture = texture;

            if (_videoTexture != null && _videoSurface.UpdateTexture())
            {
                _frameCount++;
                Console.WriteLine($"[VideoGame] Frame {_frameCount} updated ({_videoTexture.Width}x{_videoTexture.Height})");
            }

            if (_videoTexture != null)
            {
                _spriteBatch.Begin();
                _spriteBatch.Draw(_videoTexture, GraphicsDevice.Viewport.Bounds, Color.White);
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
    }
}
