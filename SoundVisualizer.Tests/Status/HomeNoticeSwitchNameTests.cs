// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Globalization;
using System.Xml.Linq;
using SoundVisualizer.Localization;
using SoundVisualizer.Status;
using SoundVisualizer.Tests.Ui;

namespace SoundVisualizer.Tests.Status;

/// <summary>
/// 홈 안내 "…설정 탭에서 ‘…’ 스위치를 켜세요"(notice.ai_unavailable_hidden)가 인용하는 스위치 이름. 인용한 글자가 설정 탭에 지금 보이는
/// 화면에 그대로 적혀 있어야 찾아 누를 수 있다. 자세한 설정의 스위치는 "환경음 표시", 쉬운 설정의 줄은 "환경음"이다.
/// 런처는 WPF 창이라 배선은 소스를 읽어 확인한다.
/// </summary>
public class HomeNoticeSwitchNameTests
{
    private const string NoticeKey = "notice.ai_unavailable_hidden";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var code in StringTables.AvailableCodes) data.Add(code);
        return data;
    }

    private static string Get(string code, string key)
    {
        Assert.True(StringTables.Get(code)!.TryGet(key, out var text), $"{code}: '{key}' 가 없습니다.");
        return text;
    }

    private static string? ContentOf(string xaml, string name) =>
        (string?)XDocument.Parse(xaml).Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == name).Attribute("Content");

    [Fact]
    public void 인용하는_이름은_설정_탭에_지금_보이는_화면의_스위치에_적힌_글자다()
    {
        string detailedKey = RunStatus.AmbientSwitchLabelKey(easySettings: false);
        string easyKey = RunStatus.AmbientSwitchLabelKey(easySettings: true);

        // 자세한 설정의 스위치와 쉬운 설정의 줄에 실제로 적힌 글자의 키와 같아야 한다.
        Assert.Equal("{l:Loc " + detailedKey + "}", ContentOf(AppSource.Read("LauncherWindow.xaml"), "ChkShowAmbient"));
        Assert.Equal("{l:Loc " + easyKey + "}", ContentOf(AppSource.Read("Ui", "EasySettingsView.xaml"), "ChkAmbient"));
        Assert.NotEqual(detailedKey, easyKey);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 안내는_어느_언어에서나_그_화면에_보이는_이름을_그대로_인용한다(string code)
    {
        string notice = Get(code, NoticeKey);
        // 이름은 문장에 한 번만 끼운다(따옴표 안의 {0}). 문구를 고칠 때 자리가 사라지지 않게 한다.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(notice, @"\{0\}"));

        foreach (bool easySettings in new[] { false, true })
        {
            string label = Get(code, RunStatus.AmbientSwitchLabelKey(easySettings));
            string sentence = string.Format(CultureInfo.InvariantCulture, notice, label);
            Assert.Contains(label, sentence);
        }
    }

    [Fact]
    public void 한국어와_영어에서는_쉬운_설정의_안내가_종류_이름만_인용한다()
    {
        // 독일어·이탈리아어·스페인어처럼 두 화면의 이름이 같은 언어도 있다. 이름이 다른 언어에서 쉬운 설정의 안내가 달라지는지를
        // 두 언어의 실제 문장으로 본다.
        string Sentence(string code, bool easySettings) =>
            string.Format(CultureInfo.InvariantCulture, Get(code, NoticeKey), Get(code, RunStatus.AmbientSwitchLabelKey(easySettings)));

        Assert.Contains("‘환경음 표시’ 스위치", Sentence("ko", easySettings: false));
        Assert.Contains("‘환경음’ 스위치", Sentence("ko", easySettings: true));
        Assert.DoesNotContain("환경음 표시’", Sentence("ko", easySettings: true));
        Assert.Contains("“Show ambient sounds”", Sentence("en", easySettings: false));
        Assert.Contains("“Ambient”", Sentence("en", easySettings: true));
    }

    [Fact]
    public void 런처는_지금_화면의_이름을_인용하고_화면을_바꾸면_안내를_다시_넣는다()
    {
        string notices = AppSource.MethodBody(AppSource.Read("LauncherWindow.Status.cs"), "UpdateNotices");
        Assert.Contains("Loc.Get(RunStatus.AmbientSwitchLabelKey(AppSettings.EasySettings))", notices);
        // 예전에는 늘 자세한 설정의 이름을 인용했다. 쉬운 설정을 쓰는 사람은 화면에 없는 "환경음 표시" 스위치를 찾아야 했다.
        Assert.DoesNotContain("Loc.Get(\"settings.show_ambient\")", notices);

        // 안내가 떠 있는 채로 쉬운 설정과 자세한 설정을 오가면 인용한 이름도 새 화면의 것으로 바뀌어야 한다.
        string setView = AppSource.MethodBody(AppSource.Read("LauncherWindow.EasySettings.cs"), "SetSettingsView");
        int show = setView.IndexOf("ShowSettingsView();", StringComparison.Ordinal);
        int update = setView.IndexOf("UpdateNotices();", StringComparison.Ordinal);
        Assert.True(show >= 0 && update > show, "화면을 바꾼 뒤에 UpdateNotices 를 불러야 합니다.");
    }
}
