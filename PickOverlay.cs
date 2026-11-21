using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HideCornerIcons {

    /// <summary>
    /// Full-screen helper for picking an on-screen element. Left-click reports the click position;
    /// right-click finishes. Shows its own message so it doesn't depend on notifications.
    /// </summary>
    internal class PickOverlay : Control {

        public Action<Point> Clicked { get; set; }
        public Action Finished { get; set; }
        public string Message { get; set; } = "";

        public PickOverlay() {
            Parent = GameService.Graphics.SpriteScreen;
            Size = GameService.Graphics.SpriteScreen.Size;
            Location = Point.Zero;
            ZIndex = 9999;

            LeftMouseButtonPressed += (s, e) => Clicked?.Invoke(RelativeMousePosition);
            RightMouseButtonPressed += (s, e) => Finished?.Invoke();
        }

        protected override CaptureType CapturesInput() {
            return CaptureType.Mouse;
        }

        protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds) {
            // Light dim so you can still see what you're clicking.
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, bounds, Color.Black * 0.25f);

            // Message banner at the top.
            var banner = new Rectangle(bounds.X, bounds.Y + 40, bounds.Width, 44);
            spriteBatch.DrawOnCtrl(this, ContentService.Textures.Pixel, banner, Color.Black * 0.8f);

            if (!string.IsNullOrEmpty(Message)) {
                spriteBatch.DrawStringOnCtrl(this, Message, GameService.Content.DefaultFont16, banner, Color.Yellow);
            }
        }

    }
}
