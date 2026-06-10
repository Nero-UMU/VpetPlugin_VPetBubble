using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VPet.Plugin.VPetBubble
{
    public class BubbleGameSettings
    {
        public int DurationSeconds { get; set; }
        public double SpawnInterval { get; set; }
        public int MinSize { get; set; }
        public int MaxSize { get; set; }
        public double MinSpeed { get; set; }
        public double MaxSpeed { get; set; }
        public double RingProbability { get; set; }
        public double LightProbability { get; set; }
        public double PetMoveProbability { get; set; }
    }

    public class BubbleGameScoreWindow : Window
    {
        private const int MinDurationSeconds = 1;
        private const int MaxDurationSeconds = 600;

        private int durationSeconds;
        private readonly Action<int> durationChangedAction;
        private readonly Func<BubbleGameSettings> getSettingsAction;
        private readonly Action<BubbleGameSettings> updateSettingsAction;
        private readonly Action resetSettingsAction;
        private readonly Action finishedAction;
        private readonly Action pauseAction;
        private readonly Action resumeAction;
        private readonly Action stopAction;
        private readonly Action resetAction;
        private readonly DispatcherTimer countdownTimer;
        private readonly TextBlock titleText;
        private readonly Grid board;
        private readonly StackPanel commandArea;
        private TextBox timeBox;
        private readonly TextBlock scoreText;
        private readonly TextBlock statusText;
        private Button decreaseTimeButton;
        private Button increaseTimeButton;
        private readonly Button pauseButton;
        private readonly Button resumeButton;
        private readonly Button resetButton;
        private readonly Button settingsButton;
        private readonly Button stopButton;
        private readonly Border settingsPanel;
        private readonly Border resultPanel;
        private TextBlock resultScoreText;
        private Slider spawnSlider;
        private Slider minSizeSlider;
        private Slider maxSizeSlider;
        private Slider minSpeedSlider;
        private Slider maxSpeedSlider;
        private Slider ringProbabilitySlider;
        private Slider lightProbabilitySlider;
        private Slider petMoveProbabilitySlider;

        private int remainingSeconds;
        private int score;
        private bool hasFinished;
        private bool hasStarted;
        private bool isUpdatingSettings;

        public bool IsRunning { get; private set; }

        public BubbleGameScoreWindow(
            int durationSeconds,
            Action<int> durationChangedAction,
            Func<BubbleGameSettings> getSettingsAction,
            Action<BubbleGameSettings> updateSettingsAction,
            Action resetSettingsAction,
            Action finishedAction,
            Action pauseAction,
            Action resumeAction,
            Action stopAction,
            Action resetAction)
        {
            this.durationSeconds = ClampDuration(durationSeconds);
            this.durationChangedAction = durationChangedAction;
            this.getSettingsAction = getSettingsAction;
            this.updateSettingsAction = updateSettingsAction;
            this.resetSettingsAction = resetSettingsAction;
            this.finishedAction = finishedAction;
            this.pauseAction = pauseAction;
            this.resumeAction = resumeAction;
            this.stopAction = stopAction;
            this.resetAction = resetAction;
            remainingSeconds = this.durationSeconds;

            Title = "戳泡泡挑战计分板";
            Width = 620;
            Height = 390;
            MinWidth = 560;
            MinHeight = 360;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            Topmost = false;
            Background = new SolidColorBrush(Color.FromRgb(17, 24, 39));
            Foreground = Brushes.White;
            FontSize = 16;

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            titleText = new TextBlock
            {
                Text = "戳泡泡挑战",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            root.Children.Add(titleText);

            board = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(8, 13, 25))
            };
            board.ColumnDefinitions.Add(new ColumnDefinition());
            board.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetRow(board, 1);
            root.Children.Add(board);

            var timePanel = CreateScorePanel("时间");
            timePanel.Children.Add(CreateTimeEditor());
            board.Children.Add(timePanel);

            var scorePanel = CreateScorePanel("得分");
            scoreText = CreateBigNumberText();
            scorePanel.Children.Add(scoreText);
            Grid.SetColumn(scorePanel, 1);
            board.Children.Add(scorePanel);

            commandArea = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(commandArea, 2);
            root.Children.Add(commandArea);

            statusText = new TextBlock
            {
                Text = "准备中",
                Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71)),
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            commandArea.Children.Add(statusText);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            commandArea.Children.Add(buttonPanel);

            pauseButton = CreateButton("暂停");
            pauseButton.Click += (_, _) => PauseGame();
            buttonPanel.Children.Add(pauseButton);

            resumeButton = CreateButton("开始");
            resumeButton.Click += (_, _) => ResumeGame();
            buttonPanel.Children.Add(resumeButton);

            resetButton = CreateButton("重置");
            resetButton.Click += (_, _) => ResetRound();
            buttonPanel.Children.Add(resetButton);

            settingsButton = CreateButton("设置");
            settingsButton.Click += (_, _) => ToggleSettingsPanel();
            buttonPanel.Children.Add(settingsButton);

            stopButton = CreateButton("停止游戏");
            stopButton.Click += (_, _) => StopGame();
            buttonPanel.Children.Add(stopButton);

            settingsPanel = CreateSettingsPanel();
            settingsPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(settingsPanel, 3);
            root.Children.Add(settingsPanel);

            resultPanel = CreateResultPanel();
            resultPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(resultPanel, 1);
            Grid.SetRowSpan(resultPanel, 2);
            root.Children.Add(resultPanel);

            countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            countdownTimer.Tick += CountdownTimer_Tick;

            RefreshSettingsPanel();
            UpdateDisplay();
            UpdateButtonState();
            Closed += (_, _) =>
            {
                if (!hasFinished)
                {
                    StopGame();
                }
            };
        }

        public void AddScore()
        {
            if (!IsRunning)
            {
                return;
            }

            score++;
            UpdateDisplay();
        }

        public void StopCountdown()
        {
            IsRunning = false;
            settingsPanel.Visibility = Visibility.Collapsed;
            countdownTimer.Stop();
            UpdateButtonState();
        }

        public void CloseSilently()
        {
            hasFinished = true;
            IsRunning = false;
            countdownTimer.Stop();
            Close();
        }

        private StackPanel CreateScorePanel(string label)
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(10),
                VerticalAlignment = VerticalAlignment.Center
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            });
            return panel;
        }

        private Grid CreateTimeEditor()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            decreaseTimeButton = CreateStepperButton("-");
            decreaseTimeButton.Click += (_, _) => ChangeDuration(-5);
            grid.Children.Add(decreaseTimeButton);

            timeBox = new TextBox
            {
                FontSize = 54,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(0, 0, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                MinWidth = 120,
                MaxLength = 3
            };
            timeBox.PreviewTextInput += (_, e) => e.Handled = !IsDigits(e.Text);
            timeBox.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    CommitTimeInput();
                    e.Handled = true;
                }
            };
            timeBox.LostFocus += (_, _) => CommitTimeInput();
            Grid.SetColumn(timeBox, 1);
            grid.Children.Add(timeBox);

            increaseTimeButton = CreateStepperButton("+");
            increaseTimeButton.Click += (_, _) => ChangeDuration(5);
            Grid.SetColumn(increaseTimeButton, 2);
            grid.Children.Add(increaseTimeButton);

            return grid;
        }

        private TextBlock CreateBigNumberText()
        {
            return new TextBlock
            {
                FontSize = 54,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        private Button CreateButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 72,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(5, 0, 5, 0)
            };
        }

        private Button CreateStepperButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 32,
                Height = 32,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 18, 4, 0)
            };
        }

        private Border CreateSettingsPanel()
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var panel = new StackPanel();
            border.Child = panel;

            var title = new TextBlock
            {
                Text = "挑战设置",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(title);

            spawnSlider = AddSlider(panel, "生成间隔（秒）", 0.1, 30);
            minSizeSlider = AddSlider(panel, "最小泡泡大小", 10, 160);
            maxSizeSlider = AddSlider(panel, "最大泡泡大小", 10, 220);
            minSpeedSlider = AddSlider(panel, "最小速度", 0.2, 20);
            maxSpeedSlider = AddSlider(panel, "最大速度", 0.2, 30);
            ringProbabilitySlider = AddSlider(panel, "泡之财宝概率", 0, 1);
            lightProbabilitySlider = AddSlider(panel, "光泡概率", 0, 1);
            petMoveProbabilitySlider = AddSlider(panel, "宠物移动概率", 0, 1);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 0, 0)
            };
            panel.Children.Add(buttonPanel);

            var resetButton = CreateButton("恢复游戏默认");
            resetButton.Click += (_, _) =>
            {
                resetSettingsAction?.Invoke();
                RefreshSettingsPanel();
                SetDuration(getSettingsAction?.Invoke()?.DurationSeconds ?? durationSeconds);
            };
            buttonPanel.Children.Add(resetButton);

            return border;
        }

        private Border CreateResultPanel()
        {
            var border = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(255, 236, 117),
                    Color.FromRgb(245, 158, 11),
                    90),
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 251, 235)),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(24),
                Margin = new Thickness(0, 0, 0, 0)
            };

            var panel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            border.Child = panel;

            resultScoreText = new TextBlock
            {
                FontSize = 96,
                FontWeight = FontWeights.Black,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromRgb(69, 26, 3)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(resultScoreText);

            panel.Children.Add(new TextBlock
            {
                Text = "游戏结束",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 22)
            });

            var resetOnlyButton = CreateButton("重置");
            resetOnlyButton.MinWidth = 120;
            resetOnlyButton.Padding = new Thickness(18, 8, 18, 8);
            resetOnlyButton.HorizontalAlignment = HorizontalAlignment.Center;
            resetOnlyButton.Click += (_, _) => ResetRound();
            panel.Children.Add(resetOnlyButton);

            return border;
        }

        private Slider AddSlider(Panel panel, string title, double min, double max)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.Children.Add(row);

            var label = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240))
            };
            row.Children.Add(label);

            var valueText = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(valueText, 1);
            row.Children.Add(valueText);

            var snapToWholeNumber = max > 40 && Math.Abs(min - Math.Round(min)) < 0.0001;
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                TickFrequency = max <= 1 ? 0.01 : snapToWholeNumber ? 1 : 0.1,
                IsSnapToTickEnabled = snapToWholeNumber,
                SmallChange = max <= 1 ? 0.01 : 0.1,
                LargeChange = max <= 1 ? 0.1 : 1,
                AutoToolTipPrecision = max <= 1 ? 2 : 1,
                Margin = new Thickness(0, 3, 0, 0),
                Tag = valueText
            };
            slider.ValueChanged += (_, _) =>
            {
                valueText.Text = slider.Value.ToString("0.##");
                UpdateGameSettings();
            };
            Grid.SetRow(slider, 1);
            Grid.SetColumnSpan(slider, 2);
            row.Children.Add(slider);

            return slider;
        }

        private void StartCountdown()
        {
            if (settingsPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            CommitTimeInput();
            hasStarted = true;
            IsRunning = true;
            titleText.Text = "戳泡泡挑战";
            statusText.Text = "挑战中";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71));
            resumeButton.Content = "继续";
            countdownTimer.Start();
            UpdateButtonState();
            resumeAction?.Invoke();
        }

        private void PauseGame()
        {
            if (!IsRunning || hasFinished)
            {
                return;
            }

            IsRunning = false;
            countdownTimer.Stop();
            statusText.Text = "已暂停";
            pauseAction?.Invoke();
            UpdateButtonState();
        }

        private void ResumeGame()
        {
            if (IsRunning || hasFinished || remainingSeconds <= 0)
            {
                return;
            }

            StartCountdown();
        }

        private void ResetRound()
        {
            if (IsRunning)
            {
                return;
            }

            hasFinished = false;
            hasStarted = false;
            remainingSeconds = durationSeconds;
            score = 0;
            ShowGamePanel();
            titleText.Text = "戳泡泡挑战";
            statusText.Text = "准备中";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71));
            resumeButton.Content = "开始";
            settingsPanel.Visibility = Visibility.Collapsed;
            countdownTimer.Stop();
            resetAction?.Invoke();
            UpdateDisplay();
            UpdateButtonState();
        }

        private void ToggleSettingsPanel()
        {
            if (IsRunning || hasStarted || hasFinished)
            {
                return;
            }

            RefreshSettingsPanel();
            settingsPanel.Visibility = settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            UpdateButtonState();
        }

        private void CountdownTimer_Tick(object sender, EventArgs e)
        {
            remainingSeconds--;
            if (remainingSeconds <= 0)
            {
                remainingSeconds = 0;
                FinishGame();
            }

            UpdateDisplay();
        }

        private void FinishGame()
        {
            if (hasFinished)
            {
                return;
            }

            hasFinished = true;
            IsRunning = false;
            countdownTimer.Stop();
            titleText.Text = string.Empty;
            ShowResultPanel();
            statusText.Text = $"荣耀达成：本局成功点破 {score} 个泡泡！";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(250, 204, 21));
            scoreText.Text = score.ToString("000");
            finishedAction?.Invoke();
            UpdateDisplay();
            UpdateButtonState();
        }

        private void StopGame()
        {
            if (hasFinished)
            {
                return;
            }

            hasFinished = true;
            IsRunning = false;
            countdownTimer.Stop();
            statusText.Text = "已停止";
            stopAction?.Invoke();
            UpdateDisplay();
            UpdateButtonState();
            Close();
        }

        private void ChangeDuration(int deltaSeconds)
        {
            if (IsRunning)
            {
                PauseGame();
            }

            SetDuration(durationSeconds + deltaSeconds);
        }

        private void CommitTimeInput()
        {
            if (int.TryParse(timeBox.Text, out var input))
            {
                SetDuration(input);
                return;
            }

            SetDuration(durationSeconds);
        }

        private void SetDuration(int seconds)
        {
            durationSeconds = ClampDuration(seconds);
            remainingSeconds = durationSeconds;
            if (hasFinished)
            {
                hasFinished = false;
                hasStarted = false;
                score = 0;
                titleText.Text = "戳泡泡挑战";
                statusText.Text = "准备中";
                statusText.Foreground = new SolidColorBrush(Color.FromRgb(253, 224, 71));
                resumeButton.Content = "开始";
            }

            durationChangedAction?.Invoke(durationSeconds);
            UpdateDisplay();
            UpdateButtonState();
        }

        private void RefreshSettingsPanel()
        {
            var settings = getSettingsAction?.Invoke();
            if (settings == null)
            {
                return;
            }

            isUpdatingSettings = true;
            durationSeconds = ClampDuration(settings.DurationSeconds);
            if (!hasStarted || hasFinished)
            {
                remainingSeconds = durationSeconds;
            }
            SetSliderValue(spawnSlider, settings.SpawnInterval);
            SetSliderValue(minSizeSlider, settings.MinSize);
            SetSliderValue(maxSizeSlider, settings.MaxSize);
            SetSliderValue(minSpeedSlider, settings.MinSpeed);
            SetSliderValue(maxSpeedSlider, settings.MaxSpeed);
            SetSliderValue(ringProbabilitySlider, settings.RingProbability);
            SetSliderValue(lightProbabilitySlider, settings.LightProbability);
            SetSliderValue(petMoveProbabilitySlider, settings.PetMoveProbability);
            isUpdatingSettings = false;
            UpdateDisplay();
        }

        private void UpdateGameSettings()
        {
            if (isUpdatingSettings)
            {
                return;
            }

            updateSettingsAction?.Invoke(new BubbleGameSettings
            {
                DurationSeconds = durationSeconds,
                SpawnInterval = spawnSlider.Value,
                MinSize = (int)Math.Round(Math.Min(minSizeSlider.Value, maxSizeSlider.Value)),
                MaxSize = (int)Math.Round(Math.Max(minSizeSlider.Value, maxSizeSlider.Value)),
                MinSpeed = Math.Min(minSpeedSlider.Value, maxSpeedSlider.Value),
                MaxSpeed = Math.Max(minSpeedSlider.Value, maxSpeedSlider.Value),
                RingProbability = ringProbabilitySlider.Value,
                LightProbability = lightProbabilitySlider.Value,
                PetMoveProbability = petMoveProbabilitySlider.Value
            });
        }

        private void SetSliderValue(Slider slider, double value)
        {
            slider.Value = value;
            if (slider.Tag is TextBlock valueText)
            {
                valueText.Text = slider.Value.ToString("0.##");
            }
        }

        private void ShowResultPanel()
        {
            resultScoreText.Text = score.ToString("000");
            titleText.Visibility = Visibility.Collapsed;
            board.Visibility = Visibility.Collapsed;
            commandArea.Visibility = Visibility.Collapsed;
            settingsPanel.Visibility = Visibility.Collapsed;
            resultPanel.Visibility = Visibility.Visible;
        }

        private void ShowGamePanel()
        {
            titleText.Visibility = Visibility.Visible;
            resultPanel.Visibility = Visibility.Collapsed;
            board.Visibility = Visibility.Visible;
            commandArea.Visibility = Visibility.Visible;
        }

        private void UpdateDisplay()
        {
            if (!timeBox.IsKeyboardFocusWithin)
            {
                timeBox.Text = remainingSeconds.ToString("000");
            }
            scoreText.Text = score.ToString("000");
        }

        private void UpdateButtonState()
        {
            var settingsOpen = settingsPanel.Visibility == Visibility.Visible;
            pauseButton.IsEnabled = IsRunning && !hasFinished;
            resumeButton.IsEnabled = !settingsOpen && !IsRunning && !hasFinished && remainingSeconds > 0;
            resetButton.IsEnabled = !IsRunning && (hasStarted || hasFinished);
            settingsButton.IsEnabled = !IsRunning && !hasStarted && !hasFinished;
            stopButton.IsEnabled = !hasFinished;
            decreaseTimeButton.IsEnabled = !IsRunning;
            increaseTimeButton.IsEnabled = !IsRunning;
            timeBox.IsReadOnly = IsRunning;
        }

        private static bool IsDigits(string text)
        {
            foreach (var character in text)
            {
                if (!char.IsDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static int ClampDuration(int value)
        {
            return Math.Min(Math.Max(value, MinDurationSeconds), MaxDurationSeconds);
        }
    }
}
