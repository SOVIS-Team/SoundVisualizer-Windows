// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer
{
    /// <summary>
    /// 오버레이의 편집 패널(F4)을 패널 밖에서 바뀐 설정으로 언제 다시 채울지 정한다.
    ///
    /// 편집 패널을 연 채로 런처(자세한 설정, 쉬운 설정)에서 모드·크기·색을 바꾸면 패널의 칸은 옛 값을 보인다. 그대로 두면 사용자가
    /// 옛 값을 보이는 슬라이더를 움직일 때 그 값이 새 모드에 쓰인다. 그래서 런처가 설정을 바꿀 때마다 패널을 다시 채워 달라고 한다
    /// (<see cref="Request"/>).
    ///
    /// 다만 사용자가 패널에서 무언가를 끌고 있는 동안에는 칸을 건드리지 않는다. 끄는 슬라이더의 값이 손 밑에서 바뀌면 다음 움직임이
    /// 엉뚱한 값을 쓴다. 그때는 기억해 두었다가 놓은 뒤에 채운다(<see cref="Released"/>). 패널이 닫혀 있으면 아무것도 하지 않는다.
    /// 패널은 열 때마다 스스로 채운다.
    ///
    /// 화면은 오버레이(MainWindow)가 맡고, 여기는 채울 차례인지만 정해 창 없이 검사한다(EditPanelSyncTests). UI 스레드에서만 쓴다.
    /// </summary>
    public sealed class EditPanelSync
    {
        /// <summary>끌기가 끝나면 채우려고 미뤄 둔 요청이 있는지.</summary>
        public bool IsPending { get; private set; }

        /// <summary>패널 밖에서 설정이 바뀌었다.</summary>
        /// <param name="panelOpen">편집 패널이 열려 있는지.</param>
        /// <param name="dragging">사용자가 패널에서 무언가를 끌고 있는지(슬라이더, 가이드라인, 크기 손잡이, 펼친 목록).</param>
        /// <returns>지금 다시 채울 차례면 true. 끄는 중이면 미뤄 두고 false.</returns>
        public bool Request(bool panelOpen, bool dragging)
        {
            if (!panelOpen)
            {
                IsPending = false;
                return false;
            }
            IsPending = dragging;
            return !dragging;
        }

        /// <summary>사용자가 끌던 것을 놓았다.</summary>
        /// <returns>미뤄 둔 요청이 있고 지금 채울 수 있으면 true. 아직 다른 것을 끌고 있으면 계속 미룬다.</returns>
        public bool Released(bool panelOpen, bool dragging)
        {
            if (!IsPending) return false;
            return Request(panelOpen, dragging);
        }

        /// <summary>패널이 스스로 다시 채웠거나(열 때) 닫혔다. 미뤄 둔 요청은 버린다.</summary>
        public void Clear() => IsPending = false;
    }
}
