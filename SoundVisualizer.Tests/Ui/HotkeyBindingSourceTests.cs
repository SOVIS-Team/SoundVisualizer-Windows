// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 단축키 입력 대기(LauncherWindow 의 _bindingTarget)는 창이 모든 키를 가로채 떼는 순간 단축키로 저장한다. 단축키 버튼은 자세한 설정에만
/// 있으므로, 버튼이 보이지 않게 될 때(쉬운 설정이나 다른 탭으로 넘어갈 때) 대기를 그만두지 않으면 그 뒤에 누른 Tab 이나 방향키가 먹지 않고
/// 단축키로 저장된다. 런처는 WPF 창이라 소스를 읽어 확인한다.
/// </summary>
public class HotkeyBindingSourceTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(RepositoryPaths.Root, "SoundVisualizer", file));

    [Fact]
    public void 쉬운_설정과_자세한_설정을_오갈_때_단축키_입력_대기를_그만둔다()
    {
        string source = Read("LauncherWindow.EasySettings.cs");
        int method = source.IndexOf("private void SetSettingsView(bool easy)", StringComparison.Ordinal);
        Assert.True(method >= 0, "SetSettingsView 를 찾지 못했습니다.");
        int cancel = source.IndexOf("CancelHotkeyBinding();", method, StringComparison.Ordinal);
        int show = source.IndexOf("ShowSettingsView();", method, StringComparison.Ordinal);

        Assert.True(cancel >= 0 && cancel < show, "화면을 바꾸기 전에 CancelHotkeyBinding 을 불러야 합니다.");
    }

    [Fact]
    public void 탭을_바꿀_때와_Esc_를_누를_때_같은_취소를_쓴다()
    {
        string source = Read("LauncherWindow.xaml.cs");

        Assert.Contains("Tabs.SelectionChanged += (s, e) => { if (ReferenceEquals(e.OriginalSource, Tabs)) CancelHotkeyBinding(); };", source);
        int escape = source.IndexOf("if (e.Key == System.Windows.Input.Key.Escape)", StringComparison.Ordinal);
        Assert.True(escape >= 0);
        Assert.Contains("CancelHotkeyBinding();", source.Substring(escape, 200));
    }
}
