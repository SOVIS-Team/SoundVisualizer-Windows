// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace SoundVisualizer
{
    // 설정 탭의 두 화면(안드로이드 SettingsTab, #408): 맨 위의 두 칸으로 쉬운 설정(Ui/EasySettingsView)과 자세한 설정(나머지 설정 카드)을 오간다.
    // 쉬운 설정의 '자세한 설정' 칸을 누르면 자세한 설정으로 넘어와 그 설정이 있는 칸까지 옮겨 가고 초점을 둔다(#427).
    public partial class LauncherWindow
    {
        /// <summary>두 칸의 차례. 고른 번호가 곧 화면이다.</summary>
        private const int EasyViewIndex = 0;
        private const int DetailedViewIndex = 1;

        /// <summary>저장된 화면(AppSettings.EasySettings)을 두 칸과 설정 탭에 보인다. 처음 열 때와 화면을 바꿀 때 부른다.</summary>
        private void ShowSettingsView()
        {
            bool easy = AppSettings.EasySettings;
            bool wasInitializing = _isInitializing;
            _isInitializing = true; // 칸을 맞추는 동안 다시 저장하지 않게
            ChipsSettingsView.SelectedIndex = easy ? EasyViewIndex : DetailedViewIndex;
            _isInitializing = wasInitializing;

            EasyView.Visibility = easy ? Visibility.Visible : Visibility.Collapsed;
            PanelDetailed.Visibility = easy ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ChipsSettingsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || ChipsSettingsView.SelectedIndex < 0) return;
            SetSettingsView(ChipsSettingsView.SelectedIndex == EasyViewIndex);
        }

        /// <summary>화면을 바꾸고 저장한다. 화면만 바뀌고 다른 설정은 건드리지 않는다.</summary>
        private void SetSettingsView(bool easy)
        {
            // 단축키 버튼은 자세한 설정에만 있다. 입력을 기다리는 채로 화면이 바뀌면 다음에 누른 키가 단축키로 저장된다.
            CancelHotkeyBinding();
            if (AppSettings.EasySettings != easy)
            {
                AppSettings.EasySettings = easy;
                AppSettings.Save();
            }
            ShowSettingsView();
            // 홈 안내가 인용하는 스위치 이름은 화면마다 다르다(RunStatus.AmbientSwitchLabelKey). 안내가 떠 있으면 새 화면의 이름으로 바꾼다.
            // 안내의 종류는 그대로라 화면 읽어주기가 다시 읽지는 않는다.
            UpdateNotices();
        }

        /// <summary>
        /// 쉬운 설정에서 값을 바꿨다(이미 AppSettings 에 쓰고 저장을 예약했다). 자세한 설정의 칸과 소리 탭, 홈 안내, 열려 있는 오버레이
        /// 편집 패널을 맞춘다.
        /// </summary>
        private void EasyView_SettingsChanged(object? sender, EventArgs e)
        {
            bool wasInitializing = _isInitializing;
            _isInitializing = true; // 칸을 맞추며 생기는 바뀜 알림이 다시 저장하지 않게
            LoadSettingsToUI();
            _isInitializing = wasInitializing;
            SyncOverlayEditPanel();
        }

        /// <summary>
        /// 쉬운 설정의 크기 줄에서 '자세한 설정' 칸을 눌렀다(안드로이드 #427). 자세한 설정으로 넘어가 지금 모드의 카드를 펼쳐(접어 두었어도) 설정 화면
        /// 맨 위로 올리고, 단계에 맞지 않게 만든 칸(크기 고정 스위치, 크기, 진하기 가운데 하나, EasySizes.DetailControl)에 초점을 둔다.
        /// 안드로이드는 카드까지 옮기기만 하지만, PC 는 키보드로 누른 사람이 바로 그 칸을 조절할 수 있게 초점까지 옮긴다.
        /// </summary>
        private void EasyView_DetailedRequested(object? sender, EventArgs e)
        {
            // 지금 모드의 설정(AppSettings.CurrentModeSettings)과 같은 차례로 고른다. 번호가 범위를 벗어나면 외곽선의 설정을 쓴다.
            int mode = AppSettings.VisualMode is >= 0 and < 3 ? AppSettings.VisualMode : 3;
            var controls = _modes[mode];
            var card = ModeCard(mode);

            SetSettingsView(easy: false);
            card.IsExpanded = true;

            FrameworkElement target = EasySizes.DetailControl(controls.Settings()) switch
            {
                EasySizeControl.SizeLock => controls.SizeLock,
                EasySizeControl.Opacity => controls.Opacity,
                _ => controls.Size,
            };
            // 자세한 설정이 배치된 뒤에 옮긴다. 그 전에는 카드의 자리를 모른다.
            Dispatcher.InvokeAsync(() => ScrollToCardAndFocus(card, target), DispatcherPriority.Loaded);
        }

        private Expander ModeCard(int mode) => mode switch
        {
            0 => ExpanderWave,
            1 => ExpanderPad,
            2 => ExpanderCircle,
            _ => ExpanderOutline,
        };

        /// <summary>카드의 맨 위가 설정 화면 맨 위에 오게 옮기고 <paramref name="target"/> 에 초점을 둔다. 카드 안의 칸이라 함께 보인다.</summary>
        private void ScrollToCardAndFocus(FrameworkElement card, FrameworkElement target)
        {
            if (SettingsScroller.Content is not Visual content || !card.IsVisible) return;
            double top = card.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
            SettingsScroller.ScrollToVerticalOffset(top);
            // 옮긴 자리를 먼저 정한다. 초점을 두면 그 칸이 보이게 다시 옮기는데, 옮기기 전 자리로 재면 카드가 맨 위에서 밀려난다.
            SettingsScroller.UpdateLayout();
            target.Focus();
        }
    }
}
