// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Ai
{
    /// <summary>Silero 덩어리 하나의 결과. 캡처 프레임 [<paramref name="startFrame"/>, <paramref name="endFrame"/>) 를 본 확률이다.</summary>
    internal delegate void SileroBlockHandler(long generation, long startFrame, long endFrame, float probability, long nowNs);

    /// <summary>
    /// AI 링의 소리를 16kHz 512샘플 덩어리로 차례로 Silero 에 넣는 배경 스레드 하나(Android 의 SileroStreamWorker.kt, #422).
    ///
    /// - 캡처 스레드는 링에 넣은 뒤 <see cref="Signal"/> 만 부른다. 깨우기는 하나로 합쳐지고, 캡처 PCM 을 따로 복사하거나 큐에 쌓지 않는다.
    ///   이 스레드가 링에서 덩어리에 필요한 범위만 꺼내(<see cref="AiAudioBuffer.CopyRange"/>) 정규 리샘플러와 같은 계수로 16kHz 로 바꾼다.
    /// - 한 번 깰 때 덩어리를 다섯 개까지만 본다. 밀린 것이 5덩어리(160ms)를 넘으면 버리고 가장 최근 쪽에서 다시 시작하며, 이어지지 않으므로
    ///   모델 상태를 비우고 <c>discontinuity</c> 를 부른다. 정규 추론은 이 스레드를 기다리지 않는다.
    /// - 링의 세대가 바뀌면(캡처를 다시 시작) 처음부터 다시 센다. 덩어리를 본 뒤에도 세대가 그대로일 때만 결과를 넘긴다.
    /// - 모델은 이 스레드가 만들고 쓰고 닫는다. 만들거나 돌리다 실패하면 미리보기만 꺼지고(<see cref="Failed"/>) 정규 분류는 그대로 돈다.
    ///
    /// PC 에서 바꾼 것: 닫을 때 스레드가 끝나기를 기다린다(<see cref="Join"/>, 파이프라인이 1초까지). Android 는 기다리지 않고 스레드가
    /// 스스로 끝나며 세션을 놓게 둔다. 여기서도 기다리다 시간이 다 되면 그렇게 둔다.
    /// 88.2·96·176.4·192kHz 는 정규 리샘플러처럼 FIR 탭을 레이트에 비례해 늘린 범위를 꺼낸다(<see cref="CaptureAudioMath.StreamSourceRange"/>).
    /// </summary>
    internal sealed class SileroStreamWorker : IDisposable
    {
        /// <summary>한 번 깰 때 보는 덩어리 수의 한도.</summary>
        internal const int MaxBlocksPerWake = 5;

        /// <summary>이만큼(16kHz 샘플, 5덩어리 = 160ms) 넘게 밀리면 버리고 가장 최근 쪽에서 다시 시작한다.</summary>
        internal const long MaxBacklogSamples = SileroInference.BlockSize * 5L;

        private const string Tag = nameof(SileroStreamWorker);

        private readonly AiAudioBuffer _buffer;
        private readonly Func<ISileroModel> _modelFactory;
        private readonly Action _discontinuity;
        private readonly SileroBlockHandler _block;
        private readonly Func<long> _nanoClock;
        // 깨우기. AvailableWaitHandle 을 쓰지 않아 놓을 네이티브 자원이 없다. 캡처 스레드가 닫기와 엇갈려 Release 해도 터지지 않게
        // 버리지(Dispose) 않는다.
        private readonly SemaphoreSlim _wake = new(0);
        private readonly Thread _thread;
        private int _closed;
        private int _queued;
        private int _failed;
        private long _calls;
        private long _resets;
        private long _next;

        /// <param name="buffer">읽을 AI 링. 레이트는 만들 때 정해진다.</param>
        /// <param name="modelFactory">이 스레드 안에서 한 번 부른다. 만든 모델은 이 스레드가 닫는다.</param>
        /// <param name="discontinuity">밀린 것을 버리고 건너뛸 때 이 스레드에서 부른다. 걸린 후보를 비우는 데 쓴다.</param>
        /// <param name="block">덩어리 하나를 본 뒤 이 스레드에서 부른다. 오래 붙잡지 않는다.</param>
        /// <param name="nanoClock">단조 나노초. 덩어리를 본 시각을 잰다.</param>
        public SileroStreamWorker(
            AiAudioBuffer buffer,
            Func<ISileroModel> modelFactory,
            Action discontinuity,
            SileroBlockHandler block,
            Func<long> nanoClock)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _modelFactory = modelFactory ?? throw new ArgumentNullException(nameof(modelFactory));
            _discontinuity = discontinuity ?? throw new ArgumentNullException(nameof(discontinuity));
            _block = block ?? throw new ArgumentNullException(nameof(block));
            _nanoClock = nanoClock ?? throw new ArgumentNullException(nameof(nanoClock));
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "SV-SileroPreview",
                // 우선순위는 낮추지 않는다(Android 는 THREAD_PRIORITY_BACKGROUND). 이 스레드는 링 버퍼 잠금과 깨우기 신호를 캡처 스레드와
                // 나눠 쓰는데, Windows 는 우선순위가 엄격하고 Monitor 에 우선순위 상속이 없다. 게임이 모든 코어를 채운 동안 낮은 우선순위로
                // 잠금을 잡은 채 밀려나면 캡처 스레드(소리 내보내기와 화면 크기 계산도 한다)가 함께 멈춘다. 일의 양은 코어의 1% 남짓이다.
            };
            _thread.Start();
        }

        /// <summary>Silero 를 돌린 덩어리 수(테스트와 진단용).</summary>
        public long Calls => Interlocked.Read(ref _calls);

        /// <summary>모델 상태를 비운 횟수. 세대가 바뀌거나(처음 한 번 포함) 밀린 것을 버릴 때 센다.</summary>
        public long Resets => Interlocked.Read(ref _resets);

        /// <summary>모델을 만들거나 돌리다 실패해 미리보기를 껐는지. 정규 분류는 상관없이 돈다.</summary>
        public bool Failed => Volatile.Read(ref _failed) != 0;

        /// <summary>다음에 볼 덩어리의 시작(지금 세대의 16kHz 샘플 번호). 테스트가 따라잡았는지 기다리는 데 쓴다.</summary>
        internal long NextOutputSample => Interlocked.Read(ref _next);

        /// <summary>스레드가 아직 도는지(테스트용).</summary>
        internal bool IsAlive => _thread.IsAlive;

        /// <summary>덩어리를 Silero 에 넣기 직전에 이 스레드에서 부른다. 테스트가 스레드를 붙잡아 둔다.</summary>
        internal Action? BeforeScoreForTest { get; set; }

        /// <summary>링에 새 소리가 들어왔다. 캡처 스레드에서 부른다. 잠그지 않고, 이미 깨워 두었으면 아무것도 하지 않는다.</summary>
        public void Signal()
        {
            if (Volatile.Read(ref _closed) != 0 || Volatile.Read(ref _failed) != 0) return;
            if (Interlocked.CompareExchange(ref _queued, 1, 0) == 0) _wake.Release();
        }

        /// <summary>멈추라고 알린다. 기다리지 않는다. 지금 보고 있는 덩어리를 마치면 끝나고 모델을 닫는다. 여러 번 불러도 된다.</summary>
        public void RequestClose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) _wake.Release();
        }

        /// <summary>스레드가 끝나기를 <paramref name="timeoutMs"/> 까지 기다린다. 끝났으면 true. 이 스레드 안에서 부르면 기다리지 않는다.</summary>
        public bool Join(int timeoutMs)
        {
            if (Thread.CurrentThread == _thread) return false;
            return _thread.Join(Math.Max(0, timeoutMs));
        }

        /// <summary>멈추고 1초까지 기다린다(테스트용 using).</summary>
        public void Dispose()
        {
            RequestClose();
            Join(RealtimeAiPipeline.CloseWaitMs);
        }

        private bool IsClosed => Volatile.Read(ref _closed) != 0;

        private void Run()
        {
            ISileroModel? model = null;
            try
            {
                model = _modelFactory();
                int rate = _buffer.SampleRate;
                long epoch = -1;
                long next = 0;
                var pcm = new float[CaptureAudioMath.StreamSourceCapacity(SileroInference.BlockSize, rate)];
                var output = new float[SileroInference.BlockSize];
                while (!IsClosed)
                {
                    _wake.Wait();
                    Volatile.Write(ref _queued, 0);
                    if (IsClosed) break;

                    var cursor = _buffer.Cursor();
                    if (epoch != cursor.Generation)
                    {
                        epoch = cursor.Generation;
                        next = 0;
                        model.Reset();
                        Interlocked.Increment(ref _resets);
                    }
                    // CPU 가 따라가지 못하면 밀린 것을 버리고 순환 상태도 비운다. 이어지지 않은 소리를 이어진 것처럼 세지 않는다.
                    long newest = CaptureAudioMath.NewestStreamBlockStart(cursor.Frames, SileroInference.BlockSize, rate);
                    if (newest - next > MaxBacklogSamples)
                    {
                        next = newest;
                        model.Reset();
                        Interlocked.Increment(ref _resets);
                        _discontinuity();
                    }
                    Interlocked.Exchange(ref _next, next);

                    int processed = 0;
                    while (!IsClosed && processed++ < MaxBlocksPerWake)
                    {
                        var (first, end) = CaptureAudioMath.StreamSourceRange(next, SileroInference.BlockSize, rate);
                        var source = pcm.AsSpan(0, (int)(end - first));
                        if (!_buffer.CopyRange(epoch, first, source)) break; // 아직 다 오지 않았거나 이미 밀려났다
                        CaptureAudioMath.ResampleStreamBlock(source, first, next, rate, output);
                        BeforeScoreForTest?.Invoke();
                        float probability = model.Score(output);
                        Interlocked.Increment(ref _calls);
                        // 보는 사이 캡처를 다시 시작했으면 옛 세대의 결과는 넘기지 않는다.
                        if (_buffer.Cursor().Generation == epoch && !IsClosed)
                        {
                            _block(epoch,
                                next * rate / CaptureAudioMath.TargetSampleRate,
                                (next + SileroInference.BlockSize) * rate / CaptureAudioMath.TargetSampleRate,
                                probability,
                                _nanoClock());
                        }
                        next += SileroInference.BlockSize;
                        Interlocked.Exchange(ref _next, next);
                    }
                }
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _failed, 1);
                AppLog.Warn(Tag, "Silero speech preview disabled; the regular AI keeps running", ex);
            }
            finally
            {
                // 모델은 이 스레드만 쓰므로 여기서 닫는다. 닫는 쪽이 기다리다 포기해도 이 스레드가 끝나며 놓는다.
                try
                {
                    model?.Dispose();
                }
                catch (Exception ex)
                {
                    AppLog.Warn(Tag, "Failed to release the Silero session", ex);
                }
            }
        }
    }
}
