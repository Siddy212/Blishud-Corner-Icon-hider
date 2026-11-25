using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace HideCornerIcons {

    /// <summary>
    /// Hides or fades every Blish HUD CornerIcon (the small icons in the top-left of the screen).
    /// Deliberately adds no UI of its own - everything lives on the module's settings page.
    /// </summary>
    [Export(typeof(Blish_HUD.Modules.Module))]
    public class HideCornerIconsModule : Blish_HUD.Modules.Module {

        private static readonly Logger Logger = Logger.GetLogger<HideCornerIconsModule>();

        private SettingEntry<bool> _hideIcons;
        private SettingEntry<float> _iconOpacity;
        private SettingEntry<bool> _hoverReveal;
        private SettingEntry<float> _hoverOpacity;
        private SettingEntry<bool> _revealAll;
        private SettingEntry<KeyBinding> _toggleKey;
        private SettingEntry<bool> _showLogo;
        private SettingEntry<float> _logoOpacity;

        // Our own corner icon: a small logo that opens Blish HUD's menu. Never hidden or faded by the rules below.
        private CornerIcon _logoIcon;
        private Texture2D _logoTexture;
        private bool _logoFailed;

        // Icons this module hid, so we only restore what we changed.
        private readonly HashSet<CornerIcon> _hidden = new HashSet<CornerIcon>();

        // Each icon's opacity from before we touched it, so it can be put back.
        private readonly Dictionary<CornerIcon, float> _originalOpacity = new Dictionary<CornerIcon, float>();

        [ImportingConstructor]
        public HideCornerIconsModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters) {
        }

        protected override void DefineSettings(SettingCollection settings) {
            _hideIcons = settings.DefineSetting(
                "HideCornerIcons",
                false,
                () => "Hide corner icons completely",
                () => "Fully hides the icons so they can't be seen or clicked. Leave off to use the opacity and hover options below.");

            _iconOpacity = settings.DefineSetting(
                "IconOpacity",
                0f,
                () => "Icon opacity",
                () => "How visible the icons are normally. 0% is invisible (but still clickable).");
            _iconOpacity.SetRange(0f, 1f);

            _hoverReveal = settings.DefineSetting(
                "HoverReveal",
                true,
                () => "Reveal on hover",
                () => "Fade an icon in to the hover opacity while your cursor is over it.");

            _hoverOpacity = settings.DefineSetting(
                "HoverOpacity",
                1f,
                () => "Hover opacity",
                () => "How visible an icon becomes while your cursor is over it.");
            _hoverOpacity.SetRange(0f, 1f);

            _revealAll = settings.DefineSetting(
                "RevealAll",
                false,
                () => "Reveal all icons together",
                () => "Hovering any one icon reveals all of them, which helps you find invisible ones.");

            _toggleKey = settings.DefineSetting(
                "ToggleKey",
                new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.H),
                () => "Toggle key",
                () => "Press to hide or show the corner icons.");

            _showLogo = settings.DefineSetting(
                "ShowLogo",
                true,
                () => "Show Blish HUD logo icon",
                () => "Adds one small logo icon to the corner icon bar. Click it to open the Blish HUD window on its Settings tab. It is never hidden by the options above.");

            _logoOpacity = settings.DefineSetting(
                "LogoOpacity",
                0.6f,
                () => "Logo icon opacity",
                () => "How visible the logo icon is normally. It becomes fully visible while your cursor is over it.");
            _logoOpacity.SetRange(0.05f, 1f);
        }

        protected override void OnModuleLoaded(EventArgs e) {
            _toggleKey.Value.Enabled = true;
            _toggleKey.Value.Activated += OnToggleKey;

            base.OnModuleLoaded(e);
        }

        private void OnToggleKey(object sender, EventArgs e) {
            _hideIcons.Value = !_hideIcons.Value;
        }

        protected override void Update(GameTime gameTime) {
            UpdateLogo();

            // Run every frame so icons that modules add or re-show later are handled too.
            if (_hideIcons.Value) {
                HideIcons();
            } else {
                RestoreVisibility();
                ApplyOpacity();
            }
        }

        private void HideIcons() {
            foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                if (child is CornerIcon icon && icon.Visible && !ReferenceEquals(icon, _logoIcon)) {
                    _hidden.Add(icon);
                    icon.Visible = false;
                }
            }
        }

        private void RestoreVisibility() {
            if (_hidden.Count == 0) return;

            foreach (var icon in _hidden) {
                icon.Visible = true;
            }

            _hidden.Clear();
        }

        private void ApplyOpacity() {
            float idle = _iconOpacity.Value;
            float hover = _hoverOpacity.Value;
            bool reveal = _hoverReveal.Value;

            bool anyHovered = false;
            if (reveal && _revealAll.Value) {
                foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                    if (child is CornerIcon c && !ReferenceEquals(c, _logoIcon) && c.MouseOver) {
                        anyHovered = true;
                        break;
                    }
                }
            }

            foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                if (child is CornerIcon icon && !ReferenceEquals(icon, _logoIcon)) {
                    if (!_originalOpacity.ContainsKey(icon)) {
                        _originalOpacity[icon] = icon.Opacity;
                    }

                    float target = (reveal && (icon.MouseOver || anyHovered)) ? hover : idle;

                    if (Math.Abs(icon.Opacity - target) > 0.001f) {
                        icon.Opacity = target;
                    }
                }
            }
        }

        private void RestoreOpacity() {
            foreach (var pair in _originalOpacity) {
                pair.Key.Opacity = pair.Value;
            }

            _originalOpacity.Clear();
        }

        // =====================================================================
        // Logo icon that opens Blish HUD's menu
        // =====================================================================

        // Runs every frame on the main thread: creates, updates or removes the logo icon.
        private void UpdateLogo() {
            if (!_showLogo.Value) {
                DisposeLogoIcon();
                return;
            }

            if (_logoIcon == null && !_logoFailed) {
                CreateLogoIcon();
            }

            if (_logoIcon != null) {
                float target = _logoIcon.MouseOver ? 1f : _logoOpacity.Value;
                if (Math.Abs(_logoIcon.Opacity - target) > 0.001f) {
                    _logoIcon.Opacity = target;
                }

                if (!_logoIcon.Visible) _logoIcon.Visible = true;
            }
        }

        private void CreateLogoIcon() {
            try {
                if (_logoTexture == null) {
                    _logoTexture = LoadLogoTexture();
                }

                _logoIcon = new CornerIcon {
                    Icon = _logoTexture,
                    BasicTooltipText = "Open Blish HUD settings",
                    Priority = 1000
                };
                _logoIcon.Click += OnLogoClicked;
            } catch (Exception ex) {
                _logoFailed = true;
                Logger.Warn(ex, "Could not create the logo icon.");
            }
        }

        // The logo is embedded in the module's DLL as "logo.png". Replace logo.png in the project to change it.
        private static Texture2D LoadLogoTexture() {
            using (var stream = typeof(HideCornerIconsModule).Assembly.GetManifestResourceStream("logo.png")) {
                if (stream == null) throw new FileNotFoundException("Embedded logo.png not found.");

                Texture2D tex;
                using (var ctx = GameService.Graphics.LendGraphicsDeviceContext()) {   // the graphics device must be borrowed, not read directly
                    tex = Texture2D.FromStream(ctx.GraphicsDevice, stream);
                }

                // Blish HUD draws with premultiplied alpha, so convert to avoid pale edges.
                var data = new Color[tex.Width * tex.Height];
                tex.GetData(data);
                for (int i = 0; i < data.Length; i++) {
                    data[i] = Color.FromNonPremultiplied(data[i].R, data[i].G, data[i].B, data[i].A);
                }
                tex.SetData(data);

                return tex;
            }
        }

        private void OnLogoClicked(object sender, Blish_HUD.Input.MouseEventArgs e) {
            OpenBlishSettings();
        }

        // Opens the main Blish HUD window on its Settings tab. Uses reflection so a name that differs between
        // Blish HUD versions logs a message instead of stopping the module from loading.
        private void OpenBlishSettings() {
            try {
                var overlay = GameService.Overlay;
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                object window = overlay.GetType().GetProperty("BlishHudWindow", flags)?.GetValue(overlay);
                if (window == null) {
                    Logger.Warn("GameService.Overlay.BlishHudWindow was not found.");
                    Notify("Could not open Blish HUD's window. Use its hotkey (Ctrl+Shift+B).");
                    return;
                }

                object tab = overlay.GetType().GetProperty("SettingsTab", flags)?.GetValue(overlay);
                if (tab != null) {
                    var selected = window.GetType().GetProperty("SelectedTab", flags);
                    if (selected != null && selected.CanWrite && selected.PropertyType.IsInstanceOfType(tab)) {
                        selected.SetValue(window, tab);
                    }
                } else {
                    Logger.Info("Overlay.SettingsTab not found; opening the Blish HUD window on its current tab.");
                }

                var show = window.GetType().GetMethod("Show", flags, null, Type.EmptyTypes, null);
                if (show == null) {
                    Logger.Warn("Show() was not found on the Blish HUD window.");
                    Notify("Could not open Blish HUD's window. Use its hotkey (Ctrl+Shift+B).");
                    return;
                }

                show.Invoke(window, null);
            } catch (Exception ex) {
                Logger.Warn(ex, "Could not open the Blish HUD settings.");
                Notify("Could not open Blish HUD's window. Use its hotkey (Ctrl+Shift+B).");
            }
        }

        private void DisposeLogoIcon() {
            if (_logoIcon == null) return;

            _logoIcon.Click -= OnLogoClicked;
            _logoIcon.Dispose();
            _logoIcon = null;
        }

        private static void Notify(string text) {
            ScreenNotification.ShowNotification(text);
        }

        protected override void Unload() {
            DisposeLogoIcon();
            _logoTexture?.Dispose();
            _logoTexture = null;

            if (_toggleKey != null) {
                _toggleKey.Value.Activated -= OnToggleKey;
                _toggleKey.Value.Enabled = false;
            }

            RestoreVisibility();
            RestoreOpacity();
        }

    }
}
