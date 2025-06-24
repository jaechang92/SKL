using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using System;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 음악 전용 매니저 (페이드, 크로스페이드, 씬 전환 음악 등)
    /// 단일 책임: 복잡한 음악 제어 및 관리
    /// </summary>
    public class MusicManager : SingletonManager<MusicManager>, IInitializableAsync
    {
        [Header("Music Source")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioSource _crossfadeSource; // 크로스페이드용 보조 소스

        [Header("Music Settings")]
        [SerializeField] private float _musicVolume = 1.0f;
        [SerializeField] private bool _loopByDefault = true;

        [Header("Fade Settings")]
        [SerializeField] private float _defaultFadeDuration = 1.0f;
        [SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Crossfade Settings")]
        [SerializeField] private float _crossfadeDuration = 2.0f;
        [SerializeField] private AnimationCurve _crossfadeCurve = AnimationCurve.Linear(0, 0, 1, 1);

        // IManagerInitializable 구현
        public string Name => "MusicManager";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Normal;
        public bool IsInitialized { get; private set; } = false;

        // 상태 관리
        private AudioClip _currentMusicClip;
        private bool _isMusicFading = false;
        private bool _isCrossfading = false;
        private float _masterVolume = 1.0f; // AudioManager로부터 전달받음

        // 이벤트 (옵저버 패턴)
        public event Action<float> OnMusicVolumeChanged;
        public event Action<AudioClip> OnMusicChanged;
        public event Action OnMusicStarted;
        public event Action OnMusicStopped;
        public event Action OnCrossfadeStarted;
        public event Action OnCrossfadeCompleted;

        #region IManagerInitializable 구현

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized) return;

            try
            {
                JCDebug.Log("[MusicManager] 음악 시스템 초기화 시작");

                // 1. 음악 소스 생성
                await CreateMusicSourcesAsync(cancellationToken);

                // 2. 음악 설정 초기화
                InitializeMusicSettings();

                IsInitialized = true;
                JCDebug.Log("[MusicManager] 음악 시스템 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[MusicManager] 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[MusicManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        #endregion

        #region 초기화 메서드들

        private async UniTask CreateMusicSourcesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Main Music Source 생성
            if (_musicSource == null)
            {
                GameObject musicObj = new GameObject("MusicSource");
                musicObj.transform.SetParent(transform);
                _musicSource = musicObj.AddComponent<AudioSource>();
                _musicSource.loop = _loopByDefault;
                _musicSource.playOnAwake = false;
                _musicSource.priority = 64;

                // 프레임 분산을 위해 잠시 대기
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            // Crossfade Music Source 생성
            if (_crossfadeSource == null)
            {
                GameObject crossfadeObj = new GameObject("CrossfadeSource");
                crossfadeObj.transform.SetParent(transform);
                _crossfadeSource = crossfadeObj.AddComponent<AudioSource>();
                _crossfadeSource.loop = _loopByDefault;
                _crossfadeSource.playOnAwake = false;
                _crossfadeSource.priority = 64;
            }

            JCDebug.Log("[MusicManager] 음악 소스 생성 완료");
        }

        private void InitializeMusicSettings()
        {
            UpdateMusicVolume();
        }

        #endregion

        #region Volume 제어

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);
            UpdateMusicVolume();
            OnMusicVolumeChanged?.Invoke(_musicVolume);
        }

        /// <summary>
        /// AudioManager로부터 마스터 볼륨 업데이트 받음
        /// </summary>
        public void OnMasterVolumeUpdated(float masterVolume)
        {
            _masterVolume = masterVolume;
            UpdateMusicVolume();
        }

        private void UpdateMusicVolume()
        {
            float finalVolume = _masterVolume * _musicVolume;

            if (_musicSource != null)
                _musicSource.volume = finalVolume;

            if (_crossfadeSource != null)
                _crossfadeSource.volume = finalVolume;
        }

        #endregion

        #region 기본 음악 제어

        /// <summary>
        /// 음악 재생 (페이드인 옵션)
        /// </summary>
        public async UniTask PlayMusicAsync(AudioClip clip, bool fadeIn = true, bool loop = true, CancellationToken cancellationToken = default)
        {
            if (clip == null)
            {
                JCDebug.Log("[MusicManager] 음악 클립이 null입니다.", JCDebug.LogLevel.Warning);
                return;
            }

            // 같은 클립이 이미 재생 중이면 무시
            if (_currentMusicClip == clip && _musicSource.isPlaying)
            {
                JCDebug.Log($"[MusicManager] 이미 재생 중인 음악: {clip.name}");
                return;
            }

            try
            {
                // 현재 음악이 재생 중이고 페이드인이 요청된 경우 크로스페이드
                if (_musicSource.isPlaying && fadeIn)
                {
                    await CrossfadeMusicAsync(clip, loop, cancellationToken);
                }
                else
                {
                    // 직접 재생
                    _musicSource.clip = clip;
                    _musicSource.loop = loop;
                    _musicSource.Play();
                    _currentMusicClip = clip;

                    if (fadeIn)
                    {
                        await FadeInMusicAsync(_defaultFadeDuration, cancellationToken);
                    }
                }

                OnMusicChanged?.Invoke(clip);
                OnMusicStarted?.Invoke();
                JCDebug.Log($"[MusicManager] 음악 재생: {clip.name}");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[MusicManager] 음악 재생이 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
        }

        /// <summary>
        /// 음악 정지 (페이드아웃 옵션)
        /// </summary>
        public async UniTask StopMusicAsync(bool fadeOut = true, CancellationToken cancellationToken = default)
        {
            if (!_musicSource.isPlaying) return;

            try
            {
                if (fadeOut)
                {
                    await FadeOutMusicAsync(_defaultFadeDuration, cancellationToken);
                }
                else
                {
                    _musicSource.Stop();
                }

                _currentMusicClip = null;
                OnMusicStopped?.Invoke();
                JCDebug.Log("[MusicManager] 음악 정지");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[MusicManager] 음악 정지가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
        }

        /// <summary>
        /// 음악 일시정지/재개
        /// </summary>
        public void PauseMusic()
        {
            if (_musicSource.isPlaying)
            {
                _musicSource.Pause();
                JCDebug.Log("[MusicManager] 음악 일시정지");
            }
        }

        public void ResumeMusic()
        {
            if (!_musicSource.isPlaying && _musicSource.time > 0)
            {
                _musicSource.UnPause();
                JCDebug.Log("[MusicManager] 음악 재개");
            }
        }

        #endregion

        #region 고급 음악 제어 (페이드, 크로스페이드)

        /// <summary>
        /// 음악 크로스페이드 (두 음악 간 부드러운 전환)
        /// </summary>
        public async UniTask CrossfadeMusicAsync(AudioClip newClip, bool loop = true, CancellationToken cancellationToken = default)
        {
            if (newClip == null || _isCrossfading) return;

            _isCrossfading = true;
            OnCrossfadeStarted?.Invoke();

            try
            {
                // 크로스페이드 소스 설정
                _crossfadeSource.clip = newClip;
                _crossfadeSource.loop = loop;
                _crossfadeSource.volume = 0f;
                _crossfadeSource.Play();

                float targetVolume = _masterVolume * _musicVolume;
                float elapsedTime = 0f;

                // 크로스페이드 실행
                while (elapsedTime < _crossfadeDuration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    elapsedTime += Time.deltaTime;
                    float progress = elapsedTime / _crossfadeDuration;
                    float curveValue = _crossfadeCurve.Evaluate(progress);

                    // 기존 음악은 페이드아웃, 새 음악은 페이드인
                    _musicSource.volume = targetVolume * (1f - curveValue);
                    _crossfadeSource.volume = targetVolume * curveValue;

                    await UniTask.Yield();
                }

                // 크로스페이드 완료 후 소스 교체
                _musicSource.Stop();
                (_musicSource, _crossfadeSource) = (_crossfadeSource, _musicSource);
                _currentMusicClip = newClip;

                OnCrossfadeCompleted?.Invoke();
                OnMusicChanged?.Invoke(newClip);
                JCDebug.Log($"[MusicManager] 크로스페이드 완료: {newClip.name}");
            }
            finally
            {
                _isCrossfading = false;
            }
        }

        /// <summary>
        /// 음악 페이드인
        /// </summary>
        public async UniTask FadeInMusicAsync(float duration, CancellationToken cancellationToken = default)
        {
            if (_musicSource == null || _isMusicFading) return;

            _isMusicFading = true;
            float targetVolume = _masterVolume * _musicVolume;
            _musicSource.volume = 0f;

            try
            {
                float elapsedTime = 0f;
                while (elapsedTime < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    elapsedTime += Time.deltaTime;
                    float progress = elapsedTime / duration;
                    float curveValue = _fadeCurve.Evaluate(progress);

                    _musicSource.volume = targetVolume * curveValue;
                    await UniTask.Yield();
                }

                _musicSource.volume = targetVolume;
            }
            finally
            {
                _isMusicFading = false;
            }
        }

        /// <summary>
        /// 음악 페이드아웃
        /// </summary>
        public async UniTask FadeOutMusicAsync(float duration, CancellationToken cancellationToken = default)
        {
            if (_musicSource == null || _isMusicFading) return;

            _isMusicFading = true;
            float startVolume = _musicSource.volume;

            try
            {
                float elapsedTime = 0f;
                while (elapsedTime < duration)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    elapsedTime += Time.deltaTime;
                    float progress = elapsedTime / duration;
                    float curveValue = _fadeCurve.Evaluate(1f - progress);

                    _musicSource.volume = startVolume * curveValue;
                    await UniTask.Yield();
                }

                _musicSource.volume = 0f;
                _musicSource.Stop();
            }
            finally
            {
                _isMusicFading = false;
            }
        }

        #endregion

        #region 씬 전환용 음악 제어

        /// <summary>
        /// 씬 전환 시 음악 처리
        /// </summary>
        public async UniTask TransitionMusicForSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            switch (sceneName.ToLower())
            {
                case "intro":
                    await PlayIntroMusicAsync(cancellationToken);
                    break;
                case "game":
                case "level1":
                case "level2":
                    await PlayGameMusicAsync(cancellationToken);
                    break;
                case "boss":
                    await PlayBossMusicAsync(cancellationToken);
                    break;
                default:
                    JCDebug.Log($"[MusicManager] 알 수 없는 씬: {sceneName}");
                    break;
            }
        }

        private async UniTask PlayIntroMusicAsync(CancellationToken cancellationToken)
        {
            // Intro 씬용 음악 로직
            JCDebug.Log("[MusicManager] Intro 음악 전환");
            // 실제 구현에서는 Resources.Load 등으로 클립 로드 후 재생
        }

        private async UniTask PlayGameMusicAsync(CancellationToken cancellationToken)
        {
            // Game 씬용 음악 로직
            JCDebug.Log("[MusicManager] Game 음악 전환");
            // 실제 구현에서는 현재 레벨에 맞는 음악 선택 로직
        }

        private async UniTask PlayBossMusicAsync(CancellationToken cancellationToken)
        {
            // Boss 전투용 음악 로직
            JCDebug.Log("[MusicManager] Boss 음악 전환");
            // 긴장감 있는 전투 음악으로 크로스페이드
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[MusicManager] 정리 시작");

            try
            {
                // 1. 진행 중인 음악 페이드/크로스페이드 정리
                if (_isMusicFading || _isCrossfading)
                {
                    JCDebug.Log("[MusicManager] 진행 중인 페이드 작업 대기 중...");

                    // 최대 3초까지 대기
                    int waitCount = 0;
                    while ((_isMusicFading || _isCrossfading) && waitCount < 30)
                    {
                        await UniTask.Delay(100);
                        waitCount++;
                    }

                    if (_isMusicFading || _isCrossfading)
                    {
                        JCDebug.Log("[MusicManager] 페이드 작업 강제 종료", JCDebug.LogLevel.Warning);
                        _isMusicFading = false;
                        _isCrossfading = false;
                    }
                }

                // 2. 모든 음악 소스 정지
                if (_musicSource != null)
                {
                    _musicSource.Stop();
                    _musicSource.clip = null;
                }

                if (_crossfadeSource != null)
                {
                    _crossfadeSource.Stop();
                    _crossfadeSource.clip = null;
                }

                // 3. 코루틴 정리
                StopAllCoroutines();

                // 4. AudioManager와의 이벤트 연결 해제
                // (AudioManager의 정리에서 이미 처리되므로 중복 해제 방지)

                // 5. 상태 초기화
                _currentMusicClip = null;
                _musicVolume = 1.0f;
                _masterVolume = 1.0f;
                _isMusicFading = false;
                _isCrossfading = false;

                // 6. 작업 완료 대기
                await UniTask.Delay(50);

                JCDebug.Log("[MusicManager] 정리 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[MusicManager] 정리 중 오류: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        #endregion

        #region 프로퍼티

        public float MusicVolume => _musicVolume;
        public bool IsMusicPlaying => _musicSource != null && _musicSource.isPlaying;
        public bool IsMusicFading => _isMusicFading;
        public bool IsCrossfading => _isCrossfading;
        public AudioClip CurrentMusic => _currentMusicClip;
        public float CurrentMusicTime => _musicSource != null ? _musicSource.time : 0f;
        public float CurrentMusicLength => _currentMusicClip != null ? _currentMusicClip.length : 0f;

        #endregion
    }
}