// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Collections.Frozen;
using System.Diagnostics;

namespace SoundVisualizer.Ai
{
    /// <summary>
    /// 실시간 AI 분류(Android 의 RealtimeAiPipeline.kt). 캡처 스레드가 넣은 소리를 배경 스레드가 250ms 마다 꺼내
    /// 링 → 16kHz → Qualcomm 로그멜 → YAMNet → 투표·안전 단서·후처리(<see cref="AiClassificationStage"/>) 를 거쳐 확정 결과를 게시한다.
    ///
    /// 스레드:
    /// - <see cref="Ingest"/> 는 캡처 스레드 하나에서만 부른다. 링에 복사만 하고 바로 돌아온다.
    /// - <see cref="LastClassification"/> 은 어느 스레드에서나 부른다. 불변 결과의 참조 하나를 잠금 없이 읽는다.
    /// - <see cref="Start"/>·<see cref="Stop"/>·<see cref="Dispose"/> 는 UI 스레드 등 어디서나 부른다. 돌고 있는 추론을 최대 1초 기다린다.
    /// - 틱은 이 클래스가 띄우는 배경 스레드 "SV-AiTick" 에서 돈다(Android 는 Dispatchers.Default 코루틴).
    /// 시각은 모두 단조 증가하는 밀리초 하나(<see cref="Environment.TickCount64"/>)로 잰다.
    ///
    /// 말소리 미리보기(Android #422, <see cref="SpeechPreviewEnabled"/>): 정규 추론이 첫 대화음 후보(확정까지 한 틱 남음)를 내면, 그 뒤에
    /// 새로 들어온 소리를 배경 스레드 "SV-SileroPreview" 의 Silero VAD 가 확인해 화면에만 대화음을 한 박자 먼저 보인다
    /// (<see cref="LastDisplayClassification"/>). 정규 결과(<see cref="LastClassification"/>)·위협음 판단·사용자 지정은 바꾸지 않는다.
    /// 정규 위협음이나 다음 정규 결과가 늘 이기고, 사용자 지정이 하나라도 있으면 미리보기를 쓰지 않는다. Silero 를 불러오거나 돌리다
    /// 실패해도 정규 분류는 그대로 돈다.
    ///
    /// 결과 알림(Android 의 onClassificationChanged, #412): 정규 결과를 새로 게시하거나 지울 때(틱 스레드), 미리보기를 게시할 때(미리보기
    /// 스레드) 만들 때 받은 <c>classificationChanged</c> 를 부른다. 개발자 HUD 가 100ms 타이머를 기다리지 않고 바로 읽게 하는 깨우기일 뿐이고
    /// 값은 싣지 않는다. 받는 쪽은 <see cref="LastDisplayClassification"/> 을 다시 읽는다. 시작·멈춤·닫기와 미리보기 만료는 알리지 않는다
    /// (부르는 쪽이 아는 일이거나 시간이 지나 생기는 일이라 타이머가 읽는다).
    ///
    /// PC 에서 바꾼 것:
    /// - <see cref="Stop"/> 이 돌고 있는 틱을 기다리고 마지막 결과를 바로 숨기며, <see cref="Start"/> 는 앞 틱이 끝난 뒤에 비운다.
    ///   Android 서비스는 늦게 끝난 추론이 비운 뒤의 결과를 덮지 않도록 읽는 쪽을 떼고 AI 재시작을 미뤘는데, 여기서는 그럴 필요가 없다.
    /// - <see cref="Dispose"/> 가 1초 안에 추론을 기다리지 못하면 Android 는 세션을 버리지만(누수), 여기서는 그 추론이 끝나면서 닫는다.
    ///   Silero 스레드도 같은 1초 안에서 끝나기를 기다린다(Android 는 기다리지 않는다). 못 기다리면 그 스레드가 끝나며 세션을 닫는다.
    /// - 미리보기 시각은 <see cref="Stopwatch"/> 의 단조 나노초로 잰다(Android 의 elapsedRealtimeNanos). 틱의 밀리초 시계(15.6ms 단위)로는
    ///   250·350ms 창이 한 프레임씩 흔들린다.
    /// </summary>
    public sealed class RealtimeAiPipeline : IDisposable
    {
        /// <summary>틱 시작에서 다음 시작까지(#379).</summary>
        public const long PredictIntervalMs = 250;

        /// <summary>추론이 150ms 넘게 걸리는 느린 PC 에서도 쉬지 않고 연달아 돌지 않게 하는 최소 휴식.</summary>
        internal const long MinTickRestMs = 100;

        /// <summary>Stop·Dispose 가 돌고 있는 추론을 기다리는 한도. UI 스레드를 오래 붙잡지 않는다(Android 는 메인 스레드 ANR).</summary>
        internal const int CloseWaitMs = 1000;

        /// <summary>
        /// 말소리 미리보기를 켤지(Android 의 BuildConfig.AI_SILERO_PREVIEW_ENABLED, v1.8.0 부터 기본 켬). 사용자 설정은 없다.
        /// <see cref="Create"/> 의 기본값이다.
        /// </summary>
        public const bool SpeechPreviewEnabled = true;

        /// <summary>정규 추론이 소리를 가져간 때부터 이 시간 안에(다음 틱 무렵까지) Silero 가 확인해야 미리보기를 받아들인다.</summary>
        internal const long SpeechPreviewAcceptNs = 250_000_000L;

        /// <summary>
        /// 정규 추론이 소리를 가져간 때부터 이 시간이 지나면 미리보기가 사라진다. 다음 정규 결과가 오면 그 전에라도 사라진다.
        /// Android 는 275ms 다(정확한 250ms 주기 + 추론 시간). PC 의 틱은 15.6ms 단위 시계와 대기로 재서 실제 주기가 255~270ms 이고,
        /// 275ms 로는 다음 정규 결과가 오기 전에 미리보기가 먼저 사라져 사람 소리 → 환경음 → 사람 소리로 깜빡인다(이 PC 에서 틱의 7~9%).
        /// 그래서 주기 250ms 에 시계 오차 두 번(31ms)과 느린 추론·바쁜 CPU 의 여유를 더해 350ms 로 둔다. 다음 정규 결과·위협음·사용자 지정은
        /// 부모 검사로 여전히 바로 이기므로, 달라지는 것은 다음 틱이 무음으로 건너뛰어질 때 미리보기가 75ms 더 남는 것뿐이다.
        /// </summary>
        internal const long SpeechPreviewExpiryNs = 350_000_000L;

        private const string Tag = nameof(RealtimeAiPipeline);
        private const long FailureLogIntervalMs = 2000;
        private const long NoTimestamp = long.MinValue;

        private static readonly IReadOnlyDictionary<string, string> NoOverrides = FrozenDictionary<string, string>.Empty;

        private readonly AiAudioBuffer _buffer;
        private readonly AiCaptureInferenceGate _gate;
        private readonly AiClassificationStage _stage;
        private readonly QualcommSourceAudioPreprocessor _frontend = new();
        private readonly IYamnetModel _model;
        private readonly Func<IReadOnlyDictionary<string, string>> _mappingOverrides;
        private readonly Action<Exception>? _tickFailed;
        private readonly Action? _classificationChanged;
        private int _classificationChangedFailed;
        private readonly Func<long> _clock;
        private readonly Func<long, CancellationToken, bool> _waitForNextTick;
        private readonly bool _runScheduler;

        // 말소리 미리보기. 스레드가 없으면(끔) 후보도 만들지 않고 화면용 결과는 늘 정규 결과다.
        private readonly Func<long> _nanoClock;
        private readonly SileroPreviewGate<SpeechParent> _speechGate = new();
        private readonly SileroStreamWorker? _speechWorker;
        private DisplayPreview? _displayPreview;

        // 추론을 한 번에 하나로 줄 세운다. 모델·후처리·작업 버퍼는 이 잠금 안에서만 쓴다.
        private readonly object _inferLock = new();
        // Start·Stop·Dispose 끼리의 순서를 지킨다. 추론 잠금을 기다리는 동안에도 잡고 있으므로 틱은 이 잠금을 잡지 않는다.
        private readonly object _lifecycleLock = new();
        private Thread? _schedulerThread;
        private CancellationTokenSource? _schedulerStop;
        private bool _running;
        private int _closed;
        private bool _modelDisposed;

        private long _inferenceExecutions;
        private long _inferenceCompletions;
        private long _silenceSkippedTicks;
        private long _lastFailureLogMs = NoTimestamp;

        private readonly float[] _captureScratch;
        private readonly float[] _mono16k = new float[CaptureAudioMath.RequiredMono16kSamples];
        private readonly float[] _logMel = new float[QualcommSourceAudioPreprocessor.LogMelSize];
        private readonly float[] _logits = new float[YamnetInference.NumClasses];
        private readonly float[] _probabilities = new float[YamnetInference.NumClasses];

        /// <param name="captureSampleRate">캡처 레이트. 링·리샘플러가 이 레이트로 만들어지므로 장치 형식이 바뀌면 새로 만든다.</param>
        /// <param name="channels">인터리브 채널 수(1 이상).</param>
        /// <param name="model">파이프라인이 넘겨받아 <see cref="Dispose"/> 에서 닫는다.</param>
        /// <param name="classes">YAMNet 클래스 521개.</param>
        /// <param name="mappingOverrides">지금의 사용자 지정. null 이면 늘 비어 있다.</param>
        /// <param name="tickFailed">틱 실패를 받을 곳. null 이면 <see cref="AppLog.Warn"/> 으로 2초에 한 번까지 남긴다.</param>
        /// <param name="clock">단조 밀리초. 테스트가 시각을 정한다. 기본은 <see cref="Environment.TickCount64"/>.</param>
        /// <param name="waitForNextTick">다음 틱까지 쉰다. 멈추라는 신호를 받으면 false. 테스트가 기다리지 않고 한 걸음씩 돌린다.</param>
        /// <param name="runScheduler">false 면 <see cref="Start"/> 가 상태만 비우고 스레드를 띄우지 않는다. 테스트가 <see cref="RunTickOnce"/> 를 직접 부른다.</param>
        /// <param name="speechModel">
        /// 말소리 미리보기의 Silero 를 만든다. 있으면 미리보기 스레드를 띄우고 그 스레드 안에서 한 번 부른다. null 이면 미리보기를 끈다
        /// (이 생성자로 만드는 테스트의 기본). 앱은 <see cref="Create"/> 가 넣는다.
        /// </param>
        /// <param name="nanoClock">미리보기 시각을 재는 단조 나노초. 테스트가 시각을 정한다. 기본은 <see cref="Stopwatch"/>.</param>
        /// <param name="classificationChanged">화면용 결과가 바뀌었을 때 틱 스레드나 미리보기 스레드에서 부른다(<see cref="Create"/> 참고).</param>
        internal RealtimeAiPipeline(
            int captureSampleRate,
            int channels,
            IYamnetModel model,
            YamnetClassMap classes,
            Func<IReadOnlyDictionary<string, string>>? mappingOverrides = null,
            Action<Exception>? tickFailed = null,
            Func<long>? clock = null,
            Func<long, CancellationToken, bool>? waitForNextTick = null,
            bool runScheduler = true,
            Func<ISileroModel>? speechModel = null,
            Func<long>? nanoClock = null,
            Action? classificationChanged = null)
        {
            ValidateArguments(captureSampleRate, channels, classes);
            ArgumentNullException.ThrowIfNull(model);
            _buffer = new AiAudioBuffer(captureSampleRate, channels);
            _gate = new AiCaptureInferenceGate(_buffer);
            _stage = new AiClassificationStage(classes);
            _model = model;
            _mappingOverrides = mappingOverrides ?? (static () => NoOverrides);
            _tickFailed = tickFailed;
            _classificationChanged = classificationChanged;
            _clock = clock ?? (static () => Environment.TickCount64);
            _waitForNextTick = waitForNextTick ?? WaitForNextTick;
            _runScheduler = runScheduler;
            _captureScratch = new float[CaptureAudioMath.CaptureSamplesForOneYamnetWindow(captureSampleRate)];
            _nanoClock = nanoClock ?? MonotonicNanoseconds;
            // 스레드는 맨 끝에 띄운다. 앞에서 실패하면 띄운 스레드가 남지 않는다. 덩어리를 보는 것은 Start 뒤에 소리가 들어온 다음이다.
            if (speechModel is not null)
            {
                _speechWorker = new SileroStreamWorker(_buffer, speechModel, ClearSpeechPreview, OnSpeechBlock, _nanoClock);
            }
        }

        /// <summary>
        /// 모델을 불러와 파이프라인을 만든다. 아직 돌지 않으므로 <see cref="Start"/> 를 부른다. 모델 확인과 세션 생성에 수십 ms 가
        /// 걸리므로 UI 스레드가 아닌 곳에서 부른다(Android 의 SV-AiInit). 실패하면 앱은 AI 없이 화면만 돌고 "AI 를 쓸 수 없음" 을 알린다.
        /// </summary>
        /// <param name="captureSampleRate">캡처 레이트(<see cref="AiCaptureSampleRatePolicy.IsSupportedForAi"/>).</param>
        /// <param name="channels">캡처 버퍼의 인터리브 채널 수.</param>
        /// <param name="modelDirectory">yamnet.onnx 와 yamnet.data 가 있는 폴더.</param>
        /// <param name="classes">yamnet_class_map.csv 에서 읽은 클래스 521개(<see cref="YamnetClassMap.Load(string)"/>).</param>
        /// <param name="mappingOverrides">
        /// 지금의 사용자 지정(영어 클래스 이름 → "ambient"/"speech"/"danger"). 틱마다, 그리고 <see cref="LastClassification"/> 을 부를 때마다
        /// 부르므로 여러 스레드에서 불려도 안전하고 가벼워야 한다(설정이 바뀔 때 통째로 바꿔 끼우는 불변 맵의 참조를 돌려주는 식).
        /// 예외를 던지면 안 된다. null 을 돌려주면 사용자 지정이 없는 것으로 본다.
        /// </param>
        /// <param name="tickFailed">틱 실패를 틱 스레드에서 받는다. null 이면 <see cref="AppLog.Warn"/> 으로 2초에 한 번까지 남긴다.</param>
        /// <param name="speechPreview">
        /// 말소리 미리보기를 켤지. 켜면 같은 폴더의 silero_vad.onnx 를 미리보기 스레드가 불러온다. 그 파일이 없거나 다르면 미리보기만 꺼지고
        /// (<see cref="AppLog.Warn"/>) 정규 분류는 돈다.
        /// </param>
        /// <param name="classificationChanged">
        /// 화면용 결과(<see cref="LastDisplayClassification"/>)가 바뀌었다는 깨우기. 정규 결과를 게시하거나 지운 틱 스레드, 미리보기를 게시한
        /// 미리보기 스레드에서 부른다. 값은 싣지 않으니 받는 쪽이 다시 읽는다. 추론 스레드를 붙잡지 않게 곧바로 돌아와야 한다
        /// (UI 스레드에 넘기기만 한다, <c>Diagnostics.AiHudRefresh</c>). 던지면 삼키고 처음 한 번만 <see cref="AppLog.Warn"/> 으로 남긴다.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">AI 가 받지 않는 캡처 레이트이거나 채널 수가 1 보다 작다.</exception>
        /// <exception cref="FileNotFoundException">모델 파일이 없다.</exception>
        /// <exception cref="InvalidDataException">모델 파일이 배포한 것과 다르거나 입출력이 YAMNet 계약과 다르다.</exception>
        /// <exception cref="Microsoft.ML.OnnxRuntime.OnnxRuntimeException">ONNX Runtime 이 세션을 만들지 못했다.</exception>
        public static RealtimeAiPipeline Create(
            int captureSampleRate,
            int channels,
            string modelDirectory,
            YamnetClassMap classes,
            Func<IReadOnlyDictionary<string, string>>? mappingOverrides = null,
            Action<Exception>? tickFailed = null,
            bool speechPreview = SpeechPreviewEnabled,
            Action? classificationChanged = null)
        {
            // 모델을 읽기 전에 거른다. 화면만 돌 레이트라면 15MB 를 해시하고 세션을 만들 까닭이 없다.
            ValidateArguments(captureSampleRate, channels, classes);
            var model = YamnetInference.Create(modelDirectory);
            try
            {
                string directory = Path.GetFullPath(modelDirectory);
                return new RealtimeAiPipeline(captureSampleRate, channels, model, classes, mappingOverrides, tickFailed,
                    speechModel: speechPreview ? () => SileroInference.Create(directory) : null,
                    classificationChanged: classificationChanged);
            }
            catch
            {
                model.Dispose();
                throw;
            }
        }

        public int SampleRate => _buffer.SampleRate;

        public int ChannelCount => _buffer.ChannelCount;

        /// <summary><see cref="Start"/> 뒤, <see cref="Stop"/>·<see cref="Dispose"/> 전.</summary>
        public bool IsRunning => Volatile.Read(ref _running) && !IsClosed;

        private bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <summary>
        /// 캡처 버퍼 하나(인터리브 float PCM, 채널 수 × 프레임)를 링에 복사한다. 추론은 하지 않고 바로 돌아온다.
        /// 캡처 스레드 하나에서만 부른다. 돌고 있지 않을 때(<see cref="Start"/> 전, <see cref="Stop"/> 뒤)와 닫힌 뒤에는 버린다.
        /// NAudio 의 바이트 버퍼는 <c>MemoryMarshal.Cast&lt;byte, float&gt;(e.Buffer.AsSpan(0, e.BytesRecorded))</c> 로 넘긴다.
        /// </summary>
        public void Ingest(ReadOnlySpan<float> interleaved)
        {
            if (!Volatile.Read(ref _running) || IsClosed) return;
            // 섞기 전의 원래 채널로 무음을 판단하고, 게이트가 닫혀 있어도 늘 링에 남긴다. 다시 열릴 때 그 앞의 소리까지 한 창으로 본다.
            _gate.IngestInterleaved(interleaved, _clock());
            // 링에 넣은 뒤 미리보기 스레드를 깨운다. Android 처럼 무음 게이트와 상관없이 소리가 들어오는 동안은 따라 읽는다.
            // Windows 루프백은 아무것도 재생하지 않으면 버퍼를 보내지 않으므로 그때는 깨우지 않는다.
            _speechWorker?.Signal();
        }

        /// <summary>
        /// 마지막 확정 결과. 화면은 null 이면 환경음으로 그린다. 어느 스레드에서나 부를 수 있고 잠그지 않는다.
        /// 돌고 있지 않거나(시작 전, 멈춘 뒤, 닫힌 뒤), 첫 추론이 아직 끝나지 않았거나, 결과를 만든 뒤 사용자 지정이 바뀌었으면
        /// (다음 추론 전이라도) null 이다. 필드끼리는 한 결과 안에서만 짝이 맞으므로 여러 필드를 쓰는 쪽은 한 번만 읽는다.
        /// </summary>
        public AiClassificationResult? LastClassification()
        {
            if (!Volatile.Read(ref _running) || IsClosed) return null;
            return _stage.LastClassification(CurrentOverrides());
        }

        /// <summary>
        /// 화면과 개발자 HUD 가 읽는 결과(Android 의 lastDisplayClassification). 보통은 <see cref="LastClassification"/> 과 같다.
        /// 말소리 미리보기를 받아들였고 정규 결과가 아직 그 부모(환경음) 그대로이며 사용자 지정이 없을 때만, 만료 전까지
        /// 대화음 미리보기(<see cref="AiClassificationResult.SpeechPreview"/>)를 준다. 정규 위협음이나 다음 정규 결과, 사용자 지정이
        /// 늘 이긴다. 진동·알림처럼 확정 판단이 필요한 곳은 쓰지 않는다(PC 에는 아직 없다). 어느 스레드에서나 부르고 잠그지 않는다.
        /// </summary>
        public AiClassificationResult? LastDisplayClassification()
        {
            var regular = LastClassification();
            if (regular is null) return null;
            var view = Volatile.Read(ref _displayPreview);
            if (view is null) return regular;
            if (_nanoClock() >= view.ExpiresNs)
            {
                Interlocked.CompareExchange(ref _displayPreview, null, view);
                return regular;
            }
            return ReferenceEquals(view.Parent, regular) &&
                regular.Coarse == SoundType.Ambient &&
                CurrentOverrides().Count == 0
                    ? view.Result
                    : regular;
        }

        /// <summary>
        /// 추론을 시작한다. 링·무음 게이트·히스테리시스·마지막 결과를 비우고 틱 스레드를 띄운다. 이미 돌고 있거나 닫혔으면 아무것도
        /// 하지 않는다. 앞선 실행의 추론이 아직 돌고 있으면 그 추론이 끝난 뒤에 비운다. 그래야 그 결과가 비운 뒤에 되살아나지 않는다.
        /// </summary>
        /// <exception cref="TimeoutException">앞선 실행의 추론이 1초 안에 끝나지 않았다.</exception>
        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (IsClosed || Volatile.Read(ref _running)) return;

                if (!Monitor.TryEnter(_inferLock, CloseWaitMs))
                    throw new TimeoutException("The previous AI inference did not finish within 1 s");
                try
                {
                    ResetStateLocked();
                }
                finally
                {
                    Monitor.Exit(_inferLock);
                }

                if (_runScheduler)
                {
                    var stop = new CancellationTokenSource();
                    CancellationToken token = stop.Token;
                    var thread = new Thread(() => RunScheduler(token))
                    {
                        IsBackground = true,
                        Name = "SV-AiTick",
                    };
                    try
                    {
                        thread.Start();
                    }
                    catch
                    {
                        stop.Dispose();
                        throw;
                    }
                    _schedulerThread = thread;
                    _schedulerStop = stop;
                }
                Volatile.Write(ref _running, true);
            }
        }

        /// <summary>
        /// 추론을 멈춘다. 마지막 결과는 바로 숨기고(<see cref="LastClassification"/> 이 null), 다음 틱을 막고, 돌고 있는 틱이 끝나기를
        /// 최대 1초 기다린다. 다시 <see cref="Start"/> 할 수 있다. 여러 번 불러도 된다.
        /// </summary>
        public void Stop()
        {
            StopScheduler(Environment.TickCount64 + CloseWaitMs);
            ClearSpeechPreview();
        }

        /// <summary>
        /// 멈추고 모델을 닫는다. 여러 번 불러도 된다. 닫은 뒤에는 추론하지 않고, <see cref="Ingest"/> 는 버리고,
        /// <see cref="LastClassification"/> 은 null 이다. 돌고 있는 추론은 끝나기를 기다리되 1초까지만 기다린다. 그 안에 끝나지 않으면
        /// 기다리지 않고 돌아오고, 그 추론이 끝나면서 모델을 닫는다. 어느 경우에도 쓰는 중인 세션을 닫지 않는다.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            long deadlineMs = Environment.TickCount64 + CloseWaitMs;
            // 미리보기 스레드에 먼저 알려 정규 추론을 기다리는 동안 함께 멈추게 한다. 끝나기를 기다리는 것은 맨 뒤에서 한다.
            _speechWorker?.RequestClose();
            StopScheduler(deadlineMs);
            ClearSpeechPreview();

            // 멈추기는 다음 틱만 막는다. 이미 모델 안에 들어간 추론이 있으면 끝나기를 기다린 뒤에 세션을 닫아야 해제한 메모리를 쓰지 않는다.
            if (Monitor.TryEnter(_inferLock, RemainingMs(deadlineMs)))
            {
                try
                {
                    DisposeModelLocked();
                }
                finally
                {
                    Monitor.Exit(_inferLock);
                }
            }
            else
            {
                AppLog.Warn(Tag, "Inference still running after 1 s; the running tick releases the YAMNet session when it finishes");
            }

            // Silero 세션은 미리보기 스레드가 끝나며 닫는다. 남은 시간 안에 끝나지 않으면 기다리지 않는다(그 스레드가 덩어리를 마치고 닫는다).
            if (_speechWorker is not null && !_speechWorker.Join(RemainingMs(deadlineMs)))
            {
                AppLog.Warn(Tag, "Silero preview still running after 1 s; it releases its session when the current block finishes");
            }
        }

        /// <summary>
        /// 다음 틱까지 쉴 시간(Kotlin remainingTickDelayMs, #379). 틱 시작에서 다음 시작까지를 <paramref name="intervalMs"/> 로 맞추려고
        /// 틱에 걸린 시간을 빼되, 오래 걸린 틱 뒤에도 <paramref name="minimumRestMs"/> 는 쉰다. 끝이 시작보다 앞이면 걸린 시간을 0 으로 본다.
        /// </summary>
        internal static long RemainingTickDelayMs(long intervalMs, long tickStartedAtMs, long tickFinishedAtMs, long minimumRestMs)
        {
            long tickDurationMs = Math.Max(0L, tickFinishedAtMs - tickStartedAtMs);
            return Math.Max(intervalMs - tickDurationMs, minimumRestMs);
        }

        /// <summary>
        /// 틱 한 번(Kotlin runTickInternal). 스케줄러가 <see cref="PredictIntervalMs"/> 마다 부르고, 테스트는 시각을 정해 직접 부른다.
        /// 예외를 던지지 않는다. 실패는 알리고 무음 게이트에 넘긴다.
        /// </summary>
        internal AiTickOutcome RunTickOnce(long nowMs)
        {
            if (IsClosed) return AiTickOutcome.Closed;
            if (!_buffer.HasEnoughForYamnetWindow()) return AiTickOutcome.NotEnoughAudio;
            // 앞 틱이 아직 돌고 있으면 기다리지 않고 건너뛴다(Kotlin 의 tryLock).
            if (!Monitor.TryEnter(_inferLock)) return AiTickOutcome.Busy;

            (AiTickOutcome Outcome, Exception? Failure, bool Changed) tick;
            try
            {
                tick = RunTickLocked(nowMs);
            }
            finally
            {
                // Dispose 가 기다리다 포기한 뒤에 끝난 틱이면 여기서 닫는다. 잠금 안이라 다른 추론과 겹치지 않는다.
                if (IsClosed) DisposeModelLocked();
                Monitor.Exit(_inferLock);
            }

            if (tick.Failure is not null) ReportTickFailure(tick.Failure);
            // 추론 잠금을 놓은 뒤에 알린다. 받는 쪽이 느리거나 이 파이프라인을 다시 불러도 다음 틱과 닫기가 잠금에서 기다리지 않는다.
            if (tick.Changed && !IsClosed) NotifyClassificationChanged();
            return tick.Outcome;
        }

        /// <summary>추론 횟수(테스트와 진단용). <see cref="Start"/> 가 0 으로 되돌린다.</summary>
        internal InferenceStats Stats => new(
            Interlocked.Read(ref _inferenceExecutions),
            Interlocked.Read(ref _inferenceCompletions),
            Interlocked.Read(ref _silenceSkippedTicks));

        /// <summary>마지막 추론의 16kHz 입력. 다음 틱이 덮어쓰므로 스케줄러를 돌리지 않는 테스트만 읽는다.</summary>
        internal ReadOnlySpan<float> Mono16kForTest => _mono16k;

        /// <summary>마지막 추론의 로그멜.</summary>
        internal ReadOnlySpan<float> LogMelForTest => _logMel;

        /// <summary>마지막 추론의 softmax 확률.</summary>
        internal ReadOnlySpan<float> ProbabilitiesForTest => _probabilities;

        /// <summary>닫기 경합 테스트가 틱을 링 검사에 세워 두는 데 쓴다.</summary>
        internal AiAudioBuffer AudioBufferForTest => _buffer;

        /// <summary>말소리 미리보기 스레드. 끄면 null.</summary>
        internal SileroStreamWorker? SpeechWorkerForTest => _speechWorker;

        /// <returns>Changed 는 이 틱이 정규 결과를 새로 게시했거나(완료) 매핑이 바뀌어 지웠는지다. 잠금을 놓은 뒤에 알린다.</returns>
        private (AiTickOutcome Outcome, Exception? Failure, bool Changed) RunTickLocked(long nowMs)
        {
            // 정규 추론이 시작되면 미리보기를 새로 받아들이지 않는다. 이미 받아들인 것은 만료나 다음 정규 결과까지 둔다(Android 와 같다).
            _speechGate.RegularStarted();
            // Dispose 가 잠금을 잡기 직전에 위의 검사를 지났을 수 있으므로 잠금 안에서 다시 본다.
            if (IsClosed) return (AiTickOutcome.Closed, null, false);

            bool cleared = false;
            try
            {
                // 무음이라 건너뛰는 틱에도 먼저 반영한다. 이전 매핑에서 확정한 위협음이 새 매핑으로 넘어가지 않게 한다.
                // 매핑이 바뀌어 마지막 결과를 지웠으면 그 결과의 미리보기도 지운다.
                if (_stage.UpdateMapping(CurrentOverrides()))
                {
                    ClearSpeechPreview();
                    cleared = true;
                }
            }
            catch (Exception ex)
            {
                return (AiTickOutcome.Failed, ex, false);
            }

            if (!_gate.IsInferenceOpen(nowMs))
            {
                Interlocked.Increment(ref _silenceSkippedTicks);
                return (AiTickOutcome.SkippedForSilence, null, cleared);
            }

            try
            {
                InferLocked();
            }
            catch (Exception ex)
            {
                _gate.OnInferenceFailed(nowMs);
                return (AiTickOutcome.Failed, ex, cleared);
            }
            // 결과를 게시한 뒤에만 완료로 센다. 닫기 경합 테스트가 잠깐 보이는 결과 대신 완료 수로 확인한다(Kotlin 과 같다).
            Interlocked.Increment(ref _inferenceCompletions);
            _gate.OnInferenceCompleted(nowMs);
            return (AiTickOutcome.Completed, null, true);
        }

        private void InferLocked()
        {
            Interlocked.Increment(ref _inferenceExecutions);
            // 미리보기 창(받아들임 250ms·만료 350ms)은 정규 추론이 소리를 가져가기 직전부터 잰다(Android 의 snapshotBootNs).
            long snapshotNs = _speechWorker is null ? 0L : _nanoClock();
            long started = Stopwatch.GetTimestamp();
            _buffer.CopyTailRightPadded(_captureScratch);
            CaptureAudioMath.ResampleMonoFloatTo16kCustom(_captureScratch, _buffer.SampleRate, _mono16k);
            _frontend.ComputeLogMelSpectrogram(_mono16k, _logMel);
            long preprocessed = Stopwatch.GetTimestamp();
            _model.Infer(_logMel, _logits, _probabilities);
            long inferred = Stopwatch.GetTimestamp();
            var result = _stage.Process(
                _probabilities,
                started,
                preprocessMs: Stopwatch.GetElapsedTime(started, preprocessed).TotalMilliseconds,
                yamnetMs: Stopwatch.GetElapsedTime(preprocessed, inferred).TotalMilliseconds);
            OnRegularResultPublished(result, snapshotNs);
        }

        /// <summary>
        /// 새 정규 결과를 게시한 직후(추론 잠금 안). 앞 결과의 미리보기와 후보를 버리고, 이번 프레임이 첫 대화음 후보면 새 후보를 건다.
        /// 후보는 지금까지 링에 들어온 소리 뒤의 새 소리만 본다. Android 는 결과를 저장하기 전에 비우지만, 여기서는 저장한 뒤에 비운다.
        /// 미리보기는 부모가 지금 정규 결과일 때만 보이므로(<see cref="LastDisplayClassification"/>) 그 사이에도 옛 미리보기가 보이지 않고,
        /// 저장하기 전에 비우면 그 사이 한 프레임 동안 미리보기 대신 부모(환경음)가 보일 수 있다.
        /// </summary>
        private void OnRegularResultPublished(AiClassificationResult result, long snapshotNs)
        {
            if (_speechWorker is null) return;
            SileroPreviewGate<SpeechParent>.Candidate? candidate = null;
            if (_stage.SpeechOnset is { } onset)
            {
                var cursor = _buffer.Cursor();
                candidate = new SileroPreviewGate<SpeechParent>.Candidate(
                    new SpeechParent(result, onset.Display, onset.Confidence),
                    FreshAfterFrame: cursor.Frames,
                    AcceptBeforeNs: snapshotNs + SpeechPreviewAcceptNs,
                    ExpiresNs: snapshotNs + SpeechPreviewExpiryNs,
                    Generation: cursor.Generation);
            }
            _speechGate.Regular(candidate);
            Volatile.Write(ref _displayPreview, null);
        }

        /// <summary>
        /// Silero 덩어리 하나의 결과(미리보기 스레드). 받아들였고, 그 사이 새 정규 결과나 사용자 지정이 생기지 않았고, 받아들임 마감 전이면
        /// 화면용 미리보기를 게시한다.
        /// </summary>
        private void OnSpeechBlock(long generation, long startFrame, long endFrame, float probability, long nowNs)
        {
            var accepted = _speechGate.Block(generation, startFrame, endFrame, probability, nowNs);
            if (accepted is null || IsClosed) return;
            var parent = accepted.Parent;
            if (!ReferenceEquals(LastClassification(), parent.Result) || CurrentOverrides().Count != 0) return;
            var preview = parent.Result.AsSpeechPreview(parent.Display, parent.Confidence, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (_nanoClock() < accepted.AcceptBeforeNs)
            {
                Volatile.Write(ref _displayPreview, new DisplayPreview(parent.Result, preview, accepted.ExpiresNs));
                // 미리보기는 다음 정규 결과가 오면 사라져 100ms 도 못 사는 일이 있다. 타이머만으로는 HUD 와 기록이 그것을 통째로 놓친다.
                NotifyClassificationChanged();
            }
        }

        /// <summary>
        /// 화면용 결과가 바뀌었다고 알린다. 틱 스레드(추론 잠금 밖)와 미리보기 스레드에서 부른다. 받는 쪽의 결함이 추론이나 미리보기를
        /// 멈추면 안 되므로 예외는 삼키고, 틱마다 같은 줄이 쌓이지 않게 처음 한 번만 남긴다.
        /// </summary>
        private void NotifyClassificationChanged()
        {
            if (_classificationChanged is null) return;
            try
            {
                _classificationChanged();
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _classificationChangedFailed, 1) == 0)
                {
                    AppLog.Warn(Tag, "Classification-changed callback threw", ex);
                }
            }
        }

        /// <summary>후보·받아들인 것·게시한 미리보기를 모두 버린다. 멈춤·초기화·매핑 변경·닫기, 미리보기 스레드가 밀린 소리를 버릴 때.</summary>
        private void ClearSpeechPreview()
        {
            _speechGate.Regular(null);
            Volatile.Write(ref _displayPreview, null);
        }

        private static readonly double NanosecondsPerTimestampTick = 1_000_000_000.0 / Stopwatch.Frequency;

        private static long MonotonicNanoseconds() => (long)(Stopwatch.GetTimestamp() * NanosecondsPerTimestampTick);

        private void RunScheduler(CancellationToken stop)
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    long startedMs = _clock();
                    try
                    {
                        RunTickOnce(startedMs);
                    }
                    catch (Exception ex)
                    {
                        // RunTickOnce 는 실패를 스스로 받아 알린다. 그래도 새어 나온 예외가 루프를 죽이지 않게 한 번 더 막는다.
                        AppLog.Warn(Tag, "AI tick failed", ex);
                    }
                    long delayMs = RemainingTickDelayMs(PredictIntervalMs, startedMs, _clock(), MinTickRestMs);
                    if (!_waitForNextTick(delayMs, stop)) return;
                }
            }
            catch (Exception ex)
            {
                // 여기까지 오는 것은 시계나 대기의 결함뿐이다. 앱을 죽이지 않고 AI 만 멈춘다.
                AppLog.Warn(Tag, "AI scheduler stopped unexpectedly", ex);
            }
        }

        private static bool WaitForNextTick(long delayMs, CancellationToken stop) =>
            !stop.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(delayMs));

        private void StopScheduler(long deadlineMs)
        {
            Thread? thread;
            CancellationTokenSource? stop;
            lock (_lifecycleLock)
            {
                Volatile.Write(ref _running, false);
                thread = _schedulerThread;
                stop = _schedulerStop;
                _schedulerThread = null;
                _schedulerStop = null;
                stop?.Cancel();
            }
            if (thread is null) return;
            // 틱 스레드 안(실패 알림 등)에서 불렸으면 자기 자신은 기다릴 수 없다. 스레드는 지금 틱을 마치고 스스로 빠져나간다.
            if (thread == Thread.CurrentThread) return;

            if (thread.Join(RemainingMs(deadlineMs)))
            {
                stop?.Dispose();
            }
            else
            {
                // 대기 핸들을 아직 쓰고 있을 수 있으므로 토큰은 버리지 않고 GC 에 맡긴다.
                AppLog.Warn(Tag, "AI tick is still running after 1 s; it stops after the current inference");
            }
        }

        private void ResetStateLocked()
        {
            _buffer.Reset();
            _gate.Reset();
            _stage.Reset();
            ClearSpeechPreview();
            Interlocked.Exchange(ref _inferenceExecutions, 0);
            Interlocked.Exchange(ref _inferenceCompletions, 0);
            Interlocked.Exchange(ref _silenceSkippedTicks, 0);
        }

        /// <summary>추론 잠금 안에서만 부른다. 그래서 추론과 겹치지 않고 두 번 닫지도 않는다.</summary>
        private void DisposeModelLocked()
        {
            if (_modelDisposed) return;
            _modelDisposed = true;
            try
            {
                _model.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn(Tag, "Failed to release the YAMNet session", ex);
            }
        }

        private void ReportTickFailure(Exception failure)
        {
            if (_tickFailed is not null)
            {
                try
                {
                    _tickFailed(failure);
                }
                catch (Exception callbackFailure)
                {
                    AppLog.Warn(Tag, "AI tick failure callback threw", callbackFailure);
                }
                return;
            }

            // 모델이 계속 실패하면 틱마다(초당 4번) 같은 줄이 쌓이므로 2초에 한 번만 남긴다.
            long now = Environment.TickCount64;
            long last = Interlocked.Read(ref _lastFailureLogMs);
            if (last != NoTimestamp && now - last < FailureLogIntervalMs) return;
            Interlocked.Exchange(ref _lastFailureLogMs, now);
            AppLog.Warn(Tag, "AI tick failed", failure);
        }

        private IReadOnlyDictionary<string, string> CurrentOverrides() => _mappingOverrides() ?? NoOverrides;

        private static int RemainingMs(long deadlineMs) =>
            (int)Math.Clamp(deadlineMs - Environment.TickCount64, 0L, CloseWaitMs);

        private static void ValidateArguments(int captureSampleRate, int channels, YamnetClassMap classes)
        {
            if (!AiCaptureSampleRatePolicy.IsSupportedForAi(captureSampleRate))
                throw new ArgumentOutOfRangeException(nameof(captureSampleRate), captureSampleRate, "Unsupported AI capture sample rate; run visual-only");
            if (channels < 1)
                throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be at least 1");
            ArgumentNullException.ThrowIfNull(classes);
            if (classes.Count != YamnetCoarseClassifier.NumClasses)
                throw new ArgumentException($"Expected {YamnetCoarseClassifier.NumClasses} class names, got {classes.Count}", nameof(classes));
        }

        /// <summary>추론 횟수. Executed 는 시작한 추론, Completed 는 결과를 게시하고 끝난 추론이다.</summary>
        internal readonly record struct InferenceStats(long Executed, long Completed, long SkippedForSilence);

        /// <summary>미리보기 후보의 부모: 그 정규 결과(환경음)와, 받아들이면 보일 대화음 이름·확신도.</summary>
        private sealed record SpeechParent(AiClassificationResult Result, string Display, float Confidence);

        /// <summary>게시한 미리보기. <paramref name="Parent"/> 가 지금 정규 결과일 때만, <paramref name="ExpiresNs"/> 전까지 보인다.</summary>
        private sealed record DisplayPreview(AiClassificationResult Parent, AiClassificationResult Result, long ExpiresNs);
    }

    /// <summary>틱 한 번이 어떻게 끝났는지. 테스트와 진단용이다.</summary>
    internal enum AiTickOutcome
    {
        /// <summary>닫힌 뒤라 아무것도 하지 않았다.</summary>
        Closed,

        /// <summary>링에 한 창(0.975초)이 아직 모이지 않았다. 사용자 지정도 반영하지 않았다(Kotlin 과 같다).</summary>
        NotEnoughAudio,

        /// <summary>앞 틱이 아직 돌고 있어 건너뛰었다.</summary>
        Busy,

        /// <summary>무음 게이트가 닫혀 추론을 건너뛰었다. 사용자 지정은 반영했다.</summary>
        SkippedForSilence,

        /// <summary>추론하고 결과를 게시했다.</summary>
        Completed,

        /// <summary>사용자 지정을 읽거나 추론하다 실패했다. 알렸고, 추론 실패는 무음 게이트에도 넘겼다.</summary>
        Failed,
    }
}
