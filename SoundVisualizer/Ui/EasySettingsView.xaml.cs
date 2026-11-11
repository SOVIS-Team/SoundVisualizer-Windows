// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SoundVisualizer.Localization;
using SoundVisualizer.Rendering;

namespace SoundVisualizer.Ui
{
    /// <summary>
    /// 설정 탭의 쉬운 설정(안드로이드 EasySettingsView, #408). 1차 실증(농학교)에서 "설정이 너무 세세하고, 글과 슬라이더보다 그림이 낫다"는
    /// 의견이 나와 그림으로 선택하는 화면을 따로 두었다. 새로 설치한 사람은 이 화면부터 본다(AppSettings.EasySettings).
    ///
    /// - 모양: 표현 모드 넷을 실제 엔진으로 그린 카드로 선택한다(ModePreview). 고른 카드만 잠깐 움직인다. 카드를 선택하거나, 고른 카드를 다시
    ///   누르거나, 크기를 바꾸거나, 이 화면이 보이게 될 때다(설정 탭에 들어오거나 자세한 설정에서 넘어올 때).
    /// - 크기: 슬라이더 대신 세 단계(EasySizes). 지금 모드의 크기만 바꾼다. 자세한 설정에서 단계와 다르게 맞춘 크기면 어느 칸도 선택해 두지 않고
    ///   한 줄로 알린다(#425). 밑의 '자세한 설정' 칸은 자세한 설정의 그 칸으로 간다(#429, 런처가 옮긴다).
    /// - 소리 종류: 종류마다 화면 표시를 켜고 끈다. 안드로이드의 진동 세 칸은 PC 에 진동이 없어 뺐다.
    ///
    /// 쉬운 설정은 자기가 맡은 값만 바꾼다. 자세한 설정에만 있는 값(속도·민감도·광원·색·단축키 등)은 그대로 남아 계속 적용된다.
    /// 값은 AppSettings 에 바로 쓰고 저장을 예약한 뒤 <see cref="SettingsChanged"/> 로 알린다. 런처가 자세한 설정의 칸을 맞춘다.
    /// </summary>
    public partial class EasySettingsView : UserControl
    {
        public static readonly DependencyProperty CurrentModeProperty = DependencyProperty.Register(
            nameof(CurrentMode), typeof(int), typeof(EasySettingsView), new FrameworkPropertyMetadata(VisualizerEngine.ModeWave));

        private bool _loading;
        private int _clickedSelected = -1; // 누르기 시작할 때 이미 골라져 있던 모양 카드(다시 누르면 한 번 더 움직인다)
        private bool _customShown;

        public EasySettingsView()
        {
            InitializeComponent();
            IsVisibleChanged += (s, e) => OnVisibleChanged();
        }

        /// <summary>
        /// 설정 탭에 들어오거나 자세한 설정에서 넘어와 보이게 됐다. 그동안 바뀐 값(오버레이 편집, 자세한 설정)을 다시 읽고 고른 카드를 움직인다.
        ///
        /// 선택 상태(모양·크기 칸, 소리 종류 줄)는 그 자리에서 맞춘다. 예전에는 모두 배치 뒤(Loaded)로 미뤘는데, Loaded 는 그리기보다 늦어
        /// 자세한 설정에서 값을 바꾸고 넘어오면 한 프레임 동안 이전 선택이 보였다. 배치 뒤로 미루는 것은 배치가 있어야 하는 일뿐이다.
        /// 카드의 그림(ModePreview)은 서식이 붙은 뒤에야 찾을 수 있고, 화면 읽어주기 알림은 요소가 놓인 뒤에 보낸다.
        /// </summary>
        private void OnVisibleChanged()
        {
            if (!IsVisible) return;
            ReloadSelection();
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsVisible) return;
                ShowPreviews();
                AnnounceCustomNote();
                PlaySelected();
            }, DispatcherPriority.Loaded);
        }

        /// <summary>쉬운 설정에서 값을 바꿨다. 런처가 자세한 설정의 칸과 다른 탭을 맞춘다.</summary>
        public event EventHandler? SettingsChanged;

        /// <summary>크기 줄의 '자세한 설정' 칸을 눌렀다. 런처가 자세한 설정으로 넘어가 지금 모드의 그 칸을 보여 준다.</summary>
        public event EventHandler? DetailedRequested;

        /// <summary>크기 칸의 그림이 그리는 표현 모드(지금 모드).</summary>
        public int CurrentMode
        {
            get => (int)GetValue(CurrentModeProperty);
            set => SetValue(CurrentModeProperty, value);
        }

        /// <summary>
        /// AppSettings 를 다시 읽어 칸을 맞춘다. 런처가 설정을 다시 그릴 때마다(오버레이 편집, 단축키, 초기화) 부른다.
        /// 움직이던 그림은 설정이 바뀌었을 때만 멈춘다.
        /// </summary>
        public void Reload()
        {
            ReloadSelection();
            ShowPreviews();
            AnnounceCustomNote();
        }

        /// <summary>
        /// 선택 상태만 맞춘다: 고른 모양 카드와 크기 칸, 단계와 다른 크기라는 안내, 소리 종류 줄의 스위치와 색, 화면 읽어주기 이름.
        /// 이 화면의 XAML 에 이름 붙여 둔 요소의 속성만 바꾸고 카드의 서식 안쪽(그림)은 찾지 않으므로, 서식이 붙기 전(보이게 된 바로 그때)에
        /// 불러도 된다.
        /// </summary>
        private void ReloadSelection()
        {
            _loading = true;
            try
            {
                int mode = AppSettings.VisualMode;
                bool known = mode >= VisualizerEngine.ModeWave && mode <= VisualizerEngine.ModeOutline;
                // 설정 파일의 모드 번호가 범위를 벗어나면 어느 카드도 고르지 않는다. 크기는 지금 쓰는 모드 설정(AppSettings.CurrentModeSettings)이다.
                if (known) ModeCards.SelectedIndex = mode;
                else ModeCards.ClearSelection();
                CurrentMode = known ? mode : VisualizerEngine.ModeOutline;

                var step = EasySizes.Of(AppSettings.CurrentModeSettings);
                if (step is { } s) SizeChips.SelectedIndex = (int)s;
                else SizeChips.ClearSelection();
                ShowCustomNote(step == null);

                ChkDanger.IsChecked = AppSettings.ShowDanger;
                ChkSpeech.IsChecked = AppSettings.ShowSpeech;
                ChkAmbient.IsChecked = AppSettings.ShowAmbient;

                ChkDanger.Background = Frozen(ParseColor(AppSettings.ColorDanger));
                ChkSpeech.Background = Frozen(ParseColor(AppSettings.ColorSpeech));
                ChkAmbient.Background = Frozen(ParseColor(AppSettings.ColorAmbient));

                ApplyCodeTexts();
            }
            finally
            {
                _loading = false;
            }
        }

        /// <summary>
        /// 모양 카드의 그림을 지금 설정과 색으로 다시 그린다. 서식이 아직 붙지 않은 카드는 건너뛴다(<see cref="PreviewAt"/> 이 null).
        /// 그런 카드는 이 화면이 보이게 된 뒤 배치가 끝나면 그린다(<see cref="OnVisibleChanged"/>).
        /// </summary>
        private void ShowPreviews()
        {
            Color ambient = ParseColor(AppSettings.ColorAmbient);
            Color speech = ParseColor(AppSettings.ColorSpeech);
            Color danger = ParseColor(AppSettings.ColorDanger);
            for (int i = 0; i < ModeCards.Items.Count; i++)
            {
                PreviewAt(i)?.Show(ModeSettings(i), ambient, speech, danger);
            }
        }

        /// <summary>
        /// 바인딩이 아니라 코드가 넣는 화면 읽어주기 이름: 크기 제목("크기, 원형")과 소리 종류 줄("위협음 표시"). 언어가 바뀌면 런처가 부른다.
        /// </summary>
        public void ApplyCodeTexts()
        {
            string pair = Loc.Get("easy.name_pair");
            string sizeName = string.Format(System.Globalization.CultureInfo.InvariantCulture, pair, Loc.Get("easy.section.size"), ModeName(CurrentMode));
            AutomationProperties.SetName(TxtSizeTitle, sizeName);
            AutomationProperties.SetName(SizeChips, sizeName);

            AutomationProperties.SetName(ChkDanger, EasyNames.TypeRow(Loc.Get("sounds.type.danger"), Loc.Get("settings.show_danger"), pair));
            AutomationProperties.SetName(ChkSpeech, EasyNames.TypeRow(Loc.Get("sounds.type.speech"), Loc.Get("settings.show_speech"), pair));
            AutomationProperties.SetName(ChkAmbient, EasyNames.TypeRow(Loc.Get("sounds.type.ambient"), Loc.Get("settings.show_ambient"), pair));
        }

        /// <summary>모드 이름(표현 모드 칸과 같은 글자).</summary>
        private static string ModeName(int mode) => mode switch
        {
            VisualizerEngine.ModePad => Loc.Get("settings.mode.pad"),
            VisualizerEngine.ModeCircle => Loc.Get("settings.mode.circle"),
            VisualizerEngine.ModeOutline => Loc.Get("settings.mode.outline"),
            _ => Loc.Get("settings.mode.wave"),
        };

        private static VisualModeSettings ModeSettings(int mode) => mode switch
        {
            VisualizerEngine.ModePad => AppSettings.PadMode,
            VisualizerEngine.ModeCircle => AppSettings.CircleMode,
            VisualizerEngine.ModeOutline => AppSettings.OutlineMode,
            _ => AppSettings.WaveMode,
        };

        // ==========================================
        // 모양
        // ==========================================

        private void ModeCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || ModeCards.SelectedIndex < 0) return;
            AppSettings.VisualMode = ModeCards.SelectedIndex;
            Changed();
            PlaySelected();
        }

        // 고른 카드를 다시 누르면 한 번 더 움직인다(안드로이드와 같다). 다른 카드를 누르면 선택이 바뀌어 위에서 움직인다.
        private void ModeCards_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
            _clickedSelected = CardIndexUnder(e.OriginalSource) is { } index && index == ModeCards.SelectedIndex ? index : -1;

        private void ModeCards_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            int pressed = _clickedSelected;
            _clickedSelected = -1;
            if (pressed >= 0 && CardIndexUnder(e.OriginalSource) == pressed) PlaySelected();
        }

        // 키보드로는 고른 카드에서 스페이스나 Enter 를 누르면 한 번 더 움직인다. 고르는 일은 목록이 그대로 한다.
        private void ModeCards_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Space or Key.Enter)) return;
            if (CardIndexUnder(e.OriginalSource) is { } index && index == ModeCards.SelectedIndex) PlaySelected();
        }

        private int? CardIndexUnder(object source)
        {
            for (var element = source as DependencyObject; element != null && element != ModeCards; element = TreeParent(element))
            {
                if (element is ListBoxItem item)
                {
                    int index = ModeCards.Items.IndexOf(item);
                    return index >= 0 ? index : null;
                }
            }
            return null;
        }

        private static DependencyObject? TreeParent(DependencyObject element) =>
            element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

        /// <summary>고른 모양 카드의 그림만 처음부터 움직이고 다른 카드는 멈춘다.</summary>
        private void PlaySelected()
        {
            for (int i = 0; i < ModeCards.Items.Count; i++)
            {
                var preview = PreviewAt(i);
                if (preview == null) continue;
                if (i == ModeCards.SelectedIndex) preview.Play();
                else preview.Stop();
            }
        }

        /// <summary>그 카드의 그림. 카드는 XAML 에 적은 ListBoxItem 그대로라 Items 가 곧 칸이다. 서식이 아직 붙지 않았으면 null 이다.</summary>
        private ModePreview? PreviewAt(int index)
        {
            if (ModeCards.Items[index] is not ListBoxItem item || item.Template == null) return null;
            item.ApplyTemplate();
            return item.Template.FindName("Preview", item) as ModePreview;
        }

        // ==========================================
        // 크기
        // ==========================================

        private void SizeChips_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || SizeChips.SelectedIndex < 0) return;
            // 지금 모드에만 쓴다. 크기 고정을 켜 두었으면 끈다(누른 크기가 실제로 보여야 한다). 나머지 값은 그대로다.
            EasySizes.All[SizeChips.SelectedIndex].ApplyTo(AppSettings.CurrentModeSettings);
            Changed();
            PlaySelected();
        }

        /// <summary>단계와 다른 크기면 한 줄 안내를 보이고 '자세한 설정' 칸을 고른 칸처럼 보인다.</summary>
        private void ShowCustomNote(bool custom)
        {
            BtnSizeDetailed.IsCurrent = custom;
            TxtSizeCustom.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 단계와 다른 크기라는 안내가 새로 나타났으면 화면 읽어주기가 읽게 한다(이미 보이던 안내는 다시 읽지 않는다).
        /// 보이게 된 바로 그때가 아니라 배치 뒤에 부른다(<see cref="OnVisibleChanged"/>).
        /// </summary>
        private void AnnounceCustomNote()
        {
            bool custom = TxtSizeCustom.Visibility == Visibility.Visible;
            if (custom && !_customShown && IsVisible && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            {
                UIElementAutomationPeer.CreatePeerForElement(TxtSizeCustom)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
            _customShown = custom;
        }

        private void BtnSizeDetailed_Click(object sender, RoutedEventArgs e) => DetailedRequested?.Invoke(this, EventArgs.Empty);

        // ==========================================
        // 소리 종류
        // ==========================================

        private void TypeRow_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            if (sender == ChkDanger) AppSettings.ShowDanger = ChkDanger.IsChecked == true;
            else if (sender == ChkSpeech) AppSettings.ShowSpeech = ChkSpeech.IsChecked == true;
            else if (sender == ChkAmbient) AppSettings.ShowAmbient = ChkAmbient.IsChecked == true;
            Changed();
        }

        /// <summary>바꾼 값을 저장하고(미뤄 모은다) 이 화면과 런처를 맞춘다.</summary>
        private void Changed()
        {
            AppSettings.Save();
            Reload();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (Exception)
            {
                // 오버레이와 같이 읽을 수 없는 색은 흰색으로 그린다.
                return Colors.White;
            }
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
