// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 런처에서 설정이 바뀌었을 때 오버레이의 편집 패널을 언제 다시 채울지(Core 의 EditPanelSync). 열려 있으면 바로 채우고, 사용자가
/// 패널에서 무언가를 끌고 있으면 놓을 때까지 미루며, 닫혀 있으면 아무것도 하지 않는다. 배선은 EditPanelSourceTests 가 본다.
/// </summary>
public class EditPanelSyncTests
{
    [Fact]
    public void 패널이_열려_있고_끌고_있지_않으면_바로_다시_채운다()
    {
        var sync = new EditPanelSync();

        Assert.True(sync.Request(panelOpen: true, dragging: false));
        Assert.False(sync.IsPending);
    }

    [Fact]
    public void 패널이_닫혀_있으면_채우지_않고_미뤄_두지도_않는다()
    {
        // 패널은 열 때마다 스스로 채운다. 닫힌 동안의 요청을 기억해 두면 열고 나서 한 번 더 채우게 된다.
        var sync = new EditPanelSync();

        Assert.False(sync.Request(panelOpen: false, dragging: false));
        Assert.False(sync.IsPending);
        Assert.False(sync.Released(panelOpen: true, dragging: false));
    }

    [Fact]
    public void 끄는_중에는_칸을_건드리지_않고_놓은_뒤에_한_번_채운다()
    {
        // 끄는 슬라이더의 값이 손 밑에서 바뀌면 다음 움직임이 엉뚱한 값을 설정에 쓴다.
        var sync = new EditPanelSync();

        Assert.False(sync.Request(panelOpen: true, dragging: true));
        Assert.False(sync.Request(panelOpen: true, dragging: true)); // 끄는 동안 런처에서 여러 번 바뀌어도
        Assert.True(sync.IsPending);

        Assert.True(sync.Released(panelOpen: true, dragging: false)); // 놓으면 한 번만 채운다
        Assert.False(sync.IsPending);
        Assert.False(sync.Released(panelOpen: true, dragging: false));
    }

    [Fact]
    public void 미뤄_둔_것이_없으면_놓아도_채우지_않는다()
    {
        // 패널에서 슬라이더를 끌었다 놓을 때마다 다시 채우면 안 된다. 런처가 바꾼 것이 없으면 칸은 이미 맞다.
        var sync = new EditPanelSync();

        Assert.False(sync.Released(panelOpen: true, dragging: false));
        Assert.True(sync.Request(panelOpen: true, dragging: false));
        Assert.False(sync.Released(panelOpen: true, dragging: false));
    }

    [Fact]
    public void 하나를_놓았어도_아직_다른_것을_끌고_있으면_계속_미룬다()
    {
        var sync = new EditPanelSync();
        sync.Request(panelOpen: true, dragging: true);

        Assert.False(sync.Released(panelOpen: true, dragging: true));
        Assert.True(sync.IsPending);
        Assert.True(sync.Released(panelOpen: true, dragging: false));
    }

    [Fact]
    public void 끄는_사이에_패널이_닫히거나_스스로_채웠으면_미뤄_둔_것을_버린다()
    {
        var sync = new EditPanelSync();
        sync.Request(panelOpen: true, dragging: true);
        Assert.False(sync.Released(panelOpen: false, dragging: false)); // 놓기 전에 닫혔다
        Assert.False(sync.IsPending);

        sync.Request(panelOpen: true, dragging: true);
        sync.Clear(); // 패널을 다시 열어 스스로 채웠다
        Assert.False(sync.Released(panelOpen: true, dragging: false));
    }

    [Fact]
    public void 끄는_중에_미뤘어도_다음_요청_때_끌고_있지_않으면_그때_채운다()
    {
        var sync = new EditPanelSync();
        sync.Request(panelOpen: true, dragging: true);

        Assert.True(sync.Request(panelOpen: true, dragging: false));
        Assert.False(sync.IsPending);
    }
}
