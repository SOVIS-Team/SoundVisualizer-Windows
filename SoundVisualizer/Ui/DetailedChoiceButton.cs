// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Windows;
using System.Windows.Controls;

namespace SoundVisualizer.Ui
{
    /// <summary>
    /// 쉬운 설정의 크기 칸 밑에 있는 '자세한 설정' 칸(안드로이드 #427 의 DetailedChoiceCell). 누르면 값을 바꾸지 않고 자세한 설정의 그 칸으로 간다.
    /// 지금 값이 세 단계 어느 것에도 맞지 않으면(<see cref="IsCurrent"/>) 고른 칸처럼 강조 바탕과 체크 표시로 보인다(안드로이드와 같은 모양).
    ///
    /// 안드로이드는 이 칸을 세 단계와 한 선택지 묶음에 넣지만 PC 는 따로 둔 단추다. 선택지 묶음(ListBox)은 화살표 키로 옮기기만 해도 그 칸이
    /// 골라지므로, 같은 묶음에 두면 크기 칸을 화살표로 훑다가 뜻하지 않게 자세한 설정으로 넘어간다. 그래서 고른 칸처럼 보이는 까닭은
    /// 단추 위의 한 줄 안내(#425)가 글로도 알린다.
    /// </summary>
    public sealed class DetailedChoiceButton : Button
    {
        public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
            nameof(IsCurrent), typeof(bool), typeof(DetailedChoiceButton), new FrameworkPropertyMetadata(false));

        /// <summary>지금 값이 자세한 설정에서 맞춘 것인지(세 단계 어느 것에도 맞지 않는지).</summary>
        public bool IsCurrent
        {
            get => (bool)GetValue(IsCurrentProperty);
            set => SetValue(IsCurrentProperty, value);
        }
    }
}
