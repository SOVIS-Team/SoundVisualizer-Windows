// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 오버레이 편집 패널의 코드 숨김(MainWindow.xaml.cs)과, 런처가 그 패널을 맞추는 배선(LauncherWindow*.cs)을 읽어 확인한다.
/// MainWindow 는 화면 전체를 덮는 WPF 창이라 단위 시험에서 띄우지 않는다. 언제 다시 채울지의 규칙은 EditPanelSyncTests 가 본다.
/// </summary>
public class EditPanelSourceTests
{
    private static string Overlay => AppSource.Read("MainWindow.xaml.cs");

    private static string OverlayMethod(string name) => AppSource.MethodBody(Overlay, name);

    [Fact]
    public void 편집_패널에서_모드_칸을_바꾸면_슬라이더를_새_모드의_값으로_다시_채운다()
    {
        // 크기·진하기·속도 같은 값은 모드마다 따로 저장된다(AppSettings 의 모드별 접근자). 모드를 바꾼 뒤 다시 채우지 않으면
        // 슬라이더가 이전 모드의 값을 보이고, 그 상태로 움직이면 새 모드에 엉뚱한 값이 들어간다. F3 단축키로 바꿀 때는 이미 다시 채운다.
        string body = OverlayMethod("EditPanelVisualMode_SelectionChanged");

        Assert.Contains("LoadSettingsToEditPanel();", body);
    }

    [Fact]
    public void 런처에서_설정을_쓰고_저장하는_곳은_모두_열린_편집_패널을_맞춘다()
    {
        // 편집 패널을 연 채 런처에서 모드·크기·스위치·색을 바꾸면 패널의 칸이 옛 값을 보였고, 그 슬라이더를 움직이면 옛 값이 새 모드에
        // 쓰였다. 반대 방향(오버레이 → 런처)만 맞추고 있었다.
        string launcher = AppSource.Read("LauncherWindow.xaml.cs");
        var member = new Regex(@"\n        (private|public|protected|internal) ");
        var saves = Regex.Matches(launcher, @"AppSettings\.Save(Now)?\(\);");
        // 자세한 설정의 칸, 색, 단축키, 모드별 초기화 넷, 소리 종류 초기화, 전체 초기화, 출력 장치의 채널 수
        Assert.True(saves.Count >= 10, "런처가 설정을 저장하는 곳을 찾지 못했습니다: " + saves.Count);
        foreach (Match save in saves)
        {
            // 저장한 그 메서드 안(다음 멤버 선언 전)에서 알려야 한다.
            var next = member.Match(launcher, save.Index);
            string rest = launcher[save.Index..(next.Success ? next.Index : launcher.Length)];
            int line = launcher[..save.Index].Count(ch => ch == '\n') + 1;
            Assert.True(rest.Contains("SyncOverlayEditPanel();", StringComparison.Ordinal),
                $"LauncherWindow.xaml.cs:{line} 에서 저장한 뒤 SyncOverlayEditPanel 을 부르지 않습니다.");
        }

        // 손으로 바꾸는 칸, 색, 초기화는 이름으로도 확인한다.
        foreach (string handler in new[]
        {
            "Setting_Changed", "BtnColor_Click", "Window_PreviewKeyUp", "CheckAndApplyDeviceChannels",
            "BtnResetWave_Click", "BtnResetPad_Click", "BtnResetCircle_Click", "BtnResetOutline_Click",
            "BtnResetSoundClassification_Click", "BtnReset_Click",
        })
        {
            Assert.Contains("SyncOverlayEditPanel();", AppSource.MethodBody(launcher, handler));
        }

        // 쉬운 설정은 스스로 저장하고 알려 온다. 런처가 자세한 설정의 칸을 맞춘 뒤 편집 패널도 맞춘다.
        string easy = AppSource.MethodBody(AppSource.Read("LauncherWindow.EasySettings.cs"), "EasyView_SettingsChanged");
        int load = easy.IndexOf("LoadSettingsToUI();", StringComparison.Ordinal);
        int sync = easy.IndexOf("SyncOverlayEditPanel();", StringComparison.Ordinal);
        Assert.True(load >= 0 && sync > load, "쉬운 설정의 변경도 편집 패널에 알려야 합니다.");
    }

    [Fact]
    public void 오버레이가_알려_온_변경을_런처에_옮기는_중에는_편집_패널로_되돌려_알리지_않는다()
    {
        // 편집 패널에서 슬라이더를 끌면 오버레이가 런처에 알리고, 런처는 칸을 맞추며 Setting_Changed 를 다시 받는다. 그것이 편집 패널로
        // 되돌아가면 사용자가 끌고 있는 패널을 런처가 다시 채우게 된다.
        string status = AppSource.Read("LauncherWindow.Status.cs");
        string start = AppSource.MethodBody(status, "StartOverlay");
        int callback = start.IndexOf("overlay.OnSettingsChangedFromHotkey = () =>", StringComparison.Ordinal);
        Assert.True(callback >= 0, "오버레이의 변경 알림을 받는 곳을 찾지 못했습니다.");
        string received = start[callback..start.IndexOf("overlay.AiAvailabilityChanged", callback, StringComparison.Ordinal)];
        int guardOn = received.IndexOf("_applyingOverlayChange = true;", StringComparison.Ordinal);
        int load = received.IndexOf("LoadSettingsToUI();", StringComparison.Ordinal);
        int guardOff = received.IndexOf("_applyingOverlayChange = false;", StringComparison.Ordinal);
        Assert.True(guardOn >= 0 && guardOn < load && load < guardOff, "칸을 맞추는 동안 _applyingOverlayChange 를 켜 둡니다.");
        Assert.Contains("finally", received[load..guardOff]);

        string sync = AppSource.MethodBody(status, "SyncOverlayEditPanel");
        int skip = sync.IndexOf("if (_applyingOverlayChange) return;", StringComparison.Ordinal);
        int reload = sync.IndexOf("_overlayWindow?.ReloadEditPanelIfOpen();", StringComparison.Ordinal);
        Assert.True(skip >= 0 && reload > skip);

        // 칸을 맞추는 일 자체(LoadSettingsToUI)는 두 방향이 함께 쓰므로 거기서는 알리지 않는다.
        Assert.DoesNotContain("SyncOverlayEditPanel", AppSource.MethodBody(AppSource.Read("LauncherWindow.xaml.cs"), "LoadSettingsToUI"));
    }

    [Fact]
    public void 편집_패널은_열려_있을_때만_다시_채우고_끄는_중이면_놓은_뒤에_채운다()
    {
        string reloadIfOpen = OverlayMethod("ReloadEditPanelIfOpen");
        int request = reloadIfOpen.IndexOf("if (!_editPanelSync.Request(_isEditMode, IsDraggingInEditPanel)) return;", StringComparison.Ordinal);
        int load = reloadIfOpen.IndexOf("LoadSettingsToEditPanel();", StringComparison.Ordinal);
        Assert.True(request >= 0 && load > request, "다시 채우기 전에 EditPanelSync 에 물어야 합니다.");
        Assert.Contains("if (_isUpdatingEditPanelSliders) return;", reloadIfOpen[..request]);

        // 슬라이더 손잡이, 크기 손잡이, 가이드라인, 펼친 목록은 끄는 동안 마우스를 잡아 둔다.
        Assert.Contains("private bool IsDraggingInEditPanel => IsMouseCaptureWithin;", Overlay);

        // 놓으면(잡았던 마우스를 놓으면) 미뤄 둔 것을 채운다. 놓는 처리가 끝난 뒤에 한다.
        Assert.Contains("AddHandler(System.Windows.Input.Mouse.LostMouseCaptureEvent,", Overlay);
        Assert.Contains("new System.Windows.Input.MouseEventHandler(OnEditPanelMouseReleased), handledEventsToo: true);", Overlay);
        string released = OverlayMethod("OnEditPanelMouseReleased");
        int later = released.IndexOf("Dispatcher.InvokeAsync(", StringComparison.Ordinal);
        int ask = released.IndexOf("if (!_editPanelSync.Released(_isEditMode, IsDraggingInEditPanel)) return;", StringComparison.Ordinal);
        int fill = released.IndexOf("LoadSettingsToEditPanel();", StringComparison.Ordinal);
        Assert.True(later >= 0 && ask > later && fill > ask);

        // 패널을 열면 스스로 채우고 닫으면 채울 것이 없으므로 미뤄 둔 것을 버린다.
        Assert.Contains("_editPanelSync.Clear();", OverlayMethod("ToggleEditMode"));
    }

    [Fact]
    public void 편집_패널을_다시_채우는_동안에는_칸의_처리기가_설정에_다시_쓰거나_런처에_알리지_않는다()
    {
        // 런처 → 패널 다시 채우기 → 칸의 바뀜 알림 → 저장 → 런처로 되돌아오는 고리가 생기지 않게 한다.
        string load = OverlayMethod("LoadSettingsToEditPanel");
        Assert.StartsWith("_isUpdatingEditPanelSliders = true;", load.TrimStart('{').TrimStart());
        Assert.EndsWith("_isUpdatingEditPanelSliders = false;", load.TrimEnd('}').TrimEnd());
        Assert.DoesNotContain("OnSettingsChangedFromHotkey", load);
        Assert.DoesNotContain("AppSettings.Save", load);

        // 패널의 값 칸에 이어 둔 처리기는 모두 채우는 중이면 바로 돌아간다. XAML 에서 찾으므로 칸을 더해도 빠지지 않는다.
        var xaml = XDocument.Parse(AppSource.Read("MainWindow.xaml"));
        var handlers = xaml.Descendants().Attributes()
            .Where(attribute => attribute.Name.LocalName is "ValueChanged" or "SelectionChanged" or "Checked" or "Unchecked")
            .Select(attribute => attribute.Value)
            .Distinct()
            .ToArray();
        Assert.True(handlers.Length >= 8, "편집 패널의 처리기를 찾지 못했습니다: " + string.Join(", ", handlers));
        foreach (string handler in handlers)
        {
            Assert.StartsWith("if (_isUpdatingEditPanelSliders) return;", OverlayMethod(handler).TrimStart('{').TrimStart());
        }
    }

    [Fact]
    public void 단축키_입력을_기다리는_버튼의_안내_글자는_패널을_다시_채워도_남는다()
    {
        // 입력을 기다리는 중에 런처에서 설정이 바뀌어 패널을 다시 채워도, 버튼이 옛 키 이름으로 돌아가면 기다리는 중인지 알 수 없다.
        string load = OverlayMethod("LoadSettingsToEditPanel");
        Assert.Contains("if (_bindingTarget != \"Visual\") BtnEditPanelVisualHotkey.Content = GetKeysName(AppSettings.VisualModeKeyBind);", load);
        Assert.Contains("if (_bindingTarget != \"Sound\") BtnEditPanelSoundModeHotkey.Content = GetKeysName(AppSettings.StereoUpmixKeyBind);", load);
        Assert.Contains("if (_bindingTarget != \"Edit\") BtnEditPanelEditHotkey.Content = GetKeysName(AppSettings.EditModeKeyBind);", load);
    }

    [Fact]
    public void 출력_장치의_채널_수가_바뀌면_열려_있는_편집_패널의_사운드_모드_칸도_맞춘다()
    {
        // 오버레이의 캡처 엔진이 런처보다 먼저 사운드 모드를 바꾸면 런처는 바뀐 것이 없다고 보고 알리지 않는다.
        string boot = OverlayMethod("BootSequence");
        int changed = boot.IndexOf("AppSettings.SoundMode = autoSoundMode;", StringComparison.Ordinal);
        int reload = boot.IndexOf("ReloadEditPanelIfOpen();", StringComparison.Ordinal);
        Assert.True(changed >= 0 && reload > changed);
    }
}
