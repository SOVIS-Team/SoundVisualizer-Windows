// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 쉬운 설정 화면(Ui/EasySettingsView)과 런처의 두 칸·'자세한 설정' 이동을 소스로 확인한다(안드로이드 EasySettingsSourceTest).
/// 런처는 WPF 창이라 단위 시험에서 띄우지 않는다. 규칙 자체(크기 단계, 기본 화면)는 EasySizesTests·AppSettingsTests 가 본다.
/// </summary>
public class EasySettingsSourceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root, "SoundVisualizer" }.Concat(parts).ToArray()));

    private static XDocument ViewXaml => XDocument.Parse(Source("Ui", "EasySettingsView.xaml"));

    private static XElement Named(XDocument document, string name) =>
        document.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name);

    [Fact]
    public void 크기_칸의_차례는_단계의_차례와_같다()
    {
        // 화면은 고른 번호로 단계를 찾는다(EasySizes.All[번호]).
        var tags = Named(ViewXaml, "SizeChips").Elements().Select(e => (string?)e.Attribute("Tag")).ToArray();
        Assert.Equal(EasySizes.All.Select(step => "{x:Static core:EasySize." + step + "}"), tags);
    }

    [Fact]
    public void 모양_카드의_차례는_표현_모드_번호와_같다()
    {
        var tags = Named(ViewXaml, "ModeCards").Elements().Select(e => (string?)e.Attribute("Tag")).ToArray();
        Assert.Equal(new[] { "ModeWave", "ModePad", "ModeCircle", "ModeOutline" }.Select(name => "{x:Static r:VisualizerEngine." + name + "}"), tags);
    }

    [Fact]
    public void 자세한_설정_칸은_크기_선택지_묶음_밖의_단추다()
    {
        // 선택지 묶음(ListBox)은 화살표 키로 옮기기만 해도 칸이 골라진다. 같은 묶음에 두면 크기를 화살표로 훑다가 자세한 설정으로 넘어간다.
        var view = ViewXaml;
        var button = Named(view, "BtnSizeDetailed");
        Assert.Equal("DetailedChoiceButton", button.Name.LocalName);
        Assert.DoesNotContain(button, Named(view, "SizeChips").Descendants());
        // 이름은 맨 위 두 칸의 '자세한 설정'과 같은 글자다.
        Assert.Equal("{l:Loc settings.view.detailed}", (string?)button.Attribute("Content"));

        // 선택이 비는 까닭을 알리는 한 줄(#425)은 칸들과 단추 사이에 있고, 나타날 때 화면 읽어주기가 읽는다.
        var parent = button.Parent!.Elements().ToList();
        var note = Named(view, "TxtSizeCustom");
        Assert.True(parent.IndexOf(Named(view, "SizeChips")) < parent.IndexOf(note) && parent.IndexOf(note) < parent.IndexOf(button));
        Assert.Equal("Polite", (string?)note.Attribute("AutomationProperties.LiveSetting"));
    }

    [Fact]
    public void 소리_종류는_소리_탭의_종류_이름을_그대로_쓰고_위협음부터_둔다()
    {
        // 종류 이름을 새로 만들지 않는다. 소리 탭의 이름이 바뀌면(대화음 → 사람 소리) 쉬운 설정도 함께 바뀐다.
        string xaml = Source("Ui", "EasySettingsView.xaml");
        var contents = new[] { "ChkDanger", "ChkSpeech", "ChkAmbient" }.Select(name => (string?)Named(ViewXaml, name).Attribute("Content")).ToArray();
        Assert.Equal(new[] { "{l:Loc sounds.type.danger}", "{l:Loc sounds.type.speech}", "{l:Loc sounds.type.ambient}" }, contents);
        Assert.DoesNotMatch(new Regex(@"\{l:Loc easy\.type"), xaml);

        // 화면 읽어주기의 이름은 자세한 설정의 스위치 이름으로 짓는다.
        string code = Source("Ui", "EasySettingsView.xaml.cs");
        foreach (var type in new[] { "danger", "speech", "ambient" })
        {
            Assert.Contains($"EasyNames.TypeRow(Loc.Get(\"sounds.type.{type}\"), Loc.Get(\"settings.show_{type}\"), pair)", code);
        }
    }

    [Fact]
    public void 모양_카드는_Windows_애니메이션_효과가_켜져_있고_보일_때만_움직인다()
    {
        string preview = Source("Ui", "ModePreview.cs");
        string play = preview[preview.IndexOf("public void Play()", StringComparison.Ordinal)..];
        play = play[..play.IndexOf("public void Stop()", StringComparison.Ordinal)];
        Assert.Contains("!SystemParameters.ClientAreaAnimation || !IsVisible", play);
        // 정해 둔 시간만큼 움직이면 스스로 멈춘다.
        Assert.Contains("if (t >= TutorialDemo.PreviewPlaySec) Stop();", preview);
        // 그림은 실제 방향이라 오른쪽에서 왼쪽으로 쓰는 언어에서도 뒤집지 않는다.
        Assert.Contains("FlowDirection = FlowDirection.LeftToRight;", preview);
    }

    [Fact]
    public void 체크_표시는_오른쪽에서_왼쪽으로_쓰는_언어에서도_뒤집지_않는다()
    {
        // 뒤집으면 아래로 꺾인 화살표처럼 보였다(아랍어 화면에서 확인).
        var style = ViewXaml.Descendants().Single(e => e.Name.LocalName == "Style" && (string?)e.Attribute(Xaml + "Key") == "CheckMark");
        Assert.Contains(style.Elements(), setter => (string?)setter.Attribute("Property") == "FlowDirection" && (string?)setter.Attribute("Value") == "LeftToRight");
    }

    [Fact]
    public void 런처는_저장된_화면으로_열고_자세한_설정_칸은_그_칸에_초점을_둔다()
    {
        string launcher = Source("LauncherWindow.xaml.cs");
        Assert.Contains("ShowSettingsView();", launcher);
        Assert.Contains("EasyView?.Reload();", launcher);
        Assert.Contains("EasyView.ApplyCodeTexts();", launcher);

        string easy = Source("LauncherWindow.EasySettings.cs");
        Assert.Contains("EasySizes.DetailControl(controls.Settings())", easy);
        Assert.Contains("card.IsExpanded = true;", easy);
        Assert.Contains("target.Focus();", easy);
        Assert.Contains("AppSettings.EasySettings = easy;", easy);

        // 두 칸과 쉬운 설정 화면은 설정 탭 맨 위에 있고, 자세한 설정(초기화 버튼 포함)은 그 밑의 한 묶음이다.
        var window = XDocument.Parse(Source("LauncherWindow.xaml"));
        var detailed = Named(window, "PanelDetailed");
        var order = detailed.Parent!.Elements().ToList();
        Assert.True(order.IndexOf(Named(window, "ChipsSettingsView").Parent!) < order.IndexOf(Named(window, "EasyView")));
        Assert.True(order.IndexOf(Named(window, "EasyView")) < order.IndexOf(detailed));
        Assert.Contains(Named(window, "BtnReset"), detailed.Descendants());
    }
}
