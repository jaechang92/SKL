using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Initialization;
using System;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    // ===== 1. AudioManager (기본 오디오 시스템 관리) =====

    /// <summary>
    /// 기본 오디오 시스템 관리 (SFX, Ambient, Volume 제어)
    /// 단일 책임: 기본 오디오 기능과 전역 볼륨 관리
    /// </summary>
    public class AudioManager : SingletonManager<AudioManager>, IInitializableAsync
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioSource _ambientSource;

        [Header("Volume Settings")]
        [SerializeField] private float _masterVolume = 1.0f;
        [SerializeField] private float _sfxVolume = 1.0f;
        [SerializeField] private float _ambientVolume = 0.5f;

        [Header("SFX Pool Settings")]
        [SerializeField] private int _sfxPoolSize = 10;
        [SerializeField] private AudioSource[] _sfxPool;

        // IManagerInitializable 구현
        public string Name => "AudioManager";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Normal;
        public bool IsInitialized { get; private set; } = false;

        // 이벤트 (옵저버 패턴)
        public event Action<float> OnMasterVolumeChanged;
        public event Action<float> OnSFXVolumeChanged;
        public event Action<float> OnAmbientVolumeChanged;

        // 다른 매니저와의 의존성
        private MusicManager _musicManager;

        #region IManagerInitializable 구현

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized) return;

            try
            {
                JCDebug.Log("[AudioManager] 오디오 시스템 초기화 시작");

                // 1. 오디오 소스 생성
                await CreateAudioSourcesAsync(cancellationToken);

                // 2. SFX 풀 생성
                await CreateSFXPoolAsync(cancellationToken);

                // 3. 볼륨 설정 적용
                ApplyVolumeSettings();

                // 4. MusicManager 연동 설정
                SetupMusicManagerIntegration();

                IsInitialized = true;
                JCDebug.Log("[AudioManager] 오디오 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[AudioManager] 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AudioManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        #endregion

        #region 초기화 메서드들

        private async UniTask CreateAudioSourcesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // SFX Source 생성
            if (_sfxSource == null)
            {
                GameObject sfxObj = new GameObject("SFXSource");
                sfxObj.transform.SetParent(transform);
                _sfxSource = sfxObj.AddComponent<AudioSource>();
                _sfxSource.loop = false;
                _sfxSource.playOnAwake = false;
                _sfxSource.priority = 128;

                // 프레임 분산을 위해 잠시 대기
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            // Ambient Source 생성
            if (_ambientSource == null)
            {
                GameObject ambientObj = new GameObject("AmbientSource");
                ambientObj.transform.SetParent(transform);
                _ambientSource = ambientObj.AddComponent<AudioSource>();
                _ambientSource.loop = true;
                _ambientSource.playOnAwake = false;
                _ambientSource.priority = 96;
            }

            JCDebug.Log("[AudioManager] 오디오 소스 생성 완료");
        }

        private async UniTask CreateSFXPoolAsync(CancellationToken cancellationToken)
        {
            _sfxPool = new AudioSource[_sfxPoolSize];

            for (int i = 0; i < _sfxPoolSize; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 메인 스레드에서 Unity 오브젝트 생성
                GameObject poolObj = new GameObject($"SFXPool_{i}");
                poolObj.transform.SetParent(transform);
                AudioSource poolSource = poolObj.AddComponent<AudioSource>();
                // ... 설정

                _sfxPool[i] = poolSource;

                // 3개마다 한 프레임 대기 (부하 분산)
                if (i % 3 == 0)
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private void SetupMusicManagerIntegration()
        {
            // MusicManager와 연동 설정
            _musicManager = MusicManager.Instance;
            if (_musicManager != null)
            {
                // MusicManager의 볼륨 변경 이벤트 구독
                OnMasterVolumeChanged += _musicManager.OnMasterVolumeUpdated;
            }
        }

        #endregion

        #region Volume 제어 (전역 볼륨 관리)

        public void SetMasterVolume(float volume)
        {
            _masterVolume = Mathf.Clamp01(volume);
            ApplyVolumeSettings();
            OnMasterVolumeChanged?.Invoke(_masterVolume);

            JCDebug.Log($"[AudioManager] 마스터 볼륨 설정: {_masterVolume:F2}");
        }

        public void SetSFXVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
            UpdateSFXVolume();
            OnSFXVolumeChanged?.Invoke(_sfxVolume);
        }

        public void SetAmbientVolume(float volume)
        {
            _ambientVolume = Mathf.Clamp01(volume);
            UpdateAmbientVolume();
            OnAmbientVolumeChanged?.Invoke(_ambientVolume);
        }

        private void ApplyVolumeSettings()
        {
            UpdateSFXVolume();
            UpdateAmbientVolume();
        }

        private void UpdateSFXVolume()
        {
            float finalVolume = _masterVolume * _sfxVolume;

            if (_sfxSource != null)
                _sfxSource.volume = finalVolume;

            // SFX 풀의 모든 AudioSource 볼륨 업데이트
            if (_sfxPool != null)
            {
                foreach (var poolSource in _sfxPool)
                {
                    if (poolSource != null)
                        poolSource.volume = finalVolume;
                }
            }
        }

        private void UpdateAmbientVolume()
        {
            if (_ambientSource != null)
                _ambientSource.volume = _masterVolume * _ambientVolume;
        }

        #endregion

        #region SFX 제어 (오브젝트 풀 패턴 적용)

        /// <summary>
        /// SFX 재생 (기본)
        /// </summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null)
            {
                JCDebug.Log("[AudioManager] SFX 클립이 null입니다.", JCDebug.LogLevel.Warning);
                return;
            }

            // 사용 가능한 AudioSource 찾기
            AudioSource availableSource = GetAvailableSFXSource();
            if (availableSource != null)
            {
                availableSource.PlayOneShot(clip, volumeScale);
            }
            else
            {
                // 풀이 가득 찬 경우 기본 SFX 소스 사용
                _sfxSource.PlayOneShot(clip, volumeScale);
            }
        }

        /// <summary>
        /// SFX 비동기 재생 (재생 완료까지 대기)
        /// </summary>
        public async UniTask PlaySFXAsync(AudioClip clip, float volumeScale = 1f, CancellationToken cancellationToken = default)
        {
            if (clip == null) return;

            PlaySFX(clip, volumeScale);

            // 클립 재생 완료까지 대기
            await UniTask.Delay(TimeSpan.FromSeconds(clip.length).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
        }

        /// <summary>
        /// 위치 기반 SFX 재생 (3D 사운드)
        /// </summary>
        public void PlaySFX3D(AudioClip clip, Vector3 position, float volumeScale = 1f)
        {
            if (clip == null) return;

            AudioSource availableSource = GetAvailableSFXSource();
            if (availableSource != null)
            {
                availableSource.transform.position = position;
                availableSource.spatialBlend = 1f; // 3D 사운드
                availableSource.PlayOneShot(clip, volumeScale);

                // 재생 완료 후 위치 초기화
                ResetSFXSourceAsync(availableSource, clip.length).Forget();
            }
        }

        private AudioSource GetAvailableSFXSource()
        {
            if (_sfxPool == null) return null;

            foreach (var source in _sfxPool)
            {
                if (source != null && !source.isPlaying)
                {
                    return source;
                }
            }
            return null;
        }

        private async UniTaskVoid ResetSFXSourceAsync(AudioSource source, float delay)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay).Milliseconds);
            if (source != null)
            {
                source.transform.position = transform.position;
                source.spatialBlend = 0f; // 2D 사운드로 복구
            }
        }

        #endregion

        #region Ambient 제어

        public void PlayAmbient(AudioClip clip)
        {
            if (clip == null || _ambientSource == null) return;

            _ambientSource.clip = clip;
            _ambientSource.Play();

            JCDebug.Log($"[AudioManager] Ambient 재생: {clip.name}");
        }

        public void StopAmbient()
        {
            if (_ambientSource != null)
            {
                _ambientSource.Stop();
                JCDebug.Log("[AudioManager] Ambient 정지");
            }
        }

        public async UniTask FadeAmbientAsync(float targetVolume, float duration, CancellationToken cancellationToken = default)
        {
            if (_ambientSource == null) return;

            float startVolume = _ambientSource.volume;
            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / duration;
                _ambientSource.volume = Mathf.Lerp(startVolume, targetVolume, progress);

                await UniTask.Yield();
            }

            _ambientSource.volume = targetVolume;
        }

        #endregion

        #region 프로퍼티

        public float MasterVolume => _masterVolume;
        public float SFXVolume => _sfxVolume;
        public float AmbientVolume => _ambientVolume;
        public bool IsAmbientPlaying => _ambientSource != null && _ambientSource.isPlaying;

        #endregion
    }

    
}

/* 
=== 분리 후 Using 구문 ===
using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using CustomDebug;

=== 분리의 장점 ===
1. **단일 책임 원칙 준수**: 각 클래스가 명확한 역할 분담
2. **유지보수성 향상**: 음악/SFX 기능을 독립적으로 수정 가능
3. **확장성 증대**: 각 시스템을 독립적으로 확장 가능
4. **성능 최적화**: 필요한 기능만 초기화 가능
5. **테스트 용이성**: 각 매니저를 개별적으로 테스트 가능

=== 사용 방법 ===
// SFX 재생
AudioManager.Instance.PlaySFX(jumpSound);

// 3D 사운드 재생  
AudioManager.Instance.PlaySFX3D(footstepSound, playerPosition);

// 음악 재생 (페이드인)
await MusicManager.Instance.PlayMusicAsync(bgmClip, fadeIn: true);

// 음악 크로스페이드
await MusicManager.Instance.CrossfadeMusicAsync(bossMusic);

// 볼륨 제어
AudioManager.Instance.SetMasterVolume(0.8f);
MusicManager.Instance.SetMusicVolume(0.6f);

=== UnifiedGameManager에 등록 ===
RegisterManager<AudioManager>("Audio", InitializationPriority.Normal);
RegisterManager<MusicManager>("Audio", InitializationPriority.Normal);
*/