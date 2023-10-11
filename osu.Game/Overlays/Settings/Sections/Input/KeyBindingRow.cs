// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.Resources.Localisation.Web;
using osuTK;
using osuTK.Graphics;
using osuTK.Input;

namespace osu.Game.Overlays.Settings.Sections.Input
{
    public partial class KeyBindingRow : Container, IFilterable
    {
        /// <summary>
        /// Invoked when the binding of this row is updated with a change being written.
        /// </summary>
        public KeyBindingUpdated? BindingUpdated { get; set; }

        public delegate void KeyBindingUpdated(KeyBindingRow sender, KeyBindingUpdatedEventArgs args);

        public record KeyBindingUpdatedEventArgs(Guid KeyBindingID, string KeyCombinationString);

        /// <summary>
        /// Whether left and right mouse button clicks should be included in the edited bindings.
        /// </summary>
        public bool AllowMainMouseButtons { get; init; }

        /// <summary>
        /// The bindings to display in this row.
        /// </summary>
        public BindableList<RealmKeyBinding> KeyBindings { get; } = new BindableList<RealmKeyBinding>();

        /// <summary>
        /// The default key bindings for this row.
        /// </summary>
        public IEnumerable<KeyCombination> Defaults { get; init; } = Array.Empty<KeyCombination>();

        #region IFilterable

        private bool matchingFilter;

        public bool MatchingFilter
        {
            get => matchingFilter;
            set
            {
                matchingFilter = value;
                this.FadeTo(!matchingFilter ? 0 : 1);
            }
        }

        public bool FilteringActive { get; set; }

        public IEnumerable<LocalisableString> FilterTerms => KeyBindings.Select(b => (LocalisableString)keyCombinationProvider.GetReadableString(b.KeyCombination)).Prepend(text.Text);

        #endregion

        private readonly object action;

        private Bindable<bool> isDefault { get; } = new BindableBool(true);

        [Resolved]
        private ReadableKeyCombinationProvider keyCombinationProvider { get; set; } = null!;

        private Container content = null!;

        private OsuSpriteText text = null!;
        private FillFlowContainer cancelAndClearButtons = null!;
        private FillFlowContainer<KeyButton> buttons = null!;

        private KeyButton? bindTarget;

        private const float transition_time = 150;
        private const float height = 20;
        private const float padding = 5;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) =>
            content.ReceivePositionalInputAt(screenSpacePos);

        public override bool AcceptsFocus => bindTarget == null;

        /// <summary>
        /// Creates a new <see cref="KeyBindingRow"/>.
        /// </summary>
        /// <param name="action">The action that this row contains bindings for.</param>
        public KeyBindingRow(object action)
        {
            this.action = action;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Padding = new MarginPadding { Right = SettingsPanel.CONTENT_MARGINS };

            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Y,
                    Width = SettingsPanel.CONTENT_MARGINS,
                    Child = new RevertToDefaultButton<bool>
                    {
                        Current = isDefault,
                        Action = RestoreDefaults,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    }
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = SettingsPanel.CONTENT_MARGINS },
                    Children = new Drawable[]
                    {
                        content = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Masking = true,
                            CornerRadius = padding,
                            EdgeEffect = new EdgeEffectParameters
                            {
                                Radius = 2,
                                Colour = colourProvider.Highlight1.Opacity(0),
                                Type = EdgeEffectType.Shadow,
                                Hollow = true,
                            },
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = colourProvider.Background5,
                                },
                                text = new OsuSpriteText
                                {
                                    Text = action.GetLocalisableDescription(),
                                    Margin = new MarginPadding(1.5f * padding),
                                },
                                buttons = new FillFlowContainer<KeyButton>
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight
                                },
                                cancelAndClearButtons = new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Padding = new MarginPadding(padding) { Top = height + padding * 2 },
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Alpha = 0,
                                    Spacing = new Vector2(5),
                                    Children = new Drawable[]
                                    {
                                        new CancelButton { Action = () => finalise(false) },
                                        new ClearButton { Action = clear },
                                    },
                                },
                                new HoverClickSounds()
                            }
                        }
                    }
                }
            };

            KeyBindings.BindCollectionChanged((_, _) =>
            {
                Scheduler.AddOnce(updateButtons);
                updateIsDefaultValue();
            }, true);
        }

        private void updateButtons()
        {
            if (buttons.Count > KeyBindings.Count)
                buttons.RemoveRange(buttons.Skip(KeyBindings.Count).ToArray(), true);

            while (buttons.Count < KeyBindings.Count)
                buttons.Add(new KeyButton());

            foreach (var (button, binding) in buttons.Zip(KeyBindings))
                button.KeyBinding.Value = binding;
        }

        public void RestoreDefaults()
        {
            int i = 0;

            foreach (var d in Defaults)
            {
                var button = buttons[i++];
                button.UpdateKeyCombination(d);
                finalise();
            }

            isDefault.Value = true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.FadeEdgeEffectTo(1, transition_time, Easing.OutQuint);

            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.FadeEdgeEffectTo(0, transition_time, Easing.OutQuint);

            base.OnHoverLost(e);
        }

        private bool isModifier(Key k) => k < Key.F1;

        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (!HasFocus)
                return base.OnMouseDown(e);

            Debug.Assert(bindTarget != null);

            if (!bindTarget.IsHovered)
                return base.OnMouseDown(e);

            if (!AllowMainMouseButtons)
            {
                switch (e.Button)
                {
                    case MouseButton.Left:
                    case MouseButton.Right:
                        return true;
                }
            }

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromMouseButton(e.Button));
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            // don't do anything until the last button is released.
            if (!HasFocus || e.HasAnyButtonPressed)
            {
                base.OnMouseUp(e);
                return;
            }

            Debug.Assert(bindTarget != null);

            if (bindTarget.IsHovered)
                finalise(false);
            // prevent updating bind target before clear button's action
            else if (!cancelAndClearButtons.Any(b => b.IsHovered))
                updateBindTarget();
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            if (HasFocus)
            {
                Debug.Assert(bindTarget != null);

                if (bindTarget.IsHovered)
                {
                    bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState, e.ScrollDelta), KeyCombination.FromScrollDelta(e.ScrollDelta).First());
                    finalise();
                    return true;
                }
            }

            return base.OnScroll(e);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!HasFocus || e.Repeat)
                return false;

            Debug.Assert(bindTarget != null);

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromKey(e.Key));
            if (!isModifier(e.Key)) finalise();

            return true;
        }

        protected override void OnKeyUp(KeyUpEvent e)
        {
            if (!HasFocus)
            {
                base.OnKeyUp(e);
                return;
            }

            finalise();
        }

        protected override bool OnJoystickPress(JoystickPressEvent e)
        {
            if (!HasFocus)
                return false;

            Debug.Assert(bindTarget != null);

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromJoystickButton(e.Button));
            finalise();

            return true;
        }

        protected override void OnJoystickRelease(JoystickReleaseEvent e)
        {
            if (!HasFocus)
            {
                base.OnJoystickRelease(e);
                return;
            }

            finalise();
        }

        protected override bool OnMidiDown(MidiDownEvent e)
        {
            if (!HasFocus)
                return false;

            Debug.Assert(bindTarget != null);

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromMidiKey(e.Key));
            finalise();

            return true;
        }

        protected override void OnMidiUp(MidiUpEvent e)
        {
            if (!HasFocus)
            {
                base.OnMidiUp(e);
                return;
            }

            finalise();
        }

        protected override bool OnTabletAuxiliaryButtonPress(TabletAuxiliaryButtonPressEvent e)
        {
            if (!HasFocus)
                return false;

            Debug.Assert(bindTarget != null);

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromTabletAuxiliaryButton(e.Button));
            finalise();

            return true;
        }

        protected override void OnTabletAuxiliaryButtonRelease(TabletAuxiliaryButtonReleaseEvent e)
        {
            if (!HasFocus)
            {
                base.OnTabletAuxiliaryButtonRelease(e);
                return;
            }

            finalise();
        }

        protected override bool OnTabletPenButtonPress(TabletPenButtonPressEvent e)
        {
            if (!HasFocus)
                return false;

            Debug.Assert(bindTarget != null);

            bindTarget.UpdateKeyCombination(KeyCombination.FromInputState(e.CurrentState), KeyCombination.FromTabletPenButton(e.Button));
            finalise();

            return true;
        }

        protected override void OnTabletPenButtonRelease(TabletPenButtonReleaseEvent e)
        {
            if (!HasFocus)
            {
                base.OnTabletPenButtonRelease(e);
                return;
            }

            finalise();
        }

        private void clear()
        {
            if (bindTarget == null)
                return;

            bindTarget.UpdateKeyCombination(InputKey.None);
            finalise(false);
        }

        private void finalise(bool hasChanged = true)
        {
            if (bindTarget != null)
            {
                updateIsDefaultValue();

                bindTarget.IsBinding = false;
                var args = new KeyBindingUpdatedEventArgs(bindTarget.KeyBinding.Value.ID, bindTarget.KeyBinding.Value.KeyCombinationString);
                Schedule(() =>
                {
                    // schedule to ensure we don't instantly get focus back on next OnMouseClick (see AcceptFocus impl.)
                    bindTarget = null;
                    if (hasChanged)
                        BindingUpdated?.Invoke(this, args);
                });
            }

            if (HasFocus)
                GetContainingInputManager().ChangeFocus(null);

            cancelAndClearButtons.FadeOut(300, Easing.OutQuint);
            cancelAndClearButtons.BypassAutoSizeAxes |= Axes.Y;
        }

        protected override void OnFocus(FocusEvent e)
        {
            content.AutoSizeDuration = 500;
            content.AutoSizeEasing = Easing.OutQuint;

            cancelAndClearButtons.FadeIn(300, Easing.OutQuint);
            cancelAndClearButtons.BypassAutoSizeAxes &= ~Axes.Y;

            updateBindTarget();
            base.OnFocus(e);
        }

        protected override void OnFocusLost(FocusLostEvent e)
        {
            finalise(false);
            base.OnFocusLost(e);
        }

        /// <summary>
        /// Updates the bind target to the currently hovered key button or the first if clicked anywhere else.
        /// </summary>
        private void updateBindTarget()
        {
            if (bindTarget != null) bindTarget.IsBinding = false;
            bindTarget = buttons.FirstOrDefault(b => b.IsHovered) ?? buttons.FirstOrDefault();
            if (bindTarget != null) bindTarget.IsBinding = true;
        }

        private void updateIsDefaultValue()
        {
            isDefault.Value = KeyBindings.Select(b => b.KeyCombination).SequenceEqual(Defaults);
        }

        private partial class CancelButton : RoundedButton
        {
            public CancelButton()
            {
                Text = CommonStrings.ButtonsCancel;
                Size = new Vector2(80, 20);
            }
        }

        public partial class ClearButton : DangerousRoundedButton
        {
            public ClearButton()
            {
                Text = CommonStrings.ButtonsClear;
                Size = new Vector2(80, 20);
            }
        }

        public partial class KeyButton : Container
        {
            public Bindable<RealmKeyBinding> KeyBinding { get; } = new Bindable<RealmKeyBinding>();

            private readonly Box box;
            public readonly OsuSpriteText Text;

            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            [Resolved]
            private ReadableKeyCombinationProvider keyCombinationProvider { get; set; } = null!;

            private bool isBinding;

            public bool IsBinding
            {
                get => isBinding;
                set
                {
                    if (value == isBinding) return;

                    isBinding = value;

                    updateHoverState();
                }
            }

            public KeyButton()
            {
                Margin = new MarginPadding(padding);

                Masking = true;
                CornerRadius = padding;

                Height = height;
                AutoSizeAxes = Axes.X;

                Children = new Drawable[]
                {
                    new Container
                    {
                        AlwaysPresent = true,
                        Width = 80,
                        Height = height,
                    },
                    box = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    Text = new OsuSpriteText
                    {
                        Font = OsuFont.Numeric.With(size: 10),
                        Margin = new MarginPadding(5),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                    new HoverSounds()
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                KeyBinding.BindValueChanged(_ =>
                {
                    if (KeyBinding.Value.IsManaged)
                        throw new ArgumentException("Key binding should not be attached as we make temporary changes", nameof(KeyBinding));

                    updateKeyCombinationText();
                });
                keyCombinationProvider.KeymapChanged += updateKeyCombinationText;
                updateKeyCombinationText();
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                updateHoverState();
                FinishTransforms(true);
            }

            protected override bool OnHover(HoverEvent e)
            {
                updateHoverState();
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                updateHoverState();
                base.OnHoverLost(e);
            }

            private void updateHoverState()
            {
                if (isBinding)
                {
                    box.FadeColour(colourProvider.Light2, transition_time, Easing.OutQuint);
                    Text.FadeColour(Color4.Black, transition_time, Easing.OutQuint);
                }
                else
                {
                    box.FadeColour(IsHovered ? colourProvider.Light4 : colourProvider.Background6, transition_time, Easing.OutQuint);
                    Text.FadeColour(IsHovered ? Color4.Black : Color4.White, transition_time, Easing.OutQuint);
                }
            }

            /// <summary>
            /// Update from a key combination, only allowing a single non-modifier key to be specified.
            /// </summary>
            /// <param name="fullState">A <see cref="KeyCombination"/> generated from the full input state.</param>
            /// <param name="triggerKey">The key which triggered this update, and should be used as the binding.</param>
            public void UpdateKeyCombination(KeyCombination fullState, InputKey triggerKey) =>
                UpdateKeyCombination(new KeyCombination(fullState.Keys.Where(KeyCombination.IsModifierKey).Append(triggerKey)));

            public void UpdateKeyCombination(KeyCombination newCombination)
            {
                if (KeyBinding.Value.RulesetName != null && !RealmKeyBindingStore.CheckValidForGameplay(newCombination))
                    return;

                KeyBinding.Value.KeyCombination = newCombination;
                updateKeyCombinationText();
            }

            private void updateKeyCombinationText()
            {
                Scheduler.AddOnce(updateText);

                void updateText() => Text.Text = keyCombinationProvider.GetReadableString(KeyBinding.Value.KeyCombination);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                if (keyCombinationProvider.IsNotNull())
                    keyCombinationProvider.KeymapChanged -= updateKeyCombinationText;
            }
        }
    }
}
