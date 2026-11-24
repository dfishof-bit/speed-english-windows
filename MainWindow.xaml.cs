using System.Globalization;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpeedEnglish;

public partial class MainWindow : Window
{
    private readonly List<WordPair> _words;
    private readonly AppState _state;
    private readonly Random _rng = new();
    private readonly SpeechSynthesizer _synth = new();
    private readonly MediaPlayer _mediaPlayer = new();
    private readonly DispatcherTimer _nextTimer = new();
    private readonly string? _audioPath;

    private int _streak = 0;
    private bool _locked = false;
    private int _lastWordIdx = -1;
    private int _currentWordIdx = -1;
    private int _correctBtnIdx = -1;
    private int _activeRangeStart = 1;
    private bool _isInitializingUi = true;
    private readonly List<Button> _answerButtons = new();

    public MainWindow()
    {
        InitializeComponent();
        _state = AppState.Load();
        _words = WordRepository.LoadEmbeddedWords();
        _audioPath = WordRepository.ExtractCelebrationAudioToTemp();

        _nextTimer.Tick += NextTimer_Tick;
        InitSpeechVoices();
        SyncSettingsUi();
        _isInitializingUi = false;

        SwitchTab("game");
        NextQuestion();
    }

    private void InitSpeechVoices()
    {
        try
        {
            _synth.SetOutputToDefaultAudioDevice();
            var voices = _synth.GetInstalledVoices()
                .Where(v => v.Enabled)
                .Select(v => v.VoiceInfo)
                .ToList();

            var englishVoices = voices
                .Where(v => v.Culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var listToShow = englishVoices.Count > 0 ? englishVoices : voices;
            CmbVoices.Items.Clear();
            foreach (var v in listToShow)
            {
                CmbVoices.Items.Add(v.Name);
            }

            if (!string.IsNullOrEmpty(_state.VoiceName) && listToShow.Any(v => v.Name == _state.VoiceName))
            {
                CmbVoices.SelectedItem = _state.VoiceName;
                _synth.SelectVoice(_state.VoiceName);
            }
            else if (listToShow.Count > 0)
            {
                var preferred = listToShow.FirstOrDefault(v => v.Culture.Name.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                    ?? listToShow[0];
                _state.VoiceName = preferred.Name;
                CmbVoices.SelectedItem = preferred.Name;
                _synth.SelectVoice(preferred.Name);
            }
            _synth.Rate = _state.TtsRate;
        }
        catch
        {
            // TTS optional if system has no speech voices installed
        }
    }

    private void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _synth.SpeakAsyncCancelAll();
            _synth.Rate = _state.TtsRate;
            _synth.SpeakAsync(text);
        }
        catch
        {
            // Ignore speech engine errors
        }
    }

    private void PlayCelebrationSound()
    {
        if (!_state.SoundEnabled || string.IsNullOrEmpty(_audioPath)) return;
        try
        {
            _mediaPlayer.Stop();
            _mediaPlayer.Open(new Uri(_audioPath, UriKind.Absolute));
            _mediaPlayer.Play();
        }
        catch
        {
        }
    }

    private void StopCelebrationSound()
    {
        try { _mediaPlayer.Stop(); } catch { }
    }

    // Navigation
    private void NavTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tab)
        {
            SwitchTab(tab);
        }
    }

    private void SwitchTab(string tab)
    {
        PanelGame.Visibility = tab == "game" ? Visibility.Visible : Visibility.Collapsed;
        PanelLevels.Visibility = tab == "levels" ? Visibility.Visible : Visibility.Collapsed;
        PanelSettings.Visibility = tab == "settings" ? Visibility.Visible : Visibility.Collapsed;
        PanelHelp.Visibility = tab == "help" ? Visibility.Visible : Visibility.Collapsed;

        StyleNavBtn(NavGame, tab == "game");
        StyleNavBtn(NavLevels, tab == "levels");
        StyleNavBtn(NavSettings, tab == "settings");
        StyleNavBtn(NavHelp, tab == "help");

        if (tab == "levels")
        {
            _activeRangeStart = ((_state.CurrentLevel - 1) / 50) * 50 + 1;
            RenderLevels();
        }
        else if (tab == "settings")
        {
            SyncSettingsUi();
        }
        else if (tab == "game")
        {
            UpdateHeader();
            if (_answerButtons.Count != _state.ChoicesCount)
            {
                NextQuestion();
            }
        }
    }

    private void StyleNavBtn(Button btn, bool active)
    {
        btn.Background = active ? (Brush)FindResource("BrandPrimary") : new SolidColorBrush(Color.FromRgb(248, 250, 253));
        btn.Foreground = active ? Brushes.White : (Brush)FindResource("TextPrimary");
        btn.BorderBrush = active ? (Brush)FindResource("BrandPrimary") : (Brush)FindResource("BorderBrushLight");
    }

    // Game logic
    private void UpdateHeader()
    {
        var pool = _state.GetPoolSize(_words.Count);
        TxtLevelBadge.Text = string.Format(CultureInfo.InvariantCulture, "שלב {0} • מאגר: {1:N0} מילים", _state.CurrentLevel, pool);
        TxtStreak.Text = $"רצף: {_streak} / {_state.TargetStreak}";
        ProgressStreak.Maximum = _state.TargetStreak;
        ProgressStreak.Value = Math.Min(_streak, _state.TargetStreak);
        BtnQuickMute.Content = _state.TtsEnabled ? "🔈 הקראה אוטומטית: פעילה" : "🔇 הקראה אוטומטית: מושתקת";
    }

    private void NextQuestion()
    {
        _nextTimer.Stop();
        if (_words.Count == 0) return;

        int pool = Math.Max(10, _state.GetPoolSize(_words.Count));
        int numChoices = _state.ChoicesCount;

        int idx = _rng.Next(pool);
        if (idx == _lastWordIdx && pool > 1)
        {
            idx = (idx + 1) % pool;
        }
        _lastWordIdx = idx;
        _currentWordIdx = idx;

        var target = _words[idx];
        var used = new HashSet<string> { target.Hebrew };
        var options = new List<string>(numChoices) { target.Hebrew };

        int guard = 0;
        while (options.Count < numChoices && guard < 300)
        {
            guard++;
            var cand = _words[_rng.Next(pool)].Hebrew;
            if (used.Add(cand))
            {
                options.Add(cand);
            }
        }

        // Fisher-Yates shuffle
        for (int i = options.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (options[i], options[j]) = (options[j], options[i]);
        }
        _correctBtnIdx = options.IndexOf(target.Hebrew);

        TxtEnglishWord.Text = target.English;
        TxtFeedback.Text = "";
        AnswersPanel.Children.Clear();
        _answerButtons.Clear();

        for (int i = 0; i < options.Count; i++)
        {
            int choiceIdx = i;
            var btn = new Button { Style = (Style)FindResource("AnswerButtonStyle") };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });

            var keyBadge = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(238, 242, 248)),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("TextSecondary"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(keyBadge, 0);

            var label = new TextBlock
            {
                Text = options[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(label, 1);

            grid.Children.Add(keyBadge);
            grid.Children.Add(label);
            btn.Content = grid;
            btn.Click += (_, _) => HandleAnswer(choiceIdx);

            AnswersPanel.Children.Add(btn);
            _answerButtons.Add(btn);
        }

        _locked = false;
        UpdateHeader();

        if (_state.TtsEnabled)
        {
            Speak(target.English);
        }
    }

    private void HandleAnswer(int selectedIdx)
    {
        if (_locked || selectedIdx < 0 || selectedIdx >= _answerButtons.Count) return;
        _locked = true;

        var green = (Brush)FindResource("AnswerCorrect");
        var red = (Brush)FindResource("AnswerWrong");

        if (selectedIdx == _correctBtnIdx)
        {
            HighlightButton(_answerButtons[selectedIdx], green);
            _streak++;
            _state.TotalCorrect++;
            _state.Save();
            UpdateHeader();

            if (_streak >= _state.TargetStreak)
            {
                TxtFeedback.Foreground = green;
                TxtFeedback.Text = "מצוין! השלמת את השלב!";
                _nextTimer.Interval = TimeSpan.FromMilliseconds(500);
                _nextTimer.Tag = "levelup";
                _nextTimer.Start();
            }
            else
            {
                TxtFeedback.Foreground = green;
                TxtFeedback.Text = "נכון!";
                _nextTimer.Interval = TimeSpan.FromMilliseconds(450);
                _nextTimer.Tag = "next";
                _nextTimer.Start();
            }
        }
        else
        {
            HighlightButton(_answerButtons[selectedIdx], red);
            if (_correctBtnIdx >= 0 && _correctBtnIdx < _answerButtons.Count)
            {
                HighlightButton(_answerButtons[_correctBtnIdx], green);
            }
            _streak = 0;
            _state.TotalWrong++;
            _state.Save();
            UpdateHeader();
            TxtFeedback.Foreground = red;
            TxtFeedback.Text = "לא נורא — התשובה הנכונה מסומנת בירוק";
            _nextTimer.Interval = TimeSpan.FromMilliseconds(1300);
            _nextTimer.Tag = "next";
            _nextTimer.Start();
        }
    }

    private static void HighlightButton(Button btn, Brush bg)
    {
        btn.Background = bg;
        btn.BorderBrush = bg;
        btn.Foreground = Brushes.White;
    }

    private void NextTimer_Tick(object? sender, EventArgs e)
    {
        _nextTimer.Stop();
        if (_nextTimer.Tag as string == "levelup")
        {
            ShowLevelUp();
        }
        else
        {
            NextQuestion();
        }
    }

    private void ShowLevelUp()
    {
        _streak = 0;
        int nextLvl = Math.Min(AppState.MaxLevel, _state.CurrentLevel + 1);
        _state.CurrentLevel = nextLvl;
        if (nextLvl > _state.HighestLevel) _state.HighestLevel = nextLvl;
        _state.Save();
        UpdateHeader();
        PlayCelebrationSound();

        int newPool = _state.GetPoolSize(_words.Count);
        TxtLevelUpMessage.Text = string.Format(
            CultureInfo.InvariantCulture,
            "ענית על {0} מילים ברצף! עלית לשלב {1} — המאגר שלך גדל ל-{2:N0} המילים השכיחות ביותר.",
            _state.TargetStreak, nextLvl, newPool);
        LevelUpOverlay.Visibility = Visibility.Visible;
    }

    private void BtnLevelUpContinue_Click(object sender, RoutedEventArgs e)
    {
        StopCelebrationSound();
        LevelUpOverlay.Visibility = Visibility.Collapsed;
        NextQuestion();
    }

    private void BtnSpeak_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWordIdx >= 0) Speak(_words[_currentWordIdx].English);
    }

    private void TxtEnglishWord_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentWordIdx >= 0) Speak(_words[_currentWordIdx].English);
    }

    private void BtnQuickMute_Click(object sender, RoutedEventArgs e)
    {
        _state.TtsEnabled = !_state.TtsEnabled;
        _state.Save();
        UpdateHeader();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (LevelUpOverlay.Visibility == Visibility.Visible)
        {
            if (e.Key == Key.Enter || e.Key == Key.Space)
            {
                e.Handled = true;
                BtnLevelUpContinue_Click(this, new RoutedEventArgs());
            }
            return;
        }

        if (PanelGame.Visibility != Visibility.Visible) return;
        if (Keyboard.FocusedElement is TextBox) return;

        int chosen = e.Key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            _ => -1
        };

        if (chosen >= 0 && chosen < _state.ChoicesCount && !_locked)
        {
            e.Handled = true;
            HandleAnswer(chosen);
        }
        else if (e.Key == Key.Space || e.Key == Key.R)
        {
            e.Handled = true;
            if (_currentWordIdx >= 0) Speak(_words[_currentWordIdx].English);
        }
    }

    // Levels
    private void RenderLevels()
    {
        TxtLevelsSummary.Text = $"שלב נוכחי: {_state.CurrentLevel} • שלב שיא: {_state.HighestLevel} מתוך {AppState.MaxLevel}";
        RangeTabsPanel.Children.Clear();
        for (int start = 1; start <= AppState.MaxLevel; start += 50)
        {
            int s = start;
            int end = Math.Min(AppState.MaxLevel, s + 49);
            var b = new Button
            {
                Content = $"{s}–{end}",
                Style = (Style)FindResource("NavTabButtonStyle"),
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 5, 10, 5)
            };
            StyleNavBtn(b, s == _activeRangeStart);
            b.Click += (_, _) =>
            {
                _activeRangeStart = s;
                RenderLevels();
            };
            RangeTabsPanel.Children.Add(b);
        }

        LevelsGrid.Children.Clear();
        int rangeEnd = Math.Min(AppState.MaxLevel, _activeRangeStart + 49);
        for (int lvl = _activeRangeStart; lvl <= rangeEnd; lvl++)
        {
            int selectedLvl = lvl;
            int wordsCount = lvl >= AppState.MaxLevel ? _words.Count : lvl * AppState.WordsPerLevel;
            bool isCurrent = lvl == _state.CurrentLevel;
            bool isUnlocked = lvl <= _state.HighestLevel;

            var tile = new Button
            {
                Margin = new Thickness(4),
                Padding = new Thickness(6, 10, 6, 10),
                Cursor = Cursors.Hand,
                Background = isCurrent
                    ? (Brush)FindResource("BrandPrimary")
                    : (isUnlocked ? (Brush)FindResource("BrandPrimarySoft") : Brushes.White),
                Foreground = isCurrent ? Brushes.White : (Brush)FindResource("TextPrimary"),
                BorderBrush = isCurrent ? (Brush)FindResource("BrandPrimary") : (Brush)FindResource("BorderBrushLight"),
                BorderThickness = new Thickness(1)
            };

            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = $"שלב {lvl}",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} מילים", wordsCount),
                FontSize = 11,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            tile.Content = sp;
            tile.Click += (_, _) =>
            {
                _state.CurrentLevel = selectedLvl;
                if (selectedLvl > _state.HighestLevel) _state.HighestLevel = selectedLvl;
                _streak = 0;
                _state.Save();
                SwitchTab("game");
                NextQuestion();
            };
            LevelsGrid.Children.Add(tile);
        }
    }

    private void BtnJumpLevel_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(TxtJumpLevel.Text.Trim(), out int val) && val >= 1 && val <= AppState.MaxLevel)
        {
            _state.CurrentLevel = val;
            if (val > _state.HighestLevel) _state.HighestLevel = val;
            _streak = 0;
            _state.Save();
            TxtJumpLevel.Text = "";
            SwitchTab("game");
            NextQuestion();
        }
    }

    // Settings
    private void SyncSettingsUi()
    {
        _isInitializingUi = true;
        StyleNavBtn(BtnStreak10, _state.TargetStreak == 10);
        StyleNavBtn(BtnStreak15, _state.TargetStreak == 15);
        StyleNavBtn(BtnChoices5, _state.ChoicesCount == 5);
        StyleNavBtn(BtnChoices4, _state.ChoicesCount == 4);
        ChkTts.IsChecked = _state.TtsEnabled;
        ChkSound.IsChecked = _state.SoundEnabled;
        SliderRate.Value = _state.TtsRate;
        TxtRateLabel.Text = $"מהירות הקראה: {_state.TtsRate}";
        StatHighest.Text = _state.HighestLevel.ToString(CultureInfo.InvariantCulture);
        StatCorrect.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", _state.TotalCorrect);
        StatWrong.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0}", _state.TotalWrong);
        _isInitializingUi = false;
    }

    private void BtnStreak10_Click(object sender, RoutedEventArgs e) { _state.TargetStreak = 10; _state.Save(); SyncSettingsUi(); UpdateHeader(); }
    private void BtnStreak15_Click(object sender, RoutedEventArgs e) { _state.TargetStreak = 15; _state.Save(); SyncSettingsUi(); UpdateHeader(); }
    private void BtnChoices5_Click(object sender, RoutedEventArgs e) { _state.ChoicesCount = 5; _state.Save(); SyncSettingsUi(); }
    private void BtnChoices4_Click(object sender, RoutedEventArgs e) { _state.ChoicesCount = 4; _state.Save(); SyncSettingsUi(); }

    private void ChkTts_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUi) return;
        _state.TtsEnabled = ChkTts.IsChecked == true;
        _state.Save();
        UpdateHeader();
    }

    private void ChkSound_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializingUi) return;
        _state.SoundEnabled = ChkSound.IsChecked == true;
        _state.Save();
    }

    private void CmbVoices_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializingUi) return;
        if (CmbVoices.SelectedItem is string name)
        {
            _state.VoiceName = name;
            _state.Save();
            try { _synth.SelectVoice(name); } catch { }
        }
    }

    private void SliderRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializingUi) return;
        _state.TtsRate = (int)Math.Round(SliderRate.Value);
        TxtRateLabel.Text = $"מהירות הקראה: {_state.TtsRate}";
        _state.Save();
    }

    private void BtnTestVoice_Click(object sender, RoutedEventArgs e)
    {
        Speak("Speed English, ready to practice!");
    }

    private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
    {
        _state.ResetSettings();
        SyncSettingsUi();
        UpdateHeader();
    }

    private void BtnResetProgress_Click(object sender, RoutedEventArgs e)
    {
        _state.CurrentLevel = 1;
        _state.HighestLevel = 1;
        _state.TotalCorrect = 0;
        _state.TotalWrong = 0;
        _streak = 0;
        _state.Save();
        SyncSettingsUi();
        UpdateHeader();
    }

    protected override void OnClosed(EventArgs e)
    {
        _nextTimer.Stop();
        StopCelebrationSound();
        _synth.Dispose();
        base.OnClosed(e);
    }
}
