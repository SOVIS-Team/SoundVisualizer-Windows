// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Windows.Controls;

namespace SoundVisualizer.Ui
{
    /// <summary>
    /// 칸을 눌러 하나를 고르는 선택지 줄(Themes/Theme.xaml 의 ChoiceChips 서식). 예전 콤보 상자 자리에 둔다.
    /// ListBox 라서 화살표 키로 옮겨 고를 수 있고, 화면 읽어주기는 칸마다 "선택됨"을 읽는다. 고른 번호는 콤보처럼 SelectedIndex 다.
    ///
    /// ListBox 는 Ctrl+클릭(Ctrl+스페이스)으로 고른 칸을 풀 수 있다. 표현 모드와 사운드 모드는 늘 하나가 골라져 있어야 하므로
    /// (번호 -1 을 설정에 쓰면 오버레이가 모드 이름을 찾지 못한다), 풀리면 방금 것을 다시 고르고 푼 일은 알리지 않는다.
    /// </summary>
    public sealed class ChoiceChipList : ListBox
    {
        private bool _clearing;

        /// <summary>
        /// 고른 칸을 코드에서 비운다. 쉬운 설정의 크기 칸이 쓴다: 자세한 설정에서 맞춘 크기는 세 칸 어느 것에도 맞지 않아 아무 칸도 고르지 않는다.
        /// 사용자가 Ctrl+클릭으로 푸는 것은 지금처럼 막는다.
        /// </summary>
        public void ClearSelection()
        {
            _clearing = true;
            try
            {
                SelectedIndex = -1;
            }
            finally
            {
                _clearing = false;
            }
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            if (!_clearing && SelectedIndex < 0 && e.RemovedItems.Count > 0)
            {
                SelectedItem = e.RemovedItems[0];
                return;
            }
            base.OnSelectionChanged(e);
        }
    }
}
