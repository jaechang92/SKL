using System;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;

namespace Metamorph.Managers
{
    [System.Serializable]
    public class GameSettings
    {
        public float masterVolume = 1.0f;
        public float musicVolume = 0.8f;
        public float sfxVolume = 0.9f;
        public float ambientVolume = 0.5f;
        public bool fullscreen = true;
        public SerializableVector2Int resolution = new SerializableVector2Int(1920, 1080);
        public bool vsync = true;
        public string language = "Korean";
        public int qualityLevel = 2;
        public float brightness = 1.0f;

        // 게임 특화 설정
        public bool showDamageNumbers = true;
        public bool enableScreenShake = true;
        public float gameSpeed = 1.0f;
        public bool showTutorial = true;
        public bool autoSave = true;
        public float autoSaveInterval = 300f; // 5분
    }

    [System.Serializable]
    public class SerializableVector2Int
    {
        public int x, y;
        public SerializableVector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
        }
    }

    /// <summary>
    /// UniTask 기반 게임 설정 매니저 (싱글톤)
    /// </summary>
    public class UniTaskGameSettingsManager : SingletonManager<UniTaskGameSettingsManager>, IInitializableAsync
    {
        public string Name => "Game Settings Manager";

        public InitializationPriority Priority { get; set; } = InitializationPriority.High;


        public bool IsInitialized { get; private set; }

        private GameSettings currentSettings;
        private const string SETTINGS_FILE_NAME = "GameSettings.json";

        [Header("Settings Configuration")]
        [SerializeField] private bool _autoApplyOnLoad = true;
        [SerializeField] private bool _validateSettings = true;
        [SerializeField] private bool _backupSettings = true;

        public GameSettings Settings => currentSettings;

        // 이벤트
        public event Action<GameSettings> OnSettingsLoaded;
        public event Action<GameSettings> OnSettingsChanged;
        public event Action<GameSettings> OnSettingsSaved;
        public event Action<string> OnSettingsValidationFailed;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UniTaskGameSettingsManager] 게임 설정 매니저 초기화 시작");

                // 설정 로드
                await LoadSettingsAsync(cancellationToken);

                // 설정 검증
                if (_validateSettings)
                {
                    await ValidateSettingsAsync(cancellationToken);
                }

                // 자동 적용
                if (_autoApplyOnLoad)
                {
                    await ApplySettingsAsync(cancellationToken);
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskGameSettingsManager] 게임 설정 매니저 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskGameSettingsManager] 설정 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 설정 초기화 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask LoadSettingsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 로딩 시뮬레이션
                await UniTask.Delay(TimeSpan.FromSeconds(0.3f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

                string settingsPath = Path.Combine(Application.persistentDataPath, SETTINGS_FILE_NAME);

                if (File.Exists(settingsPath))
                {
                    string jsonData = await ReadFileAsync(settingsPath, cancellationToken);
                    currentSettings = JsonUtility.FromJson<GameSettings>(jsonData);
                    JCDebug.Log("[UniTaskGameSettingsManager] 설정 파일 로드 완료");
                }
                else
                {
                    currentSettings = CreateDefaultSettings();
                    JCDebug.Log("[UniTaskGameSettingsManager] 기본 설정 생성");
                }

                OnSettingsLoaded?.Invoke(currentSettings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 설정 로드 실패: {ex.Message}",JCDebug.LogLevel.Error);

                // 기본 설정으로 폴백
                currentSettings = CreateDefaultSettings();
                OnSettingsLoaded?.Invoke(currentSettings);
            }
        }

        public async UniTask SaveSettingsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (currentSettings == null)
                {
                    JCDebug.Log("[UniTaskGameSettingsManager] 저장할 설정이 없습니다.", JCDebug.LogLevel.Warning);
                    return;
                }

                // 백업 생성
                if (_backupSettings)
                {
                    await CreateBackupAsync(cancellationToken);
                }

                string jsonData = JsonUtility.ToJson(currentSettings, true);
                string settingsPath = Path.Combine(Application.persistentDataPath, SETTINGS_FILE_NAME);

                await WriteFileAsync(settingsPath, jsonData, cancellationToken);

                OnSettingsSaved?.Invoke(currentSettings);
                JCDebug.Log("[UniTaskGameSettingsManager] 설정 저장 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskGameSettingsManager] 설정 저장이 취소됨",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 설정 저장 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask ValidateSettingsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(0.1f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

                if (currentSettings == null)
                {
                    throw new Exception("설정이 null입니다.");
                }

                // 볼륨 범위 검증
                if (!IsVolumeValid(currentSettings.masterVolume) ||
                    !IsVolumeValid(currentSettings.musicVolume) ||
                    !IsVolumeValid(currentSettings.sfxVolume) ||
                    !IsVolumeValid(currentSettings.ambientVolume))
                {
                    OnSettingsValidationFailed?.Invoke("볼륨 설정이 유효하지 않습니다.");
                    FixVolumeSettings();
                }

                // 해상도 검증
                if (currentSettings.resolution.x <= 0 || currentSettings.resolution.y <= 0)
                {
                    OnSettingsValidationFailed?.Invoke("해상도 설정이 유효하지 않습니다.");
                    currentSettings.resolution = new SerializableVector2Int(1920, 1080);
                }

                // 게임 속도 검증
                if (currentSettings.gameSpeed <= 0 || currentSettings.gameSpeed > 3f)
                {
                    OnSettingsValidationFailed?.Invoke("게임 속도 설정이 유효하지 않습니다.");
                    currentSettings.gameSpeed = 1.0f;
                }

                JCDebug.Log("[UniTaskGameSettingsManager] 설정 검증 완료");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 설정 검증 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask ApplySettingsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (currentSettings == null)
                {
                    JCDebug.Log("[UniTaskGameSettingsManager] 적용할 설정이 없습니다.",JCDebug.LogLevel.Warning);
                    return;
                }

                // 오디오 설정 적용
                await ApplyAudioSettingsAsync(cancellationToken);

                // 그래픽 설정 적용
                await ApplyGraphicsSettingsAsync(cancellationToken);

                // 게임플레이 설정 적용
                await ApplyGameplaySettingsAsync(cancellationToken);

                OnSettingsChanged?.Invoke(currentSettings);
                JCDebug.Log("[UniTaskGameSettingsManager] 설정 적용 완료");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 설정 적용 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask ApplyAudioSettingsAsync(CancellationToken cancellationToken)
        {
            var audioManager = UniTaskAudioManager.Instance;
            if (audioManager != null && audioManager.IsInitialized)
            {
                audioManager.SetMasterVolume(currentSettings.masterVolume);
                audioManager.SetMusicVolume(currentSettings.musicVolume);
                audioManager.SetSFXVolume(currentSettings.sfxVolume);
                audioManager.SetAmbientVolume(currentSettings.ambientVolume);
            }
            await UniTask.Yield(cancellationToken);
        }

        private async UniTask ApplyGraphicsSettingsAsync(CancellationToken cancellationToken)
        {
            // 해상도 설정
            Screen.SetResolution(
                currentSettings.resolution.x,
                currentSettings.resolution.y,
                currentSettings.fullscreen
            );

            // V-Sync 설정
            QualitySettings.vSyncCount = currentSettings.vsync ? 1 : 0;

            // 품질 설정
            QualitySettings.SetQualityLevel(currentSettings.qualityLevel);

            await UniTask.Yield(cancellationToken);
        }

        private async UniTask ApplyGameplaySettingsAsync(CancellationToken cancellationToken)
        {
            // 게임 속도 설정
            Time.timeScale = currentSettings.gameSpeed;

            // 다른 게임플레이 설정들 적용
            await UniTask.Yield(cancellationToken);
        }

        private GameSettings CreateDefaultSettings()
        {
            return new GameSettings
            {
                masterVolume = 1.0f,
                musicVolume = 0.8f,
                sfxVolume = 0.9f,
                ambientVolume = 0.5f,
                fullscreen = true,
                resolution = new SerializableVector2Int(1920, 1080),
                vsync = true,
                language = "Korean",
                qualityLevel = 2,
                brightness = 1.0f,
                showDamageNumbers = true,
                enableScreenShake = true,
                gameSpeed = 1.0f,
                showTutorial = true,
                autoSave = true,
                autoSaveInterval = 300f
            };
        }

        private bool IsVolumeValid(float volume)
        {
            return volume >= 0f && volume <= 1f;
        }

        private void FixVolumeSettings()
        {
            currentSettings.masterVolume = Mathf.Clamp01(currentSettings.masterVolume);
            currentSettings.musicVolume = Mathf.Clamp01(currentSettings.musicVolume);
            currentSettings.sfxVolume = Mathf.Clamp01(currentSettings.sfxVolume);
            currentSettings.ambientVolume = Mathf.Clamp01(currentSettings.ambientVolume);
        }

        private async UniTask<string> ReadFileAsync(string path, CancellationToken cancellationToken)
        {
            // 파일 읽기를 비동기로 처리
            await UniTask.SwitchToThreadPool();
            string content = File.ReadAllText(path);
            await UniTask.SwitchToMainThread(cancellationToken);
            return content;
        }

        private async UniTask WriteFileAsync(string path, string content, CancellationToken cancellationToken)
        {
            // 파일 쓰기를 비동기로 처리
            await UniTask.SwitchToThreadPool();
            File.WriteAllText(path, content);
            await UniTask.SwitchToMainThread(cancellationToken);
        }

        private async UniTask CreateBackupAsync(CancellationToken cancellationToken)
        {
            try
            {
                string settingsPath = Path.Combine(Application.persistentDataPath, SETTINGS_FILE_NAME);
                string backupPath = Path.Combine(Application.persistentDataPath, $"Backup_{SETTINGS_FILE_NAME}");

                if (File.Exists(settingsPath))
                {
                    await UniTask.SwitchToThreadPool();
                    File.Copy(settingsPath, backupPath, true);
                    await UniTask.SwitchToMainThread(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsManager] 백업 생성 실패: {ex.Message}", JCDebug.LogLevel.Warning);
            }
        }

        // 편의 메서드들
        public void UpdateVolumeSetting(string volumeType, float value)
        {
            if (currentSettings == null) return;

            switch (volumeType.ToLower())
            {
                case "master":
                    currentSettings.masterVolume = Mathf.Clamp01(value);
                    break;
                case "music":
                    currentSettings.musicVolume = Mathf.Clamp01(value);
                    break;
                case "sfx":
                    currentSettings.sfxVolume = Mathf.Clamp01(value);
                    break;
                case "ambient":
                    currentSettings.ambientVolume = Mathf.Clamp01(value);
                    break;
            }
        }

        public void UpdateResolutionSetting(int width, int height, bool fullscreen)
        {
            if (currentSettings == null) return;

            currentSettings.resolution.x = width;
            currentSettings.resolution.y = height;
            currentSettings.fullscreen = fullscreen;
        }
    }
}