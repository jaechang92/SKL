using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;

namespace Metamorph.Managers
{
    /// <summary>
    /// UniTask 기반 오디오 매니저 (싱글톤)
    /// </summary>
    public class UniTaskAudioManager : SingletonManager<UniTaskAudioManager>, IInitializableAsync
    {
        public string Name => "Audio Manager";
        public InitializationPriority Priority => InitializationPriority.Normal;
        public bool IsInitialized { get; private set; }

        [Header("Audio Sources")]
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource ambientSource;

        [Header("Audio Settings")]
        [SerializeField] private float masterVolume = 1.0f;
        [SerializeField] private float musicVolume = 1.0f;
        [SerializeField] private float sfxVolume = 1.0f;
        [SerializeField] private float ambientVolume = 0.5f;

        [Header("Fade Settings")]
        [SerializeField] private float musicFadeDuration = 1.0f;
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        // 현재 재생 중인 음악 정보
        private AudioClip currentMusicClip;
        private bool isMusicFading = false;

        // 이벤트
        public event Action<float> OnMasterVolumeChanged;
        public event Action<float> OnMusicVolumeChanged;
        public event Action<float> OnSFXVolumeChanged;
        public event Action<AudioClip> OnMusicChanged;

        protected override void OnCreated()
        {
            base.OnCreated();
            CreateAudioSources();
        }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UniTaskAudioManager] 오디오 시스템 초기화 시작");

                // AudioSource가 없다면 생성
                if (musicSource == null || sfxSource == null || ambientSource == null)
                {
                    CreateAudioSources();
                }

                // 오디오 설정 초기화
                await InitializeAudioSettingsAsync(cancellationToken);

                // 기본 볼륨 설정 적용
                UpdateAllVolumes();

                IsInitialized = true;
                JCDebug.Log("[UniTaskAudioManager] 오디오 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskAudioManager] 오디오 초기화가 취소됨",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskAudioManager] 오디오 초기화 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        private void CreateAudioSources()
        {
            // Music Source
            if (musicSource == null)
            {
                GameObject musicObj = new GameObject("MusicSource");
                musicObj.transform.SetParent(transform);
                musicSource = musicObj.AddComponent<AudioSource>();
                musicSource.loop = true;
                musicSource.playOnAwake = false;
                musicSource.priority = 64;
            }

            // SFX Source
            if (sfxSource == null)
            {
                GameObject sfxObj = new GameObject("SFXSource");
                sfxObj.transform.SetParent(transform);
                sfxSource = sfxObj.AddComponent<AudioSource>();
                sfxSource.loop = false;
                sfxSource.playOnAwake = false;
                sfxSource.priority = 128;
            }

            // Ambient Source
            if (ambientSource == null)
            {
                GameObject ambientObj = new GameObject("AmbientSource");
                ambientObj.transform.SetParent(transform);
                ambientSource = ambientObj.AddComponent<AudioSource>();
                ambientSource.loop = true;
                ambientSource.playOnAwake = false;
                ambientSource.priority = 96;
            }
        }

        private async UniTask InitializeAudioSettingsAsync(CancellationToken cancellationToken)
        {
            // 오디오 설정 로드 (필요시)
            await UniTask.Delay(TimeSpan.FromSeconds(0.1f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

            // 오디오 믹서 설정이나 다른 초기화 작업
            JCDebug.Log("[UniTaskAudioManager] 오디오 설정 로드 완료");
        }

        #region Volume Control

        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            UpdateAllVolumes();
            OnMasterVolumeChanged?.Invoke(masterVolume);
        }

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            UpdateMusicVolume();
            OnMusicVolumeChanged?.Invoke(musicVolume);
        }

        public void SetSFXVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            UpdateSFXVolume();
            OnSFXVolumeChanged?.Invoke(sfxVolume);
        }

        public void SetAmbientVolume(float volume)
        {
            ambientVolume = Mathf.Clamp01(volume);
            UpdateAmbientVolume();
        }

        private void UpdateAllVolumes()
        {
            UpdateMusicVolume();
            UpdateSFXVolume();
            UpdateAmbientVolume();
        }

        private void UpdateMusicVolume()
        {
            if (musicSource != null)
                musicSource.volume = masterVolume * musicVolume;
        }

        private void UpdateSFXVolume()
        {
            if (sfxSource != null)
                sfxSource.volume = masterVolume * sfxVolume;
        }

        private void UpdateAmbientVolume()
        {
            if (ambientSource != null)
                ambientSource.volume = masterVolume * ambientVolume;
        }

        #endregion

        #region Music Control

        public async UniTask PlayMusicAsync(AudioClip clip, bool fadeIn = true, CancellationToken cancellationToken = default)
        {
            if (clip == null)
            {
                JCDebug.Log("[UniTaskAudioManager] 재생할 음악 클립이 null입니다.",JCDebug.LogLevel.Warning);
                return;
            }

            // 같은 클립이 이미 재생 중이면 무시
            if (currentMusicClip == clip && musicSource.isPlaying)
            {
                return;
            }

            try
            {
                if (musicSource.isPlaying && fadeIn)
                {
                    // 현재 음악을 페이드아웃하고 새 음악을 페이드인
                    await CrossfadeMusicAsync(clip, cancellationToken);
                }
                else
                {
                    // 직접 재생
                    musicSource.clip = clip;
                    musicSource.Play();
                    currentMusicClip = clip;

                    if (fadeIn)
                    {
                        await FadeInMusicAsync(cancellationToken);
                    }
                }

                OnMusicChanged?.Invoke(clip);
                JCDebug.Log($"[UniTaskAudioManager] 음악 재생: {clip.name}");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskAudioManager] 음악 재생이 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
        }

        public async UniTask StopMusicAsync(bool fadeOut = true, CancellationToken cancellationToken = default)
        {
            if (!musicSource.isPlaying) return;

            try
            {
                if (fadeOut)
                {
                    await FadeOutMusicAsync(cancellationToken);
                }
                else
                {
                    musicSource.Stop();
                }

                currentMusicClip = null;
                JCDebug.Log("[UniTaskAudioManager] 음악 정지");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskAudioManager] 음악 정지가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
        }

        private async UniTask CrossfadeMusicAsync(AudioClip newClip, CancellationToken cancellationToken)
        {
            isMusicFading = true;

            // 페이드아웃과 새 음악 준비를 동시에 진행
            var fadeOutTask = FadeOutMusicAsync(cancellationToken);

            // 페이드아웃 완료 후 새 음악 시작
            await fadeOutTask;

            musicSource.clip = newClip;
            musicSource.Play();
            currentMusicClip = newClip;

            await FadeInMusicAsync(cancellationToken);

            isMusicFading = false;
        }

        private async UniTask FadeInMusicAsync(CancellationToken cancellationToken)
        {
            float targetVolume = masterVolume * musicVolume;
            musicSource.volume = 0f;

            float elapsedTime = 0f;
            while (elapsedTime < musicFadeDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / musicFadeDuration;
                float curveValue = fadeCurve.Evaluate(normalizedTime);

                musicSource.volume = targetVolume * curveValue;

                await UniTask.Yield();
            }

            musicSource.volume = targetVolume;
        }

        private async UniTask FadeOutMusicAsync(CancellationToken cancellationToken)
        {
            float startVolume = musicSource.volume;

            float elapsedTime = 0f;
            while (elapsedTime < musicFadeDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();

                elapsedTime += Time.deltaTime;
                float normalizedTime = elapsedTime / musicFadeDuration;
                float curveValue = fadeCurve.Evaluate(1f - normalizedTime);

                musicSource.volume = startVolume * curveValue;

                await UniTask.Yield();
            }

            musicSource.volume = 0f;
            musicSource.Stop();
        }

        #endregion

        #region SFX Control

        public void PlaySFX(AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null)
            {
                JCDebug.Log("[UniTaskAudioManager] 재생할 SFX 클립이 null입니다.", JCDebug.LogLevel.Warning);
                return;
            }

            sfxSource.PlayOneShot(clip, volumeScale);
        }

        public async UniTask PlaySFXAsync(AudioClip clip, float volumeScale = 1f, CancellationToken cancellationToken = default)
        {
            if (clip == null)
            {
                JCDebug.Log("[UniTaskAudioManager] 재생할 SFX 클립이 null입니다.", JCDebug.LogLevel.Warning);
                return;
            }

            PlaySFX(clip, volumeScale);

            // 클립 재생 완료까지 대기
            await UniTask.Delay(TimeSpan.FromSeconds(clip.length).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
        }

        #endregion

        #region Ambient Control

        public void PlayAmbient(AudioClip clip)
        {
            if (clip == null) return;

            ambientSource.clip = clip;
            ambientSource.Play();
        }

        public void StopAmbient()
        {
            ambientSource.Stop();
        }

        #endregion

        #region Public Properties

        public float MasterVolume => masterVolume;
        public float MusicVolume => musicVolume;
        public float SFXVolume => sfxVolume;
        public float AmbientVolume => ambientVolume;
        public bool IsMusicPlaying => musicSource != null && musicSource.isPlaying;
        public bool IsMusicFading => isMusicFading;
        public AudioClip CurrentMusic => currentMusicClip;

        #endregion
    }
}