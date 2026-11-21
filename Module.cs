using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace HideCornerIcons {

    /// <summary>
    /// Hides or fades every Blish HUD CornerIcon (the small icons in the top-left of the screen),
    /// and can also hide other on-screen elements from other Blish HUD modules that you pick.
    /// Deliberately adds no UI of its own - everything lives on the module's settings page.
    /// </summary>
    [Export(typeof(Module))]
    public class HideCornerIconsModule : Module {

        private static readonly Logger Logger = Logger.GetLogger<HideCornerIconsModule>();

        private SettingEntry<bool> _hideIcons;
        private SettingEntry<float> _iconOpacity;
        private SettingEntry<bool> _hoverReveal;
        private SettingEntry<float> _hoverOpacity;
        private SettingEntry<bool> _revealAll;
        private SettingEntry<KeyBinding> _toggleKey;
        private SettingEntry<bool> _hidePicked;
        private SettingEntry<KeyBinding> _pickKey;
        private SettingEntry<bool> _pickNow;

        private readonly DirectoriesManager _directories;

        // Elements from other modules that you asked to hide (identified by type and size so it survives restarts).
        private sealed class PickedElement { public string TypeName; public int W; public int H; }
        private readonly List<PickedElement> _picked = new List<PickedElement>();
        private readonly List<Control> _hiddenOthers = new List<Control>();
        private double _findTimer;
        private PickOverlay _pickOverlay;
        private PickOverlay _overlayToDispose;

        // Icons this module hid, so we only restore what we changed.
        private readonly HashSet<CornerIcon> _hidden = new HashSet<CornerIcon>();

        // Each icon's opacity from before we touched it, so it can be put back.
        private readonly Dictionary<CornerIcon, float> _originalOpacity = new Dictionary<CornerIcon, float>();

        [ImportingConstructor]
        public HideCornerIconsModule([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters) {
            _directories = moduleParameters.DirectoriesManager;
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

            _hidePicked = settings.DefineSetting(
                "HidePicked",
                true,
                () => "Hide picked elements from other modules",
                () => "Keeps elements you picked with the pick key (for example another module's hotbar) hidden. Turn off to bring them back.");

            _pickNow = settings.DefineSetting(
                "PickNow",
                false,
                () => "Pick an element to hide now",
                () => "Tick this, then click the element on screen that you want hidden (works even if the pick key doesn't). It unticks itself.");

            _pickKey = settings.DefineSetting(
                "PickKey",
                new KeyBinding(ModifierKeys.Ctrl | ModifierKeys.Alt, Keys.P),
                () => "Pick element key",
                () => "Hover over an on-screen element from another Blish HUD module and press this to hide it.");
        }

        protected override void OnModuleLoaded(EventArgs e) {
            LoadPicked();

            _toggleKey.Value.Enabled = true;
            _toggleKey.Value.Activated += OnToggleKey;
            _pickKey.Value.Enabled = true;
            _pickKey.Value.Activated += OnPickKey;
            _pickNow.SettingChanged += OnPickNowChanged;

            Notify("Hide Corner Icons 1.1.1 loaded.");

            base.OnModuleLoaded(e);
        }

        private void OnToggleKey(object sender, EventArgs e) {
            _hideIcons.Value = !_hideIcons.Value;
        }

        protected override void Update(GameTime gameTime) {
            if (_overlayToDispose != null) {
                _overlayToDispose.Dispose();
                _overlayToDispose = null;
            }

            EnforcePicked();

            _findTimer += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_findTimer >= 250) {
                _findTimer = 0;
                FindPicked();
            }

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
            float idle = _iconOpacity.Value;
            float hover = _hoverOpacity.Value;
            bool reveal = _hoverReveal.Value;

            bool anyHovered = false;
            if (reveal && _revealAll.Value) {
                foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                    if (child is CornerIcon c && c.MouseOver) {
                        anyHovered = true;
                        break;
                    }
                }
            }

            foreach (var child in GameService.Graphics.SpriteScreen.Children) {
                if (child is CornerIcon icon) {
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
        // Hiding elements that belong to other Blish HUD modules
        // =====================================================================

        private void OnPickNowChanged(object sender, ValueChangedEventArgs<bool> e) {
            if (!_pickNow.Value) return;

            _pickNow.Value = false;
            BeginPick();
        }

        private void OnPickKey(object sender, EventArgs e) {
            BeginPick();
        }

        private void BeginPick() {
            if (_pickOverlay != null) return;

            _pickOverlay = new PickOverlay {
                Message = "Click the element you want to hide. Right-click to finish."
            };
            _pickOverlay.Clicked = HideElementAt;
            _pickOverlay.Finished = EndPick;
        }

        private void EndPick() {
            _overlayToDispose = _pickOverlay;
            _pickOverlay = null;
        }

        // Finds the smallest on-screen element under the click (ignoring the mouse report) and hides it.
        private void HideElementAt(Point point) {
            Control best = null;
            int bestArea = int.MaxValue;

            foreach (var child in GameService.Graphics.SpriteScreen.Children.ToList()) {
                if (ReferenceEquals(child, _pickOverlay) || child is CornerIcon || !child.Visible) continue;
                if (child.Width <= 0 || child.Height <= 0) continue;
                if (!child.Bounds.Contains(point)) continue;

                int area = child.Width * child.Height;
                Logger.Info($"Pick candidate: {child.GetType().FullName} ({child.Width}x{child.Height})");

                if (area < bestArea) {
                    best = child;
                    bestArea = area;
                }
            }

            if (best == null) {
                _pickOverlay.Message = "Nothing found there. Click right on the element, or right-click to finish.";
                return;
            }

            string typeName = best.GetType().FullName;
            _picked.RemoveAll(p => p.TypeName == typeName && Math.Abs(p.W - best.Width) <= 2 && Math.Abs(p.H - best.Height) <= 2);
            _picked.Add(new PickedElement { TypeName = typeName, W = best.Width, H = best.Height });

            if (!_hiddenOthers.Contains(best)) _hiddenOthers.Add(best);
            best.Visible = false;
            SavePicked();

            Logger.Info($"Hiding element: {typeName} ({best.Width}x{best.Height})");
            _pickOverlay.Message = $"Hidden: {typeName} ({best.Width}x{best.Height}). Click another, or right-click to finish.";
        }

        private static void Notify(string text) {
            ScreenNotification.ShowNotification(text);
        }

        // Every frame: keep picked elements hidden, even if their module tries to show them again.
        private void EnforcePicked() {
            if (_hiddenOthers.Count == 0) return;

            if (!_hidePicked.Value) {
                RestoreOthers();
                return;
            }

            // Drop elements their module has removed.
            _hiddenOthers.RemoveAll(c => c.Parent == null);

            foreach (var c in _hiddenOthers) {
                if (c.Visible) c.Visible = false;
            }
        }

        // A few times a second: find elements matching saved picks (for example after a restart).
        private void FindPicked() {
            if (!_hidePicked.Value || _picked.Count == 0) return;

            foreach (var child in GameService.Graphics.SpriteScreen.Children.ToList()) {
                if (child is CornerIcon || _hiddenOthers.Contains(child)) continue;

                string typeName = child.GetType().FullName;
                foreach (var p in _picked) {
                    if (p.TypeName == typeName && Math.Abs(p.W - child.Width) <= 2 && Math.Abs(p.H - child.Height) <= 2) {
                        _hiddenOthers.Add(child);
                        child.Visible = false;
                        break;
                    }
                }
            }
        }

        private void RestoreOthers() {
            foreach (var c in _hiddenOthers) {
                if (c.Parent != null) c.Visible = true;
            }

            _hiddenOthers.Clear();
        }

        private string PickedPath() {
            return Path.Combine(_directories.GetFullDirectoryPath("hidecornericons"), "hidden_elements.txt");
        }

        private void LoadPicked() {
            _picked.Clear();

            try {
                string path = PickedPath();
                if (!File.Exists(path)) return;

                foreach (var line in File.ReadAllLines(path)) {
                    var parts = line.Split('|');
                    if (parts.Length == 3 && int.TryParse(parts[1], out var w) && int.TryParse(parts[2], out var h)) {
                        _picked.Add(new PickedElement { TypeName = parts[0], W = w, H = h });
                    }
                }
            } catch (Exception ex) {
                Logger.Warn(ex, "Could not read hidden elements list.");
            }
        }

        private void SavePicked() {
            try {
                File.WriteAllLines(PickedPath(), _picked.Select(p => p.TypeName + "|" + p.W + "|" + p.H));
            } catch (Exception ex) {
                Logger.Warn(ex, "Could not save hidden elements list.");
            }
        }

        protected override void Unload() {
            if (_toggleKey != null) {
                _toggleKey.Value.Activated -= OnToggleKey;
                _toggleKey.Value.Enabled = false;
            }

            if (_pickKey != null) {
                _pickKey.Value.Activated -= OnPickKey;
                _pickKey.Value.Enabled = false;
            }

            if (_pickNow != null) _pickNow.SettingChanged -= OnPickNowChanged;

            _pickOverlay?.Dispose();
            _overlayToDispose?.Dispose();
            _pickOverlay = null;
            _overlayToDispose = null;

            RestoreOthers();

            RestoreVisibility();
            RestoreOpacity();
        }

    }
}
