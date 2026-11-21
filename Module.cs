using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace HideCornerIcons {

    /// <summary>
    /// Hides or fades every Blish HUD CornerIcon (the small icons in the top-left of the screen).
    /// Deliberately adds no UI of its own - everything lives on the module's settings page.
    /// </summary>
    [Export(typeof(Module))]
    public class HideCornerIconsModule : Module {

        private static readonly Logger Logger = Logger.GetLogger<HideCornerIconsModule>();

        private SettingEntry<bool> _hideIcons;
        private SettingEntry<float> _iconOpacity;
        private SettingEntry<KeyBinding> _toggleKey;

        // Icons this module hid, so we only restore what we changed.
        private readonly HashSet<CornerIcon> _hidden = new HashSet<CornerIcon>();

        // Each icon's opacity from before we touched it, so it can be put back.
        private readonly Dictionary<CornerIcon, float> _originalOpacity = new Dictionary<CornerIcon, float>();

        [ImportingConstructor]
        public HideCornerIconsModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters) { }

        protected override void DefineSettings(SettingCollection settings) {
            _hideIcons = settings.DefineSetting(
                "HideCornerIcons",
                true,
                () => "Hide corner icons",
                () => "Completely hides the Blish HUD corner icons. Turn off to show them (at the opacity below).");

            _iconOpacity = settings.DefineSetting(
                "IconOpacity",
                0.4f,
                () => "Icon opacity",
                () => "How visible the corner icons are while they are shown. Lower is more transparent.");
            _iconOpacity.SetRange(0.1f, 1f);

            _toggleKey = settings.DefineSetting(
                "ToggleKey",
                new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.H),
                () => "Toggle key",
                () => "Press to hide or show the corner icons.");
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
                if (child is CornerIcon icon && icon.Visible) {
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
            float target = _iconOpacity.Value;

            foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                if (child is CornerIcon icon) {
                    if (!_originalOpacity.ContainsKey(icon)) {
                        _originalOpacity[icon] = icon.Opacity;
                    }

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

        protected override void Unload() {
            if (_toggleKey != null) {
                _toggleKey.Value.Activated -= OnToggleKey;
                _toggleKey.Value.Enabled = false;
            }

            RestoreVisibility();
            RestoreOpacity();
        }

    }
}
