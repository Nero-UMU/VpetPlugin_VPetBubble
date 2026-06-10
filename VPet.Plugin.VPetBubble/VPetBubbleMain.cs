using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LinePutScript;
using VPet_Simulator.Windows.Interface;

namespace VPet.Plugin.VPetBubble
{
    public class VPetBubbleMain : MainPlugin
    {
        private const string SettingLineName = "vpetbubble";
        private const double DefaultSpawnInterval = 5;
        private const double DefaultGameSpawnInterval = 0.5;
        private const int DefaultMinSize = 24;
        private const int DefaultMaxSize = 64;
        private const double DefaultMinSpeed = 2;
        private const double DefaultMaxSpeed = 5;
        private const int DefaultGameDuration = 60;
        private const double DefaultRingInterval = 12;
        private const double DefaultRingIntervalRandom = 0;
        private const double DefaultGameRingProbability = 0.1;
        private const double DefaultLightInterval = 12;
        private const double DefaultLightIntervalRandom = 0;
        private const double DefaultGameLightProbability = 0.1;
        private const double DefaultGamePetMoveProbability = 0.25;
        private const double GamePetMoveCheckInterval = 2;
        private const double RingClockwiseProbability = 0.5;

        private readonly Random random = new();
        private readonly List<BubbleInfo> bubbles = new();

        private DispatcherTimer spawnTimer;
        private DispatcherTimer ringTimer;
        private DispatcherTimer ringSequenceTimer;
        private DispatcherTimer lightTimer;
        private DispatcherTimer petMoveTimer;
        private DispatcherTimer moveTimer;
        private DispatcherTimer saveTimer;
        private Window bubbleWindow;
        private Canvas bubbleCanvas;
        private BubbleDailySettingWindow settingWindow;
        private BubbleGameScoreWindow scoreWindow;
        private PetMovementController petMovementController;
        private int ringDirectionIndex;
        private int ringDirectionStep;
        private int ringDirectionsRemaining;

        public override string PluginName => "Pet Bubble";

        public VPetBubbleMain(IMainWindow mainwin) : base(mainwin) { }

        public override void LoadPlugin()
        {
            MW.Dispatcher.Invoke(() =>
            {
                AddToolbarMenu();
                CreateBubbleWindow();

                spawnTimer = new DispatcherTimer();
                spawnTimer.Tick += SpawnTimer_Tick;

                ringTimer = new DispatcherTimer();
                ringTimer.Tick += RingTimer_Tick;

                ringSequenceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(85) };
                ringSequenceTimer.Tick += RingSequenceTimer_Tick;

                lightTimer = new DispatcherTimer();
                lightTimer.Tick += LightTimer_Tick;

                petMovementController = new PetMovementController(MW);
                petMoveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(GamePetMoveCheckInterval) };
                petMoveTimer.Tick += PetMoveTimer_Tick;

                moveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                moveTimer.Tick += MoveTimer_Tick;
                moveTimer.Start();

                saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                saveTimer.Tick += SaveTimer_Tick;

                ApplyRunState();
            });
        }

        public override void EndGame()
        {
            MW.Dispatcher.Invoke(() =>
            {
                spawnTimer?.Stop();
                ringTimer?.Stop();
                ringSequenceTimer?.Stop();
                lightTimer?.Stop();
                petMoveTimer?.Stop();
                moveTimer?.Stop();
                saveTimer?.Stop();
                settingWindow?.Close();
                scoreWindow?.CloseSilently();
                bubbleWindow?.Close();
                bubbles.Clear();
            });
        }

        public override void Setting()
        {
            MW.Dispatcher.Invoke(OpenSettingWindow);
        }

        private void AddToolbarMenu()
        {
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.Interact, "戳泡泡设置", () =>
            {
                MW.Main.ToolBar.Visibility = Visibility.Collapsed;
                OpenSettingWindow();
            });
            MW.Main.ToolBar.AddMenuButton(VPet_Simulator.Core.ToolBar.MenuType.Interact, "戳泡泡挑战", () =>
            {
                MW.Main.ToolBar.Visibility = Visibility.Collapsed;
                EnterGameMode();
            });
        }

        private void OpenSettingWindow()
        {
            if (settingWindow is { IsVisible: true })
            {
                settingWindow.Activate();
                return;
            }

            settingWindow = new BubbleDailySettingWindow(this);
            settingWindow.Closed += (_, _) => settingWindow = null;
            settingWindow.Show();
        }

        private void ApplyRunState()
        {
            if (spawnTimer == null)
            {
                return;
            }

            spawnTimer.Interval = TimeSpan.FromSeconds(SpawnInterval);

            if (IsGameMode)
            {
                spawnTimer.Stop();
                StopRingGeneration();
                StopLightGeneration();
                if (scoreWindow == null)
                {
                    StartGameMode(false);
                }
                return;
            }

            StopGameMode(true);
            if (Enabled)
            {
                spawnTimer.Start();
                StartRingGeneration();
                StartLightGeneration();
            }
            else
            {
                spawnTimer.Stop();
                StopRingGeneration();
                StopLightGeneration();
                ClearBubbles();
            }
        }

        private void EnterGameMode()
        {
            IsGameMode = true;
            if (spawnTimer != null)
            {
                spawnTimer.Interval = TimeSpan.FromSeconds(SpawnInterval);
                spawnTimer.Stop();
            }
            StopRingGeneration();
            StopLightGeneration();
            ClearBubbles();
            StartGameMode(true);
            QueueSaveSettings();
        }

        private void StartGameMode(bool restart)
        {
            if (scoreWindow != null && !restart)
            {
                scoreWindow.Activate();
                return;
            }

            ClearBubbles();
            scoreWindow?.CloseSilently();
            scoreWindow = new BubbleGameScoreWindow(
                GameDuration,
                SetGameDurationFromScoreboard,
                GetGameSettings,
                UpdateGameSettings,
                ResetGameSettingsFromScoreboard,
                FinishGameMode,
                () =>
                {
                    spawnTimer?.Stop();
                    StopRingGeneration();
                    StopLightGeneration();
                    StopPetMovement();
                },
                () =>
                {
                    if (IsGameMode)
                    {
                        spawnTimer.Interval = TimeSpan.FromSeconds(SpawnInterval);
                        spawnTimer.Start();
                        StartRingGeneration();
                        StartLightGeneration();
                        StartPetMovement();
                    }
                },
                StopGameAndReturnDaily,
                ResetGameRound);
            scoreWindow.Show();
            BringBubbleWindowToFront();
        }

        private void FinishGameMode()
        {
            spawnTimer?.Stop();
            StopRingGeneration();
            StopLightGeneration();
            StopPetMovement();
            ClearBubbles();
            IsGameMode = false;
            if (Enabled)
            {
                spawnTimer.Interval = TimeSpan.FromSeconds(SpawnInterval);
                spawnTimer.Start();
                StartRingGeneration();
                StartLightGeneration();
            }
            settingWindow?.RefreshFromPlugin();
            QueueSaveSettings();
        }

        private void StopGameAndReturnDaily()
        {
            spawnTimer?.Stop();
            StopRingGeneration();
            StopLightGeneration();
            StopPetMovement();
            ClearBubbles();
            IsGameMode = false;
            scoreWindow = null;
            if (Enabled)
            {
                spawnTimer.Interval = TimeSpan.FromSeconds(SpawnInterval);
                spawnTimer.Start();
                StartRingGeneration();
                StartLightGeneration();
            }
            settingWindow?.RefreshFromPlugin();
            QueueSaveSettings();
        }

        private void ResetGameRound()
        {
            IsGameMode = true;
            spawnTimer?.Stop();
            StopRingGeneration();
            StopLightGeneration();
            StopPetMovement();
            ClearBubbles();
            QueueSaveSettings();
        }

        private void StopGameMode(bool closeWindow)
        {
            if (scoreWindow == null)
            {
                return;
            }

            scoreWindow.StopCountdown();
            StopRingGeneration();
            StopLightGeneration();
            StopPetMovement();
            if (closeWindow)
            {
                scoreWindow.CloseSilently();
                scoreWindow = null;
            }
        }

        private void SpawnTimer_Tick(object sender, EventArgs e)
        {
            CreateBubble();
        }

        private void RingTimer_Tick(object sender, EventArgs e)
        {
            if (IsGameMode)
            {
                if (scoreWindow is not { IsRunning: true } || GameRingProbability <= 0)
                {
                    return;
                }

                if (random.NextDouble() <= GameRingProbability)
                {
                    StartRingBurst();
                }
                return;
            }

            if (!Enabled || !DailyRingEnabled)
            {
                return;
            }

            StartRingBurst();
            ResetRingTimerInterval();
        }

        private void RingSequenceTimer_Tick(object sender, EventArgs e)
        {
            if (ringDirectionsRemaining <= 0)
            {
                ringSequenceTimer.Stop();
                return;
            }

            var angle = ringDirectionIndex * Math.PI * 2 / 16;
            CreateRingBubble(angle);
            ringDirectionIndex = (ringDirectionIndex + ringDirectionStep + 16) % 16;
            ringDirectionsRemaining--;
        }

        private void StartRingBurst()
        {
            if (ringSequenceTimer.IsEnabled)
            {
                return;
            }

            ringDirectionIndex = random.Next(16);
            ringDirectionStep = random.NextDouble() < RingClockwiseProbability ? 1 : -1;
            ringDirectionsRemaining = 16;
            RingSequenceTimer_Tick(null, EventArgs.Empty);
            ringSequenceTimer.Start();
        }

        private void StartRingGeneration()
        {
            if (ringTimer == null)
            {
                return;
            }

            if (IsGameMode)
            {
                if (GameRingProbability <= 0)
                {
                    StopRingGeneration();
                    return;
                }

                ringTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, GameSpawnInterval));
                ringTimer.Start();
                return;
            }

            if (Enabled && DailyRingEnabled)
            {
                ResetRingTimerInterval();
                ringTimer.Start();
            }
            else
            {
                StopRingGeneration();
            }
        }

        private void StopRingGeneration()
        {
            ringTimer?.Stop();
            ringSequenceTimer?.Stop();
            ringDirectionsRemaining = 0;
        }

        private void ResetRingTimerInterval()
        {
            if (ringTimer == null)
            {
                return;
            }

            ringTimer.Interval = TimeSpan.FromSeconds(DailyRingInterval + random.NextDouble() * DailyRingIntervalRandom);
        }

        private void LightTimer_Tick(object sender, EventArgs e)
        {
            if (IsGameMode)
            {
                if (scoreWindow is not { IsRunning: true } || GameLightProbability <= 0)
                {
                    return;
                }

                if (random.NextDouble() <= GameLightProbability)
                {
                    CreateLightBurst();
                }
                return;
            }

            if (!Enabled || !DailyLightEnabled)
            {
                return;
            }

            CreateLightBurst();
            ResetLightTimerInterval();
        }

        private void StartLightGeneration()
        {
            if (lightTimer == null)
            {
                return;
            }

            if (IsGameMode)
            {
                if (GameLightProbability <= 0)
                {
                    StopLightGeneration();
                    return;
                }

                lightTimer.Interval = TimeSpan.FromSeconds(Math.Max(0.1, GameSpawnInterval));
                lightTimer.Start();
                return;
            }

            if (Enabled && DailyLightEnabled)
            {
                ResetLightTimerInterval();
                lightTimer.Start();
            }
            else
            {
                StopLightGeneration();
            }
        }

        private void StopLightGeneration()
        {
            lightTimer?.Stop();
        }

        private void ResetLightTimerInterval()
        {
            if (lightTimer == null)
            {
                return;
            }

            lightTimer.Interval = TimeSpan.FromSeconds(DailyLightInterval + random.NextDouble() * DailyLightIntervalRandom);
        }

        private void CreateLightBurst()
        {
            var centerDirection = random.Next(4) * Math.PI / 2;
            for (var i = -2; i <= 2; i++)
            {
                CreateRingBubble(centerDirection + i * Math.PI / 4, centerDirection);
            }
        }

        private void PetMoveTimer_Tick(object sender, EventArgs e)
        {
            if (!IsGameMode || scoreWindow is not { IsRunning: true })
            {
                return;
            }

            if (!petMovementController.IsMoving && random.NextDouble() <= GamePetMoveProbability)
            {
                petMovementController.StartRandomHorizontalMove(GamePetMoveProbability);
            }
        }

        private void StartPetMovement()
        {
            if (petMoveTimer == null || GamePetMoveProbability <= 0)
            {
                StopPetMovement();
                return;
            }

            petMoveTimer.Start();
        }

        private void StopPetMovement()
        {
            petMoveTimer?.Stop();
            petMovementController?.Stop();
        }

        private void CreateBubbleWindow()
        {
            bubbleCanvas = new Canvas();

            bubbleWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                Width = SystemParameters.VirtualScreenWidth,
                Height = SystemParameters.VirtualScreenHeight,
                Content = bubbleCanvas
            };

            bubbleWindow.SourceInitialized += (_, _) => HideFromAltTab(bubbleWindow);
            bubbleWindow.Show();
            BringBubbleWindowToFront();
        }

        private void BringBubbleWindowToFront()
        {
            if (bubbleWindow == null)
            {
                return;
            }

            bubbleWindow.Topmost = false;
            bubbleWindow.Topmost = true;
        }

        private void CreateBubble()
        {
            BringBubbleWindowToFront();
            CreateBubbleAt(GetPetCenter(), random.NextDouble() * Math.PI * 2);
        }

        private void CreateRingBubble(double angle)
        {
            CreateRingBubble(angle, angle);
        }

        private void CreateRingBubble(double spawnAngle, double moveAngle)
        {
            var size = random.Next(MinSize, MaxSize + 1);
            var center = GetPetCenter();
            var radius = GetPetRadius() + size / 2.0;
            var start = new Point(
                center.X + Math.Cos(spawnAngle) * radius,
                center.Y + Math.Sin(spawnAngle) * radius);
            CreateBubbleAt(start, moveAngle, size);
        }

        private void CreateBubbleAt(Point center, double angle, int? fixedSize = null)
        {
            var size = fixedSize ?? random.Next(MinSize, MaxSize + 1);
            var bubble = CreateBubbleElement(size);

            Canvas.SetLeft(bubble, center.X - size / 2);
            Canvas.SetTop(bubble, center.Y - size / 2);
            bubbleCanvas.Children.Add(bubble);
            AddMovingBubble(bubble, angle);
        }

        private FrameworkElement CreateBubbleElement(int size)
        {
            var bubble = new Grid
            {
                Width = size,
                Height = size,
                Opacity = random.NextDouble() * 0.25 + 0.65,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1)
            };

            bubble.Children.Add(new Ellipse
            {
                Fill = CreateRandomBubbleBrush(),
                Stroke = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                StrokeThickness = Math.Max(1.5, size / 22.0)
            });

            bubble.Children.Add(new Ellipse
            {
                Width = size * (random.NextDouble() * 0.12 + 0.22),
                Height = size * (random.NextDouble() * 0.08 + 0.12),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(
                    size * (random.NextDouble() * 0.15 + 0.16),
                    size * (random.NextDouble() * 0.13 + 0.12),
                    0,
                    0),
                Fill = new SolidColorBrush(Color.FromArgb((byte)random.Next(145, 221), 255, 255, 255))
            });

            return bubble;
        }

        private Brush CreateRandomBubbleBrush()
        {
            var colors = new[]
            {
                Color.FromRgb(135, 206, 250),
                Color.FromRgb(152, 251, 152),
                Color.FromRgb(255, 182, 193),
                Color.FromRgb(221, 160, 221),
                Color.FromRgb(255, 240, 145)
            };

            var color = colors[random.Next(colors.Length)];
            var brush = new RadialGradientBrush
            {
                Center = new Point(0.42, 0.42),
                GradientOrigin = new Point(0.28, 0.24),
                RadiusX = 0.75,
                RadiusY = 0.75
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, color.R, color.G, color.B), 0.52));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(185, color.R, color.G, color.B), 1));

            return brush;
        }

        private void AddMovingBubble(FrameworkElement bubble, double angle)
        {
            var speed = random.NextDouble() * (MaxSpeed - MinSpeed) + MinSpeed;
            bubbles.Add(new BubbleInfo(bubble, Math.Cos(angle) * speed, Math.Sin(angle) * speed));
        }

        private void MoveTimer_Tick(object sender, EventArgs e)
        {
            if (IsGameMode && scoreWindow is not { IsRunning: true })
            {
                return;
            }

            var mouse = GetMousePointInBubbleWindow();
            for (var i = bubbles.Count - 1; i >= 0; i--)
            {
                var info = bubbles[i];
                var left = Canvas.GetLeft(info.Bubble) + info.SpeedX;
                var top = Canvas.GetTop(info.Bubble) + info.SpeedY;

                Canvas.SetLeft(info.Bubble, left);
                Canvas.SetTop(info.Bubble, top);

                if (IsMouseTouchingBubble(info.Bubble, left, top, mouse))
                {
                    bubbles.RemoveAt(i);
                    if (IsGameMode && scoreWindow is { IsRunning: true })
                    {
                        scoreWindow.AddScore();
                    }
                    PopBubble(info.Bubble);
                }
                else if (IsOutsideScreen(info.Bubble, left, top))
                {
                    bubbleCanvas.Children.Remove(info.Bubble);
                    bubbles.RemoveAt(i);
                }
            }
        }

        private void PopBubble(FrameworkElement bubble)
        {
            var storyboard = new Storyboard();
            var duration = TimeSpan.FromMilliseconds(220);

            var fade = new DoubleAnimation(bubble.Opacity, 0, duration)
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, bubble);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(fade);

            if (bubble.RenderTransform is ScaleTransform scale)
            {
                var scaleX = new DoubleAnimation(scale.ScaleX, 1.55, duration)
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                };
                Storyboard.SetTarget(scaleX, scale);
                Storyboard.SetTargetProperty(scaleX, new PropertyPath(ScaleTransform.ScaleXProperty));
                storyboard.Children.Add(scaleX);

                var scaleY = new DoubleAnimation(scale.ScaleY, 1.55, duration)
                {
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }
                };
                Storyboard.SetTarget(scaleY, scale);
                Storyboard.SetTargetProperty(scaleY, new PropertyPath(ScaleTransform.ScaleYProperty));
                storyboard.Children.Add(scaleY);
            }

            storyboard.Completed += (_, _) => bubbleCanvas.Children.Remove(bubble);
            storyboard.Begin();
        }

        private Point? GetMousePointInBubbleWindow()
        {
            if (!GetCursorPos(out var cursor))
            {
                return null;
            }

            return bubbleWindow.PointFromScreen(new Point(cursor.X, cursor.Y));
        }

        private static bool IsMouseTouchingBubble(FrameworkElement bubble, double left, double top, Point? mouse)
        {
            if (mouse == null)
            {
                return false;
            }

            var centerX = left + bubble.Width / 2;
            var centerY = top + bubble.Height / 2;
            var radius = bubble.Width / 2;
            var distance = Math.Sqrt(Math.Pow(mouse.Value.X - centerX, 2) + Math.Pow(mouse.Value.Y - centerY, 2));

            return distance <= radius;
        }

        private static bool IsOutsideScreen(FrameworkElement bubble, double left, double top)
        {
            return left + bubble.Width < 0
                || top + bubble.Height < 0
                || left > SystemParameters.VirtualScreenWidth
                || top > SystemParameters.VirtualScreenHeight;
        }

        private Point GetPetCenter()
        {
            var left = MW.Core.Controller.GetWindowsDistanceLeft() - SystemParameters.VirtualScreenLeft;
            var top = MW.Core.Controller.GetWindowsDistanceUp() - SystemParameters.VirtualScreenTop;
            var width = MW.Main.MainGrid.ActualWidth > 1 ? MW.Main.MainGrid.ActualWidth : 500;
            var height = MW.Main.MainGrid.ActualHeight > 1 ? MW.Main.MainGrid.ActualHeight : 500;
            var zoom = Math.Max(0.1, MW.Core.Controller.ZoomRatio);

            return new Point(left + width * zoom / 2, top + height * zoom / 2);
        }

        private double GetPetRadius()
        {
            var width = MW.Main.MainGrid.ActualWidth > 1 ? MW.Main.MainGrid.ActualWidth : 500;
            var height = MW.Main.MainGrid.ActualHeight > 1 ? MW.Main.MainGrid.ActualHeight : 500;
            var zoom = Math.Max(0.1, MW.Core.Controller.ZoomRatio);

            return Math.Max(width, height) * zoom / 2;
        }

        private void ApplySettings()
        {
            ApplyRunState();
            QueueSaveSettings();
        }

        private void QueueSaveSettings()
        {
            if (saveTimer == null)
            {
                return;
            }

            saveTimer.Stop();
            saveTimer.Start();
        }

        private void SaveTimer_Tick(object sender, EventArgs e)
        {
            saveTimer.Stop();
            MW.Save();
        }

        private void ResetSettings()
        {
            Enabled = true;
            ResetDailySettings(false);
            ResetGameSettings(false);
            IsGameMode = false;
            ApplySettings();
        }

        private void ResetDailySettings(bool apply = true)
        {
            ResetDailyBasicSettings(false);
            ResetDailyRingSettings(false);
            ResetDailyLightSettings(false);
            if (apply)
            {
                ApplySettings();
            }
        }

        private void ResetDailyBasicSettings(bool apply = true)
        {
            Enabled = true;
            DailySpawnInterval = DefaultSpawnInterval;
            DailyMinSize = DefaultMinSize;
            DailyMaxSize = DefaultMaxSize;
            DailyMinSpeed = DefaultMinSpeed;
            DailyMaxSpeed = DefaultMaxSpeed;
            if (apply)
            {
                ApplySettings();
            }
        }

        private void ResetDailyRingSettings(bool apply = true)
        {
            DailyRingEnabled = true;
            DailyRingInterval = DefaultRingInterval;
            DailyRingIntervalRandom = DefaultRingIntervalRandom;
            if (apply)
            {
                ApplySettings();
            }
        }

        private void ResetDailyLightSettings(bool apply = true)
        {
            DailyLightEnabled = true;
            DailyLightInterval = DefaultLightInterval;
            DailyLightIntervalRandom = DefaultLightIntervalRandom;
            if (apply)
            {
                ApplySettings();
            }
        }

        private void ResetGameSettings(bool apply = true)
        {
            GameSpawnInterval = DefaultGameSpawnInterval;
            GameMinSize = DefaultMinSize;
            GameMaxSize = DefaultMaxSize;
            GameMinSpeed = DefaultMinSpeed;
            GameMaxSpeed = DefaultMaxSpeed;
            GameDuration = DefaultGameDuration;
            GameRingProbability = DefaultGameRingProbability;
            GameLightProbability = DefaultGameLightProbability;
            GamePetMoveProbability = DefaultGamePetMoveProbability;
            if (apply)
            {
                ApplySettings();
            }
        }

        private BubbleGameSettings GetGameSettings()
        {
            return new BubbleGameSettings
            {
                DurationSeconds = GameDuration,
                SpawnInterval = GameSpawnInterval,
                MinSize = GameMinSize,
                MaxSize = GameMaxSize,
                MinSpeed = GameMinSpeed,
                MaxSpeed = GameMaxSpeed,
                RingProbability = GameRingProbability,
                LightProbability = GameLightProbability,
                PetMoveProbability = GamePetMoveProbability
            };
        }

        private void UpdateGameSettings(BubbleGameSettings settings)
        {
            GameDuration = settings.DurationSeconds;
            GameSpawnInterval = settings.SpawnInterval;
            GameMinSize = settings.MinSize;
            GameMaxSize = settings.MaxSize;
            GameMinSpeed = settings.MinSpeed;
            GameMaxSpeed = settings.MaxSpeed;
            GameRingProbability = settings.RingProbability;
            GameLightProbability = settings.LightProbability;
            GamePetMoveProbability = settings.PetMoveProbability;
            ApplySettings();
        }

        private void SetGameDurationFromScoreboard(int seconds)
        {
            GameDuration = seconds;
            ApplySettings();
        }

        private void ResetGameSettingsFromScoreboard()
        {
            ResetGameSettings();
        }

        private void ClearBubbles()
        {
            foreach (var info in bubbles)
            {
                bubbleCanvas.Children.Remove(info.Bubble);
            }
            bubbles.Clear();

        }

        private ILine Config => MW.Set[SettingLineName];

        private bool Enabled
        {
            get => !Config.GetBool("disabled");
            set => Config.SetBool("disabled", !value);
        }

        private double SpawnInterval => IsGameMode ? GameSpawnInterval : DailySpawnInterval;
        private int MinSize => IsGameMode ? GameMinSize : DailyMinSize;
        private int MaxSize => IsGameMode ? GameMaxSize : DailyMaxSize;
        private double MinSpeed => IsGameMode ? GameMinSpeed : DailyMinSpeed;
        private double MaxSpeed => IsGameMode ? GameMaxSpeed : DailyMaxSpeed;

        private double DailySpawnInterval
        {
            get => Clamp(Config.GetDouble("daily_spawninterval", DefaultSpawnInterval), 0.1, 60);
            set => Config.SetDouble("daily_spawninterval", Clamp(value, 0.1, 60));
        }

        private int DailyMinSize
        {
            get => (int)Clamp(Config.GetInt("daily_minsize", DefaultMinSize), 10, 160);
            set => Config.SetInt("daily_minsize", (int)Clamp(value, 10, DailyMaxSize));
        }

        private int DailyMaxSize
        {
            get => (int)Clamp(Config.GetInt("daily_maxsize", DefaultMaxSize), DailyMinSize, 220);
            set => Config.SetInt("daily_maxsize", (int)Clamp(value, DailyMinSize, 220));
        }

        private double DailyMinSpeed
        {
            get => Clamp(Config.GetDouble("daily_minspeed", DefaultMinSpeed), 0.2, 20);
            set => Config.SetDouble("daily_minspeed", Clamp(value, 0.2, DailyMaxSpeed));
        }

        private double DailyMaxSpeed
        {
            get => Clamp(Config.GetDouble("daily_maxspeed", DefaultMaxSpeed), DailyMinSpeed, 30);
            set => Config.SetDouble("daily_maxspeed", Clamp(value, DailyMinSpeed, 30));
        }

        private bool DailyRingEnabled
        {
            get => !Config.GetBool("daily_ring_disabled");
            set => Config.SetBool("daily_ring_disabled", !value);
        }

        private double DailyRingInterval
        {
            get => Clamp(Config.GetDouble("daily_ring_interval", DefaultRingInterval), 0.5, 600);
            set => Config.SetDouble("daily_ring_interval", Clamp(value, 0.5, 600));
        }

        private double DailyRingIntervalRandom
        {
            get => Clamp(Config.GetDouble("daily_ring_random", DefaultRingIntervalRandom), 0, 120);
            set => Config.SetDouble("daily_ring_random", Clamp(value, 0, 120));
        }

        private bool DailyLightEnabled
        {
            get => !Config.GetBool("daily_light_disabled");
            set => Config.SetBool("daily_light_disabled", !value);
        }

        private double DailyLightInterval
        {
            get => Clamp(Config.GetDouble("daily_light_interval", DefaultLightInterval), 0.5, 600);
            set => Config.SetDouble("daily_light_interval", Clamp(value, 0.5, 600));
        }

        private double DailyLightIntervalRandom
        {
            get => Clamp(Config.GetDouble("daily_light_random", DefaultLightIntervalRandom), 0, 120);
            set => Config.SetDouble("daily_light_random", Clamp(value, 0, 120));
        }

        private double GameSpawnInterval
        {
            get => Clamp(Config.GetDouble("game_spawninterval", DefaultGameSpawnInterval), 0.1, 30);
            set => Config.SetDouble("game_spawninterval", Clamp(value, 0.1, 30));
        }

        private int GameMinSize
        {
            get => (int)Clamp(Config.GetInt("game_minsize", DefaultMinSize), 10, 160);
            set => Config.SetInt("game_minsize", (int)Clamp(value, 10, GameMaxSize));
        }

        private int GameMaxSize
        {
            get => (int)Clamp(Config.GetInt("game_maxsize", DefaultMaxSize), GameMinSize, 220);
            set => Config.SetInt("game_maxsize", (int)Clamp(value, GameMinSize, 220));
        }

        private double GameMinSpeed
        {
            get => Clamp(Config.GetDouble("game_minspeed", DefaultMinSpeed), 0.2, 20);
            set => Config.SetDouble("game_minspeed", Clamp(value, 0.2, GameMaxSpeed));
        }

        private double GameMaxSpeed
        {
            get => Clamp(Config.GetDouble("game_maxspeed", DefaultMaxSpeed), GameMinSpeed, 30);
            set => Config.SetDouble("game_maxspeed", Clamp(value, GameMinSpeed, 30));
        }

        private double GameRingProbability
        {
            get => Clamp(Config.GetDouble("game_ring_probability", DefaultGameRingProbability), 0, 1);
            set => Config.SetDouble("game_ring_probability", Clamp(value, 0, 1));
        }

        private double GameLightProbability
        {
            get => Clamp(Config.GetDouble("game_light_probability", DefaultGameLightProbability), 0, 1);
            set => Config.SetDouble("game_light_probability", Clamp(value, 0, 1));
        }

        private double GamePetMoveProbability
        {
            get => Clamp(Config.GetDouble("game_pet_move_probability", DefaultGamePetMoveProbability), 0, 1);
            set => Config.SetDouble("game_pet_move_probability", Clamp(value, 0, 1));
        }

        private bool IsGameMode
        {
            get => Config.GetInt("mode", 0) == 1;
            set => Config.SetInt("mode", value ? 1 : 0);
        }

        private int GameDuration
        {
            get => (int)Clamp(Config.GetInt("gameduration", DefaultGameDuration), 1, 600);
            set => Config.SetInt("gameduration", (int)Clamp(value, 1, 600));
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        private static void HideFromAltTab(Window window)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var style = GetWindowLong(handle, GwlExStyle);
            SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
        }

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out CursorPoint lpPoint);

        private struct CursorPoint
        {
            public int X;
            public int Y;
        }

        private class BubbleInfo
        {
            public FrameworkElement Bubble { get; }
            public double SpeedX { get; }
            public double SpeedY { get; }

            public BubbleInfo(FrameworkElement bubble, double speedX, double speedY)
            {
                Bubble = bubble;
                SpeedX = speedX;
                SpeedY = speedY;
            }
        }

        private abstract class BubbleSettingsWindowBase : Window
        {
            protected void AddSectionTitle(Panel panel, string text)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 8, 0, 12),
                    Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175))
                });
            }

            protected Button AddButton(Panel panel, string text, Action clickAction, double rightMargin = 0)
            {
                var button = new Button
                {
                    Content = text,
                    MinWidth = 100,
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(0, 0, rightMargin, 0)
                };
                button.Click += (_, _) => clickAction();
                panel.Children.Add(button);
                return button;
            }

            protected Slider AddSlider(Panel panel, string title, double value, double min, double max, Action updateAction)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                panel.Children.Add(row);

                var label = new TextBlock
                {
                    Text = title,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetRow(label, 0);
                row.Children.Add(label);

                var valueText = new TextBlock
                {
                    Text = value.ToString("0.##"),
                    FontWeight = FontWeights.Bold,
                    TextAlignment = TextAlignment.Right
                };
                Grid.SetRow(valueText, 0);
                Grid.SetColumn(valueText, 1);
                row.Children.Add(valueText);

                var slider = new Slider
                {
                    Minimum = min,
                    Maximum = max,
                    Value = value,
                    TickFrequency = max > 40 ? 1 : 0.1,
                    IsSnapToTickEnabled = max > 40,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                slider.ValueChanged += (_, _) =>
                {
                    valueText.Text = slider.Value.ToString("0.##");
                    updateAction();
                };
                Grid.SetRow(slider, 1);
                Grid.SetColumnSpan(slider, 2);
                row.Children.Add(slider);

                return slider;
            }

            protected StackPanel[] CreateTabbedContent(string titleText, params string[] menuTexts)
            {
                Width = 680;
                Height = 560;
                MinWidth = 560;
                MinHeight = 420;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = Brushes.White;
                Foreground = Brushes.Black;
                FontSize = 16;

                var root = new Grid();
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                root.ColumnDefinitions.Add(new ColumnDefinition());
                Content = root;

                var menu = new ListBox
                {
                    Margin = new Thickness(0, 40, 5, 3),
                    BorderThickness = new Thickness(0)
                };
                foreach (var menuText in menuTexts)
                {
                    menu.Items.Add(menuText);
                }
                Grid.SetColumn(menu, 0);
                root.Children.Add(menu);

                var title = new TextBlock
                {
                    Text = titleText,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(12, 12, 8, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                root.Children.Add(title);

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(5)
                };
                Grid.SetColumn(scrollViewer, 1);
                root.Children.Add(scrollViewer);

                var border = new Border
                {
                    Padding = new Thickness(14),
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(218, 226, 235)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8)
                };
                scrollViewer.Content = border;

                var contentHost = new Grid();
                border.Child = contentHost;

                var panels = new StackPanel[menuTexts.Length];
                for (var i = 0; i < panels.Length; i++)
                {
                    panels[i] = new StackPanel
                    {
                        Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed
                    };
                    contentHost.Children.Add(panels[i]);
                }

                menu.SelectionChanged += (_, _) =>
                {
                    for (var i = 0; i < panels.Length; i++)
                    {
                        panels[i].Visibility = i == menu.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
                    }
                };
                menu.SelectedIndex = 0;
                return panels;
            }
        }

        private class BubbleDailySettingWindow : BubbleSettingsWindowBase
        {
            private readonly VPetBubbleMain plugin;
            private readonly CheckBox enabledBox;
            private readonly Slider dailySpawnSlider;
            private readonly Slider dailyMinSizeSlider;
            private readonly Slider dailyMaxSizeSlider;
            private readonly Slider dailyMinSpeedSlider;
            private readonly Slider dailyMaxSpeedSlider;
            private readonly CheckBox dailyRingEnabledBox;
            private readonly Slider dailyRingIntervalSlider;
            private readonly Slider dailyRingRandomSlider;
            private readonly CheckBox dailyLightEnabledBox;
            private readonly Slider dailyLightIntervalSlider;
            private readonly Slider dailyLightRandomSlider;
            private bool isRefreshing;

            public BubbleDailySettingWindow(VPetBubbleMain plugin)
            {
                this.plugin = plugin;
                Title = "戳泡泡设置";
                var panels = CreateTabbedContent("戳泡泡设置", "日常模式设置", "泡之财宝设置", "光泡设置");
                var dailyPanel = panels[0];
                var ringPanel = panels[1];
                var lightPanel = panels[2];

                enabledBox = new CheckBox
                {
                    Content = "启动日常泡泡",
                    IsChecked = plugin.Enabled,
                    Margin = new Thickness(0, 0, 0, 18)
                };
                enabledBox.Checked += (_, _) => UpdateSettings();
                enabledBox.Unchecked += (_, _) => UpdateSettings();
                dailyPanel.Children.Add(enabledBox);

                AddSectionTitle(dailyPanel, "日常模式设置");
                dailySpawnSlider = AddSlider(dailyPanel, "生成间隔（秒）", plugin.DailySpawnInterval, 0.1, 60, UpdateSettings);
                dailyMinSizeSlider = AddSlider(dailyPanel, "最小泡泡大小", plugin.DailyMinSize, 10, 160, UpdateSettings);
                dailyMaxSizeSlider = AddSlider(dailyPanel, "最大泡泡大小", plugin.DailyMaxSize, 10, 220, UpdateSettings);
                dailyMinSpeedSlider = AddSlider(dailyPanel, "最小速度", plugin.DailyMinSpeed, 0.2, 20, UpdateSettings);
                dailyMaxSpeedSlider = AddSlider(dailyPanel, "最大速度", plugin.DailyMaxSpeed, 0.2, 30, UpdateSettings);
                AddActionButtons(dailyPanel, "恢复日常默认", () =>
                {
                    plugin.ResetDailyBasicSettings();
                    RefreshFromPlugin();
                });

                AddSectionTitle(ringPanel, "泡之财宝设置");
                dailyRingEnabledBox = new CheckBox
                {
                    Content = "启动泡之财宝",
                    IsChecked = plugin.DailyRingEnabled,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                dailyRingEnabledBox.Checked += (_, _) => UpdateSettings();
                dailyRingEnabledBox.Unchecked += (_, _) => UpdateSettings();
                ringPanel.Children.Add(dailyRingEnabledBox);
                dailyRingIntervalSlider = AddSlider(ringPanel, "泡之财宝间隔（秒）", plugin.DailyRingInterval, 0.5, 600, UpdateSettings);
                dailyRingRandomSlider = AddSlider(ringPanel, "泡之财宝随机浮动（秒）", plugin.DailyRingIntervalRandom, 0, 120, UpdateSettings);
                AddActionButtons(ringPanel, "恢复泡之财宝默认", () =>
                {
                    plugin.ResetDailyRingSettings();
                    RefreshFromPlugin();
                });

                AddSectionTitle(lightPanel, "光泡设置");
                dailyLightEnabledBox = new CheckBox
                {
                    Content = "启动光泡",
                    IsChecked = plugin.DailyLightEnabled,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                dailyLightEnabledBox.Checked += (_, _) => UpdateSettings();
                dailyLightEnabledBox.Unchecked += (_, _) => UpdateSettings();
                lightPanel.Children.Add(dailyLightEnabledBox);
                dailyLightIntervalSlider = AddSlider(lightPanel, "光泡间隔（秒）", plugin.DailyLightInterval, 0.5, 600, UpdateSettings);
                dailyLightRandomSlider = AddSlider(lightPanel, "光泡随机浮动（秒）", plugin.DailyLightIntervalRandom, 0, 120, UpdateSettings);
                AddActionButtons(lightPanel, "恢复光泡默认", () =>
                {
                    plugin.ResetDailyLightSettings();
                    RefreshFromPlugin();
                });

            }

            private void AddActionButtons(Panel panel, string resetText, Action resetAction)
            {
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 18, 0, 0)
                };
                panel.Children.Add(buttonPanel);

                AddButton(buttonPanel, resetText, resetAction, 8);
                AddButton(buttonPanel, "关闭", Close);
            }

            private void UpdateSettings()
            {
                if (isRefreshing)
                {
                    return;
                }

                plugin.Enabled = enabledBox.IsChecked == true;
                plugin.DailySpawnInterval = dailySpawnSlider.Value;
                plugin.DailyMinSize = (int)Math.Round(Math.Min(dailyMinSizeSlider.Value, dailyMaxSizeSlider.Value));
                plugin.DailyMaxSize = (int)Math.Round(Math.Max(dailyMinSizeSlider.Value, dailyMaxSizeSlider.Value));
                plugin.DailyMinSpeed = Math.Min(dailyMinSpeedSlider.Value, dailyMaxSpeedSlider.Value);
                plugin.DailyMaxSpeed = Math.Max(dailyMinSpeedSlider.Value, dailyMaxSpeedSlider.Value);
                plugin.DailyRingEnabled = dailyRingEnabledBox.IsChecked == true;
                plugin.DailyRingInterval = dailyRingIntervalSlider.Value;
                plugin.DailyRingIntervalRandom = dailyRingRandomSlider.Value;
                plugin.DailyLightEnabled = dailyLightEnabledBox.IsChecked == true;
                plugin.DailyLightInterval = dailyLightIntervalSlider.Value;
                plugin.DailyLightIntervalRandom = dailyLightRandomSlider.Value;
                plugin.ApplySettings();
            }

            public void RefreshFromPlugin()
            {
                isRefreshing = true;
                try
                {
                    enabledBox.IsChecked = plugin.Enabled;
                    dailySpawnSlider.Value = plugin.DailySpawnInterval;
                    dailyMinSizeSlider.Value = plugin.DailyMinSize;
                    dailyMaxSizeSlider.Value = plugin.DailyMaxSize;
                    dailyMinSpeedSlider.Value = plugin.DailyMinSpeed;
                    dailyMaxSpeedSlider.Value = plugin.DailyMaxSpeed;
                    dailyRingEnabledBox.IsChecked = plugin.DailyRingEnabled;
                    dailyRingIntervalSlider.Value = plugin.DailyRingInterval;
                    dailyRingRandomSlider.Value = plugin.DailyRingIntervalRandom;
                    dailyLightEnabledBox.IsChecked = plugin.DailyLightEnabled;
                    dailyLightIntervalSlider.Value = plugin.DailyLightInterval;
                    dailyLightRandomSlider.Value = plugin.DailyLightIntervalRandom;
                }
                finally
                {
                    isRefreshing = false;
                }
            }
        }

    }
}

